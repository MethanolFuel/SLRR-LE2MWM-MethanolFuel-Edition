using Silk.NET.OpenAL;

namespace Slrr.Audio.OpenAL;

/// <summary>
/// Streams the mixer output to the default OpenAL device (queued 16-bit stereo buffers on one source). The mixing itself stays in
/// <see cref="AudioMixer"/>; OpenAL is only the output device, so results are identical to the WAV sink.
/// UNTESTED: this build environment has no audio device, so only compilation was verified. <see cref="TryCreate"/> returns null
/// when no device/context can be opened.
/// </summary>
public sealed unsafe class OpenAlSink : IAudioSink
{
    private const int BufferCount = 4;

    private readonly AL _al;
    private readonly ALContext _alc;
    private readonly Device* _device;
    private readonly Context* _context;
    private readonly uint _source;
    private readonly uint[] _buffers = new uint[BufferCount];
    private readonly Queue<uint> _free = new();
    private bool _disposed;

    private OpenAlSink(int sampleRate, AL al, ALContext alc, Device* device, Context* context)
    {
        SampleRate = sampleRate;
        _al = al;
        _alc = alc;
        _device = device;
        _context = context;
        _source = al.GenSource();
        for (var i = 0; i < BufferCount; i++)
        {
            _buffers[i] = al.GenBuffer();
            _free.Enqueue(_buffers[i]);
        }
    }

    public int SampleRate { get; }

    /// <summary>Opens the default device; null if OpenAL is not installed or no device is available.</summary>
    public static OpenAlSink? TryCreate(int sampleRate)
    {
        try
        {
            var alc = ALContext.GetApi();
            var al = AL.GetApi();
            var device = alc.OpenDevice(string.Empty);
            if (device == null)
            {
                return null;
            }

            var context = alc.CreateContext(device, null);
            if (context == null)
            {
                alc.CloseDevice(device);
                return null;
            }

            alc.MakeContextCurrent(context);
            return new OpenAlSink(sampleRate, al, alc, device, context);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or NotSupportedException)
        {
            return null;
        }
    }

    public void Write(ReadOnlySpan<float> interleavedStereo)
    {
        var pcm = new short[interleavedStereo.Length];
        for (var i = 0; i < pcm.Length; i++)
        {
            pcm[i] = (short)Math.Clamp(MathF.Round(interleavedStereo[i] * 32767f), -32768f, 32767f);
        }

        // Reclaim buffers OpenAL has finished with; block (politely) until one is free so the caller is paced by the device.
        while (true)
        {
            _al.GetSourceProperty(_source, GetSourceInteger.BuffersProcessed, out var processed);
            for (var i = 0; i < processed; i++)
            {
                uint b = 0;
                _al.SourceUnqueueBuffers(_source, 1, &b);
                _free.Enqueue(b);
            }

            if (_free.Count > 0)
            {
                break;
            }

            Thread.Sleep(2);
        }

        var buf = _free.Dequeue();
        fixed (short* p = pcm)
        {
            _al.BufferData(buf, BufferFormat.Stereo16, p, pcm.Length * sizeof(short), SampleRate);
        }

        _al.SourceQueueBuffers(_source, 1, &buf);
        _al.GetSourceProperty(_source, GetSourceInteger.SourceState, out var state);
        if ((SourceState)state != SourceState.Playing)
        {
            _al.SourcePlay(_source);
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
        foreach (var b in _buffers)
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
