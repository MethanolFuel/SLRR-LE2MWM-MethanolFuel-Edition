using System.Text;
using System.Text.Json.Nodes;
using Slrr.Core;
using Slrr.Formats.Graphics;
using Slrr.Formats.Scx;
using Xunit;

namespace Slrr.Formats.Tests.Graphics;

public class TextureDecoderTests
{
    // 16x16 RGB JPEG (4:2:0, quality 95) written by Pillow; expected pixels are Pillow/libjpeg's decode.
    private const string ColorJpeg =
        "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAIBAQEBAQIBAQECAgICAgQDAgICAgUEBAMEBgUGBgYFBgYGBwkIBgcJBwYGCAsICQoKCgoKBggLDAsKDAkKCgr/2wBDAQICAgICAgUDAwUKBwYHCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgr/wAARCAAQABADASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwD4A+FH7Kn+q/4lvp/BXtvwo/ZU/wBX/wAS30/gr6/+FH7Kn+q/4lvp/BXt3wn/AGVP9V/xLfT+Cvy/6RHjD/uv7z/n51/wHb9F/wAev4f73+Xqf//Z";

    // 8x8 greyscale JPEG (quality 100), rows are x * 30.
    private const string GreyJpeg =
        "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQH/wAALCAAIAAgBAREA/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/9oACAEBAAA/APkf/g3c/wCZG/7h3/tOv//Z";

    private static (int R, int G, int B, int A) Px(RgbaImage img, int x, int y)
    {
        var o = ((y * img.Width) + x) * 4;
        return (img.Pixels[o], img.Pixels[o + 1], img.Pixels[o + 2], img.Pixels[o + 3]);
    }

    [Fact]
    public void BaselineJpegMatchesLibjpegWithinTolerance()
    {
        var img = JpegDecoder.Decode(Convert.FromBase64String(ColorJpeg));
        Assert.Equal(16, img.Width);
        (int X, int Y, int R, int G, int B)[] expected = [(0, 0, 0, 0, 120), (5, 9, 78, 145, 127), (15, 15, 241, 239, 40), (8, 3, 126, 45, 54)];
        foreach (var e in expected)
        {
            var p = Px(img, e.X, e.Y);
            Assert.InRange(Math.Abs(p.R - e.R), 0, 6);
            Assert.InRange(Math.Abs(p.G - e.G), 0, 6);
            Assert.InRange(Math.Abs(p.B - e.B), 0, 6);
            Assert.Equal(255, p.A);
        }

        var grey = JpegDecoder.Decode(Convert.FromBase64String(GreyJpeg));
        for (var x = 0; x < 8; x++)
        {
            Assert.InRange(Math.Abs(Px(grey, x, 2).R - (x * 30)), 0, 2);
        }
    }

    [Fact]
    public void JpegRejectsProgressiveAndGarbage()
    {
        Assert.Throws<SlrrFormatException>(() => JpegDecoder.Decode([0xFF, 0xD8, 0xFF, 0xC2, 0x00, 0x02]));
        Assert.Throws<SlrrFormatException>(() => JpegDecoder.Decode([1, 2, 3, 4]));
        var truncated = Convert.FromBase64String(ColorJpeg).AsSpan(0, 300).ToArray();
        Assert.Throws<SlrrFormatException>(() => JpegDecoder.Decode(truncated));
    }

    [Fact]
    public void Dxt1BlockDecodesFourColourAndPunchThroughModes()
    {
        Span<byte> outPx = stackalloc byte[64];
        // c0 = pure red (0xF800) > c1 = pure blue (0x001F): 4-colour mode; indices 0,1,2,3 in the first row.
        byte[] block = [0x00, 0xF8, 0x1F, 0x00, 0b11_10_01_00, 0, 0, 0];
        DdsDecoder.DecodeColorBlock(block, outPx, dxt1: true);
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, outPx[..4].ToArray());
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, outPx.Slice(4, 4).ToArray());
        Assert.Equal(new byte[] { 170, 0, 85, 255 }, outPx.Slice(8, 4).ToArray());
        // swap: c0 < c1 -> 3-colour mode, index 3 is transparent black.
        byte[] block2 = [0x1F, 0x00, 0x00, 0xF8, 0b11_10_01_00, 0, 0, 0];
        DdsDecoder.DecodeColorBlock(block2, outPx, dxt1: true);
        Assert.Equal(new byte[] { 127, 0, 127, 255 }, outPx.Slice(8, 4).ToArray());
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, outPx.Slice(12, 4).ToArray());
    }

    [Fact]
    public void Dxt5AlphaBlockInterpolates()
    {
        Span<byte> outPx = stackalloc byte[64];
        // a0 = 255, a1 = 0 (8-step mode); indices: pixel0 = 0, pixel1 = 1, pixel2 = 2 (= (6*255+1*0+3)/7 = 219).
        byte[] block = [255, 0, 0b10_001_000, 0b0, 0, 0, 0, 0];
        DdsDecoder.DecodeAlphaBlock(block, outPx);
        Assert.Equal(255, outPx[3]);
        Assert.Equal(0, outPx[7]);
        Assert.Equal(219, outPx[11]);
    }

    [Fact]
    public void DdsHeaderAndUncompressedArgb()
    {
        var w = new ByteWriter();
        w.Bytes("DDS "u8);
        w.U32(124); w.U32(0x100F); w.U32(1); w.U32(2); w.U32(8); w.U32(0); w.U32(1);
        for (var i = 0; i < 11; i++)
        {
            w.U32(0);
        }

        w.U32(32); w.U32(0x41); w.U32(0); w.U32(32);
        w.U32(0x00FF0000); w.U32(0x0000FF00); w.U32(0x000000FF); w.U32(0xFF000000);
        w.U32(0x1000); w.U32(0); w.U32(0); w.U32(0); w.U32(0);
        w.U32(0x80112233); w.U32(0xFF445566);
        var img = DdsDecoder.Decode(w.ToArray());
        Assert.Equal((0x11, 0x22, 0x33, 0x80), Px(img, 0, 0));
        Assert.Equal((0x44, 0x55, 0x66, 0xFF), Px(img, 1, 0));
        Assert.StartsWith("DDS RGB32", img.Format, StringComparison.Ordinal);
    }

    [Fact]
    public void PngRoundTripsRgbaAndRgb()
    {
        var img = new RgbaImage(3, 2);
        img.Set(0, 0, 1, 2, 3, 4);
        img.Set(2, 1, 250, 251, 252, 253);
        var back = PngCodec.Decode(PngCodec.Encode(img));
        Assert.Equal(img.Pixels, back.Pixels);

        var opaque = new RgbaImage(2, 2);
        opaque.Set(1, 1, 9, 8, 7, 255);
        opaque.Set(0, 0, 0, 0, 0, 255);
        opaque.Set(1, 0, 0, 0, 0, 255);
        opaque.Set(0, 1, 0, 0, 0, 255);
        var back2 = PngCodec.Decode(PngCodec.Encode(opaque));
        Assert.Equal(opaque.Pixels, back2.Pixels);
        Assert.Contains("type2", back2.Format, StringComparison.Ordinal);
    }

    [Fact]
    public void TgaRleBottomUpDecodes()
    {
        var w = new ByteWriter();
        w.U8(0); w.U8(0); w.U8(10); w.U16(0); w.U16(0); w.U8(0); w.U16(0); w.U16(0);
        w.U16(2); w.U16(2); w.U8(32); w.U8(8);
        w.U8(0x81); w.Bytes([10, 20, 30, 40]);              // run of 2: B,G,R,A
        w.U8(0x01); w.Bytes([1, 2, 3, 4, 5, 6, 7, 8]);       // 2 raw pixels
        var img = TgaDecoder.Decode(w.ToArray());
        // first stored row is the bottom row
        Assert.Equal((30, 20, 10, 40), Px(img, 0, 1));
        Assert.Equal((3, 2, 1, 4), Px(img, 0, 0));
        Assert.Equal((7, 6, 5, 8), Px(img, 1, 0));
        w = new ByteWriter();
        w.U8(0); w.U8(0); w.U8(10); w.U16(0); w.U16(0); w.U8(0); w.U16(0); w.U16(0);
        w.U16(4); w.U16(4); w.U8(32); w.U8(8);
        w.U8(0xFF); w.Bytes([1, 2, 3, 4]);                   // run of 128 overruns 16 pixels
        Assert.Throws<SlrrFormatException>(() => TgaDecoder.Decode(w.ToArray()));
    }

    [Fact]
    public void OversizedHeadersAreRejected()
    {
        var w = new ByteWriter();
        w.U8(0); w.U8(0); w.U8(2); w.U16(0); w.U16(0); w.U8(0); w.U16(0); w.U16(0);
        w.U16(1); w.U16(1); w.U8(24); w.U8(0);
        Assert.Throws<SlrrFormatException>(() => TgaDecoder.Decode(w.ToArray())); // no pixel bytes
        Assert.Throws<SlrrFormatException>(() => new RgbaImage(100_000, 100_000));
    }
}

public class ScxMaterialTests
{
    // v4 'dash' material of cars/racers/Nonus_data/meshes/chassis.scx (104 bytes).
    private static byte[] DashMaterial()
    {
        var w = new ByteWriter();
        w.U32(4); w.U32(0); w.U32(5);
        w.U32(0x08000000);
        var name = new byte[32];
        Encoding.Latin1.GetBytes("dash").CopyTo(name, 0);
        w.Bytes(name);
        w.U32(0x00000000); w.Bytes([0xF8, 0xF8, 0xF8, 0xFF]);
        w.U32(0x01000000); w.F32(16.2f);
        w.U32(0x00000001); w.Bytes([0x21, 0x21, 0x21, 0xFF]);
        w.U32(0x06000000); w.U32(0); w.U32(0); w.U32(3); w.F32(1); w.F32(1); w.F32(0); w.F32(0);
        return w.ToArray();
    }

    [Fact]
    public void V4MaterialDecodesTaggedProperties()
    {
        var bytes = DashMaterial();
        Assert.Equal(104, bytes.Length);
        var m = ScxMaterial.DecodeV4(bytes);
        Assert.True(m.Parsed);
        Assert.Equal("dash", m.Name);
        Assert.Equal(5, m.Properties.Count);
        Assert.Equal(16.2f, m.SpecularPower);
        var s = Assert.Single(m.Stages);
        Assert.Equal((0, 0, 0, 3u, 1f, 1f), (s.Slot, s.TextureIndex, s.UvSource, s.Unknown, s.UScale, s.VScale));
        Assert.Same(s, m.BaseStage);
        Assert.InRange(m.Diffuse.X, 0.97f, 0.98f);
    }

    [Fact]
    public void V4MaterialWithUnknownTagIsFlaggedNotThrown()
    {
        var bytes = DashMaterial();
        bytes[15] = 0x09; // name tag -> unknown value type 9
        Assert.False(ScxMaterial.DecodeV4(bytes).Parsed);
        Assert.False(ScxMaterial.DecodeV4(bytes.AsMemory(0, 7)).Parsed);
    }

    [Fact]
    public void V3MaterialReadsColoursTextureSlotAndName()
    {
        var m = new byte[132];
        BitConverter.TryWriteBytes(m.AsSpan(0), 0.5f);
        BitConverter.TryWriteBytes(m.AsSpan(32), 13.5f);
        m.AsSpan(40, 60).Fill(0xFF);
        m[40] = 2;
        m[41] = 0;
        Encoding.Latin1.GetBytes("Concrete").CopyTo(m, 100);
        var mat = ScxMaterial.DecodeV3(m);
        Assert.True(mat.Parsed);
        Assert.Equal("Concrete", mat.Name);
        Assert.Equal(2, Assert.Single(mat.Stages).TextureIndex);
        Assert.Equal(13.5f, mat.SpecularPower);
    }

    [Theory]
    [InlineData(0x241u, 32, 12, 24, -1)]
    [InlineData(0x3C1u, 40, 12, 32, -1)]
    [InlineData(0x7C1u, 48, 12, 32, 40)]
    [InlineData(0x245u, 36, 16, 28, -1)]
    [InlineData(0x265u, 40, 20, 32, -1)]
    [InlineData(0xA41u, 40, 12, 24, -1)]
    public void VertexLayoutOffsets(uint flags, int stride, int normal, int uv0, int uv1)
    {
        Assert.Equal(stride, ScxFile.StrideFromFlags(flags));
        var l = ScxVertexLayout.For(new ScxVertexBuffer(flags, stride, 0, ReadOnlyMemory<byte>.Empty));
        Assert.Equal((normal, uv0, uv1), (l.Normal, l.Uv0, l.Uv1));
    }

    [Fact]
    public void AuxChunksDecode()
    {
        var w = new ByteWriter();
        w.U32(0); w.U32(3); w.U32(4); w.U32(5); w.U32(8);
        Assert.Equal([4, 5, 8], ScxAuxChunks.BonePalette(new ScxChunk(3, 0, w.ToArray()))!);
        var t2 = new ByteWriter();
        t2.U32(0); t2.U32(1); t2.U32(uint.MaxValue);
        Assert.Equal((0u, 1u, -1), ScxAuxChunks.Type2(new ScxChunk(2, 0, t2.ToArray()))!.Value);
    }

    [Fact]
    public void ExporterWritesUvMaterialAndEmbeddedTexture()
    {
        // one v4 mesh: 3 vertices, flags 0x241 (pos, normal, uv0), material = dash
        var verts = new ByteWriter();
        float[][] v = [[0, 0, 0, 0, 1, 0, 0.25f, 0.5f], [1, 0, 0, 0, 1, 0, 1, 0], [0, 0, 1, 0, 1, 0, 0, 1]];
        foreach (var vx in v)
        {
            foreach (var f in vx)
            {
                verts.F32(f);
            }
        }

        var vb = new ScxVertexBuffer(0x241, 32, 3, verts.ToArray());
        var scx = new ScxFile { Version = 4, Meshes = [new ScxMesh(DashMaterial(), vb, [0, 1, 2], [])] };
        var tex = new RgbaImage(2, 2);
        tex.Set(0, 0, 255, 0, 0, 255);
        var json = JsonNode.Parse(TexturedGltfExporter.Export(scx, "t", [new GltfTexture("dash", tex)], ["dash.dds"]))!;
        var prim = json["meshes"]![0]!["primitives"]![0]!;
        Assert.NotNull(prim["attributes"]!["TEXCOORD_0"]);
        var mat = json["materials"]![0]!;
        Assert.Equal("dash", (string?)mat["name"]);
        Assert.Equal(0, (int?)mat["pbrMetallicRoughness"]!["baseColorTexture"]!["index"]);
        Assert.Equal("image/png", (string?)json["images"]![0]!["mimeType"]);
        Assert.Equal("dash.dds", (string?)mat["extras"]!["stages"]![0]!["texture"]);
    }
}

public class GraphicsCorpusTests
{
    [Fact]
    public void EveryShippedTextureDecodes()
    {
        var root = GameRoot.Find();
        if (root is null)
        {
            return;
        }

        var fs = new GameFileSystem(root);
        var failures = new List<string>();
        var count = 0;
        foreach (var f in fs.Enumerate(".dds", ".tga", ".png", ".jpg", ".bmp", ".ptx"))
        {
            count++;
            try
            {
                var img = TextureDecoder.DecodeFile(f);
                Assert.Equal(img.Width * img.Height * 4, img.Pixels.Length);
            }
            catch (SlrrFormatException ex)
            {
                failures.Add($"{f}: {ex.Message}");
            }
        }

        Assert.True(count > 2000, $"only {count} textures found");
        Assert.Empty(failures);
    }

    [Fact]
    public void NonusChassisResolvesExternalTextureReference()
    {
        var root = GameRoot.Find();
        if (root is null)
        {
            return;
        }

        var index = RenderIndex.Build(new GameFileSystem(root));
        var render = Assert.Single(index.FindRenders(@"cars\racers\Nonus_data\meshes\chassis.scx"));
        Assert.Equal([0x3Du, 0x3Cu, 0x20025u, 0x3Bu], render.TextureIds);
        var white = index.ResolveTexture(render, 0x20025);
        Assert.Equal(@"cars\textures\default_white.png", white.SourceFile, ignoreCase: true);
        Assert.True(white.Resolved);

        var resolver = new TextureResolver(index);
        var scx = ScxFile.Read(Path.Combine(root, "cars", "racers", "Nonus_data", "meshes", "chassis.scx"));
        var report = resolver.Evaluate(@"cars\racers\Nonus_data\meshes\chassis.scx", scx);
        Assert.Equal((5, 4, 1), (report.Stages, report.Resolved, report.EnvOutOfRange));
        Assert.True(report.AllResolvedExceptEnv);
    }

    [Fact]
    public void EveryNonEmptyV4MaterialParses()
    {
        var root = GameRoot.Find();
        if (root is null)
        {
            return;
        }

        var fs = new GameFileSystem(root);
        int total = 0, bad = 0;
        foreach (var f in fs.Enumerate(".scx"))
        {
            var scx = ScxFile.Read(f);
            foreach (var m in scx.Meshes.Where(m => m.Material.Length > 0))
            {
                total++;
                bad += ScxMaterial.Decode((int)scx.Version, m.Material).Parsed ? 0 : 1;
            }
        }

        Assert.True(total > 5000);
        Assert.Equal(0, bad);
    }
}
