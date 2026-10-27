namespace Slrr.Audio;

/// <summary>Where the mixer's interleaved stereo float output goes (a file, a device, a test buffer).</summary>
public interface IAudioSink : IDisposable
{
    int SampleRate { get; }

    /// <summary>Consumes interleaved stereo frames in [-1, 1]. May block (device sinks) to pace the caller.</summary>
    void Write(ReadOnlySpan<float> interleavedStereo);
}

/// <summary>Collects everything in memory; writes a 16-bit stereo WAV on <see cref="Dispose"/> when a path was given.</summary>
public sealed class WavFileSink : IAudioSink
{
    private readonly string? _path;
    private readonly List<float> _left = [];
    private readonly List<float> _right = [];
    private bool _disposed;

    public WavFileSink(int sampleRate, string? path = null)
    {
        SampleRate = sampleRate;
        _path = path;
    }

    public int SampleRate { get; }

    public int Frames => _left.Count;

    public void Write(ReadOnlySpan<float> interleavedStereo)
    {
        for (var i = 0; i + 1 < interleavedStereo.Length; i += 2)
        {
            _left.Add(interleavedStereo[i]);
            _right.Add(interleavedStereo[i + 1]);
        }
    }

    public WavClip ToClip() => new(SampleRate, [_left.ToArray(), _right.ToArray()]);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_path is not null)
        {
            WavFile.Save(_path, ToClip());
        }
    }
}

/// <summary>Discards audio (headless runs that only need the mixer to advance).</summary>
public sealed class NullSink(int sampleRate) : IAudioSink
{
    public int SampleRate { get; } = sampleRate;

    public long FramesWritten { get; private set; }

    public void Write(ReadOnlySpan<float> interleavedStereo) => FramesWritten += interleavedStereo.Length / 2;

    public void Dispose()
    {
    }
}

public static class AudioPump
{
    /// <summary>Renders <paramref name="seconds"/> of audio in blocks, calling <paramref name="beforeBlock"/>(timeSeconds) so a model can update its parameters.</summary>
    public static void Run(AudioMixer mixer, IAudioSink sink, double seconds, Action<double>? beforeBlock = null, int blockFrames = 512)
    {
        var total = (long)Math.Round(seconds * mixer.SampleRate);
        var buf = new float[blockFrames * 2];
        for (long done = 0; done < total; done += blockFrames)
        {
            beforeBlock?.Invoke((double)done / mixer.SampleRate);
            var n = (int)Math.Min(blockFrames, total - done);
            mixer.Render(buf.AsSpan(0, n * 2));
            sink.Write(buf.AsSpan(0, n * 2));
        }
    }
}
