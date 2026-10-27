using Silk.NET.OpenAL;

namespace Slrr.App;

/// <summary>
/// Real-time OpenAL output for the interactive app. Unlike <c>Slrr.Audio.OpenAL.OpenAlSink</c> (which blocks until a buffer is free, pacing
/// a headless producer), <see cref="Write"/> never blocks the frame loop: the host produces exactly one host step of sound per step (GameAudioSink),
/// chunks are queued on one streaming source, a chunk is dropped when more than <see cref="MaxLatency"/> seconds are already queued, and after an
/// underrun the source restarts once <see cref="StartChunks"/> chunks are queued again.
/// Device order: the bundled OpenAL Soft (Silk.NET.OpenAL.Soft.Native: soft_oal.dll / libopenal.so / libopenal.dylib), then the system
/// library (Windows openal32.dll, Linux libopenal.so.1, macOS /System/Library/Frameworks/OpenAL.framework). <see cref="TryCreate"/> returns
/// null when neither opens a device; the caller falls back to a NullSink and logs why. UNTESTED with a real device (none in the build sandbox).
/// </summary>
public sealed unsafe class RealtimeAudioSink : Slrr.Audio.IAudioSink
{
    public const int MaxBuffers = 16;
    public const int StartChunks = 2;
    public const double MaxLatency = 0.25;

    private readonly AL _al;
    private readonly ALContext _alc;
    private readonly Device* _device;
    private readonly Context* _context;
    private readonly uint _source;
    private readonly List<uint> _all = [];
    private readonly Queue<uint> _free = new();
    private readonly Queue<int> _queuedFrames = new();
    private bool _started;
    private bool _disposed;

    private RealtimeAudioSink(int sampleRate, AL al, ALContext alc, Device* device, Context* context, string backend)
    {
        SampleRate = sampleRate;
        _al = al;
        _alc = alc;
        _device = device;
        _context = context;
        Backend = backend;
        _source = al.GenSource();
    }

    public int SampleRate { get; }

    public string Backend { get; }

    public int Underruns { get; private set; }

    public int DroppedChunks { get; private set; }

    public long FramesWritten { get; private set; }

    /// <summary>Seconds of sound queued on the device (latency).</summary>
    public double QueuedSeconds => _queuedFrames.Sum() / (double)SampleRate;

    public static RealtimeAudioSink? TryCreate(int sampleRate, Action<string> log)
    {
        ArgumentNullException.ThrowIfNull(log);
        foreach (var soft in new[] { true, false })
        {
            var name = soft ? "OpenAL Soft (bundled)" : "system OpenAL";
            ALContext? alc = null;
            AL? al = null;
            try
            {
                alc = ALContext.GetApi(soft);
                al = AL.GetApi(soft);
                var device = alc.OpenDevice(null!); // NULL = the default device ("" is a device name to some implementations; GUESSED safer)
                if (device == null)
                {
                    log($"audio: {name} loaded but no output device could be opened");
                    al.Dispose();
                    alc.Dispose();
                    continue;
                }

                var context = alc.CreateContext(device, null);
                if (context == null || !alc.MakeContextCurrent(context))
                {
                    log($"audio: {name}: no context on the default device");
                    if (context != null)
                    {
                        alc.DestroyContext(context);
                    }

                    alc.CloseDevice(device);
                    al.Dispose();
                    alc.Dispose();
                    continue;
                }

                var deviceName = alc.GetContextProperty(device, GetContextString.DeviceSpecifier) ?? "default device";
                return new RealtimeAudioSink(sampleRate, al, alc, device, context, $"{name}: {deviceName}");
            }
            catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or NotSupportedException
                                          or InvalidOperationException or FileNotFoundException or TypeInitializationException)
            {
                log($"audio: {name} not available ({e.GetType().Name}: {e.Message.Split('\n')[0]})");
                al?.Dispose();
                alc?.Dispose();
            }
        }

        return null;
    }

    public void Write(ReadOnlySpan<float> interleavedStereo)
    {
        if (_disposed || interleavedStereo.Length < 2)
        {
            return;
        }

        Reclaim();
        var frames = interleavedStereo.Length / 2;
        if (QueuedSeconds > MaxLatency)
        {
            DroppedChunks++;
            return;
        }

        if (_free.Count == 0)
        {
            if (_all.Count >= MaxBuffers)
            {
                DroppedChunks++;
                return;
            }

            var nb = _al.GenBuffer();
            _all.Add(nb);
            _free.Enqueue(nb);
        }

        var pcm = new short[frames * 2];
        for (var i = 0; i < pcm.Length; i++)
        {
            var v = interleavedStereo[i];
            pcm[i] = (short)Math.Clamp(MathF.Round((float.IsFinite(v) ? v : 0f) * 32767f), -32768f, 32767f);
        }

        var buf = _free.Dequeue();
        fixed (short* p = pcm)
        {
            _al.BufferData(buf, BufferFormat.Stereo16, p, pcm.Length * sizeof(short), SampleRate);
        }

        _al.SourceQueueBuffers(_source, 1, &buf);
        _queuedFrames.Enqueue(frames);
        FramesWritten += frames;
        _al.GetSourceProperty(_source, GetSourceInteger.SourceState, out var state);
        if ((SourceState)state != SourceState.Playing && _queuedFrames.Count >= StartChunks)
        {
            if (_started)
            {
                Underruns++;
            }

            _started = true;
            _al.SourcePlay(_source);
        }
    }

    /// <summary>Stops the sound now (pause); queued chunks are discarded.</summary>
    public void Flush()
    {
        if (_disposed)
        {
            return;
        }

        _al.SourceStop(_source);
        Reclaim();
        _started = false;
    }

    private void Reclaim()
    {
        _al.GetSourceProperty(_source, GetSourceInteger.BuffersProcessed, out var processed);
        for (var i = 0; i < processed; i++)
        {
            uint b = 0;
            _al.SourceUnqueueBuffers(_source, 1, &b);
            _free.Enqueue(b);
            if (_queuedFrames.Count > 0)
            {
                _queuedFrames.Dequeue();
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _al.SourceStop(_source);
        _al.DeleteSource(_source);
        foreach (var b in _all)
        {
            _al.DeleteBuffer(b);
        }

        _alc.MakeContextCurrent(null);
        _alc.DestroyContext(_context);
        _alc.CloseDevice(_device);
        _al.Dispose();
        _alc.Dispose();
    }
}
