using System.Numerics;

namespace Slrr.Audio;

/// <summary>Small FFT helpers for measuring rendered audio (tests, `audio inspect`).</summary>
public static class Spectrum
{
    /// <summary>Hann-windowed magnitude spectrum of <paramref name="size"/> samples (power of two) starting at <paramref name="start"/>; length size/2.</summary>
    public static float[] Magnitude(ReadOnlySpan<float> x, int start, int size)
    {
        if ((size & (size - 1)) != 0)
        {
            throw new ArgumentException("size must be a power of two", nameof(size));
        }

        var re = new double[size];
        var im = new double[size];
        for (var i = 0; i < size; i++)
        {
            var s = start + i < x.Length ? x[start + i] : 0f;
            re[i] = s * (0.5 - (0.5 * Math.Cos(2 * Math.PI * i / size)));
        }

        Fft(re, im);
        var mag = new float[size / 2];
        for (var i = 0; i < mag.Length; i++)
        {
            mag[i] = (float)Math.Sqrt((re[i] * re[i]) + (im[i] * im[i]));
        }

        return mag;
    }

    /// <summary>Frequency (Hz) of the strongest bin in [minHz, maxHz], parabolic-interpolated; 0 if the window is silent.</summary>
    public static double PeakFrequency(ReadOnlySpan<float> x, int start, int size, int sampleRate, double minHz = 20, double maxHz = double.MaxValue)
    {
        var mag = Magnitude(x, start, size);
        var lo = Math.Max(1, (int)Math.Ceiling(minHz * size / sampleRate));
        var hi = Math.Min(mag.Length - 2, (int)Math.Floor(Math.Min(maxHz, sampleRate / 2.0) * size / sampleRate));
        var best = -1;
        for (var i = lo; i <= hi; i++)
        {
            if (best < 0 || mag[i] > mag[best])
            {
                best = i;
            }
        }

        if (best < 0 || mag[best] <= 1e-9f)
        {
            return 0;
        }

        double a = mag[Math.Max(best - 1, 0)], b = mag[best], c = mag[best + 1];
        var denom = a - (2 * b) + c;
        var delta = Math.Abs(denom) < 1e-12 ? 0 : 0.5 * (a - c) / denom;
        return (best + delta) * sampleRate / size;
    }

    public static void Fft(double[] re, double[] im)
    {
        var n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
            {
                j ^= bit;
            }

            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }

        for (var len = 2; len <= n; len <<= 1)
        {
            var ang = -2 * Math.PI / len;
            var w = new Complex(Math.Cos(ang), Math.Sin(ang));
            for (var i = 0; i < n; i += len)
            {
                var cur = Complex.One;
                for (var k = 0; k < len / 2; k++)
                {
                    var u = new Complex(re[i + k], im[i + k]);
                    var t = cur * new Complex(re[i + k + (len / 2)], im[i + k + (len / 2)]);
                    var a = u + t;
                    var b = u - t;
                    re[i + k] = a.Real;
                    im[i + k] = a.Imaginary;
                    re[i + k + (len / 2)] = b.Real;
                    im[i + k + (len / 2)] = b.Imaginary;
                    cur *= w;
                }
            }
        }
    }
}

/// <summary>Deterministic synthesised clips: test tones, noise, and fallbacks for samples the install does not ship.</summary>
public static class Synth
{
    public static WavClip Sine(double frequency, double seconds, int sampleRate = 22050, float amplitude = 0.8f)
    {
        var n = (int)Math.Round(seconds * sampleRate);
        var d = new float[n];
        for (var i = 0; i < n; i++)
        {
            d[i] = amplitude * (float)Math.Sin(2 * Math.PI * frequency * i / sampleRate);
        }

        return new WavClip(sampleRate, [d]);
    }

    /// <summary>Harmonic stack (fundamental plus 2nd/3rd at -6/-12 dB) used as the gear-whine fallback.</summary>
    public static WavClip Whine(double frequency, double seconds, int sampleRate = 22050)
    {
        var n = (int)Math.Round(seconds * sampleRate);
        var d = new float[n];
        for (var i = 0; i < n; i++)
        {
            var t = 2 * Math.PI * frequency * i / sampleRate;
            d[i] = (float)((0.6 * Math.Sin(t)) + (0.3 * Math.Sin(2 * t)) + (0.15 * Math.Sin(3 * t)));
        }

        return new WavClip(sampleRate, [d]);
    }

    /// <summary>Seeded white noise (xorshift), for tests.</summary>
    public static WavClip Noise(double seconds, int sampleRate = 22050, uint seed = 12345, float amplitude = 0.5f)
    {
        var n = (int)Math.Round(seconds * sampleRate);
        var d = new float[n];
        var s = seed == 0 ? 1u : seed;
        for (var i = 0; i < n; i++)
        {
            s ^= s << 13;
            s ^= s >> 17;
            s ^= s << 5;
            d[i] = amplitude * ((s / (float)uint.MaxValue * 2f) - 1f);
        }

        return new WavClip(sampleRate, [d]);
    }
}
