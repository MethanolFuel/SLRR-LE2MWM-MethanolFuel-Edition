using Slrr.Core;

namespace Slrr.Formats.Ptx;

/// <summary>One PTX frame: a colour JPEG and an alpha JPEG (greyscale) plus a per-frame float.</summary>
public sealed record PtxFrame(uint Unknown, float Time, ReadOnlyMemory<byte> ColorJpeg, ReadOnlyMemory<byte> AlphaJpeg);

/// <summary>
/// PTX texture (particles / UI). Layout from all 127 shipped files:
/// u32 0, u32 frameCount, u32 width, u32 height, then per frame
/// { u32 totalLen (= colorLen + alphaLen), u32 colorLen, u32 alphaLen, u32 unknown (0), f32 time (k/frameCount) }
/// followed by the colour JPEG and the alpha JPEG. The first JPEG therefore starts at byte 36, not 28
/// as the rewrite plan guessed; see docs/formats/ptx.md.
/// </summary>
public sealed class PtxFile
{
    public uint HeaderUnknown { get; init; }
    public uint Width { get; init; }
    public uint Height { get; init; }
    public IReadOnlyList<PtxFrame> Frames { get; init; } = [];

    public static PtxFile Read(string path) => Read(File.ReadAllBytes(path));

    public static PtxFile Read(ReadOnlyMemory<byte> bytes)
    {
        var r = new ByteReader(bytes);
        var unknown = r.U32();
        var frameCount = r.CheckedCount(r.U32(), 20, "frame");
        var width = r.U32();
        var height = r.U32();
        var frames = new List<PtxFrame>();
        for (var i = 0; i < frameCount; i++)
        {
            var total = r.U32();
            var colorLen = r.U32();
            var alphaLen = r.U32();
            var frameUnknown = r.U32();
            var time = r.F32();
            if ((ulong)colorLen + alphaLen != total)
            {
                throw new SlrrFormatException($"frame {i}: colour {colorLen} + alpha {alphaLen} != total {total}");
            }

            var color = r.Slice(r.CheckedCount(colorLen, 1, "colour bytes"));
            var alpha = r.Slice(r.CheckedCount(alphaLen, 1, "alpha bytes"));
            if (!IsJpeg(color.Span) || (alphaLen > 0 && !IsJpeg(alpha.Span)))
            {
                throw new SlrrFormatException($"frame {i}: payload is not JPEG (FF D8 FF)");
            }

            frames.Add(new PtxFrame(frameUnknown, time, color, alpha));
        }

        if (r.Remaining != 0)
        {
            throw new SlrrFormatException($"{r.Remaining} trailing bytes after {frameCount} frames");
        }

        return new PtxFile { HeaderUnknown = unknown, Width = width, Height = height, Frames = frames };
    }

    private static bool IsJpeg(ReadOnlySpan<byte> b) => b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF;
}
