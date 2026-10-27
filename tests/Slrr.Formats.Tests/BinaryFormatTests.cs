using System.Text;
using Slrr.Core;
using Slrr.Formats.Bones;
using Slrr.Formats.Ptx;
using Slrr.Formats.Rpak;
using Slrr.Formats.Scx;
using Slrr.Formats.Tufa;
using Xunit;

namespace Slrr.Formats.Tests;

public class BinaryFormatTests
{
    // Minimal TUFA class (bytes from parts/scripts/bodypart/RollCage.class, constant pool shortened by hand).
    private static byte[] TinyClass()
    {
        var w = new ByteWriter();
        w.Bytes("TUFA"u8);
        w.U32(4);
        w.U32(0x11AE1);

        var cons = new ByteWriter();
        cons.U32(4);
        cons.U32(0); cons.U32(1); cons.Bytes("A"u8); cons.U8(0);        // #0 "A"
        cons.U32(4); cons.U32(0);                                         // #1 Class A
        cons.U32(0); cons.U32(16); cons.Bytes("java.lang.Object"u8); cons.U8(0); // #2
        cons.U32(4); cons.U32(2);                                         // #3 Class Object
        Chunk(w, "CONS", cons.ToArray());

        var cls = new ByteWriter();
        cls.U32(uint.MaxValue); cls.U32(0); cls.U32(1); cls.U32(3); cls.U32(0);
        Chunk(w, "CLSS", cls.ToArray());

        var tree = new ByteWriter();
        tree.U32(1);              // one body
        tree.U32(2);              // two instructions
        tree.U8(0x0A); tree.U16(7); tree.F32(50f);  // ldc_imm32 50.0
        tree.U8(0x2A); tree.U16(8);                 // return?
        Chunk(w, "TREE", tree.ToArray());
        return w.ToArray();
    }

    private static void Chunk(ByteWriter w, string tag, byte[] payload)
    {
        w.Bytes(Encoding.Latin1.GetBytes(tag));
        w.U32((uint)payload.Length);
        w.Bytes(payload);
    }

    [Fact]
    public void TufaReaderDecodesConstantsAndCode()
    {
        var c = Assert.Single(TufaFile.Read(TinyClass()));
        Assert.Equal("A", c.Name);
        Assert.Equal("java.lang.Object", c.SuperName);
        var body = Assert.Single(c.Bodies);
        Assert.Equal(50f, BitConverter.Int32BitsToSingle(body.Instructions[0].Operand));
        Assert.Equal(8, body.Instructions[1].Line);
        Assert.Contains("ldc_imm32", TufaFile.Dump(c));
    }

    [Fact]
    public void TufaUnknownOpcodeIsAFormatError()
    {
        var bytes = TinyClass();
        bytes[^3] = 0x7F; // replace the 'return' opcode
        Assert.Throws<SlrrFormatException>(() => TufaFile.Read(bytes));
    }

    [Fact]
    public void PtxReaderSplitsColourAndAlpha()
    {
        var w = new ByteWriter();
        w.U32(0); w.U32(1); w.U32(16); w.U32(8);
        byte[] color = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2];
        byte[] alpha = [0xFF, 0xD8, 0xFF, 0xD9];
        w.U32((uint)(color.Length + alpha.Length)); w.U32((uint)color.Length); w.U32((uint)alpha.Length); w.U32(0); w.F32(1f);
        w.Bytes(color); w.Bytes(alpha);
        var p = PtxFile.Read(w.ToArray());
        Assert.Equal(16u, p.Width);
        var f = Assert.Single(p.Frames);
        Assert.Equal(color, f.ColorJpeg.ToArray());
        Assert.Equal(alpha, f.AlphaJpeg.ToArray());
    }

    [Fact]
    public void BoneFileParsesNineLineRecords()
    {
        const string text = "bone00\n-1\n1.0, 0.0, 0.0\n0.0, 1.0, 0.0\n0.0, 0.0, 1.0\n0, 24.5, 0\n-3.14, 3.14\n-3.14, 3.14\n-3.14, 3.14\n";
        var b = Assert.Single(BoneFile.Parse(Encoding.Latin1.GetBytes(text)).Bones);
        Assert.Equal("bone00", b.Name);
        Assert.Equal(-1, b.Parent);
        Assert.Equal(24.5f, b.Position.Y);
    }

    [Fact]
    public void ScxV3SingleMeshAndGltfExport()
    {
        var w = new ByteWriter();
        w.Bytes("INVO"u8); w.U32(3);
        w.U32(56); w.Bytes(new byte[52]);           // 56-byte material => 44-byte vertices
        w.U32(3);
        for (var i = 0; i < 3; i++)
        {
            w.F32(i); w.F32(0); w.F32(0);              // position
            w.F32(0); w.F32(0); w.F32(1);              // normal
            w.Bytes(new byte[20]);
        }

        w.U32(1); w.U32(0); w.U32(1); w.U32(2);
        var scx = ScxFile.Read(w.ToArray());
        var mesh = Assert.Single(scx.Meshes);
        Assert.Equal(44, mesh.Vertices.Stride);
        Assert.Equal(scx.TotalBytes, scx.CoveredBytes);
        var gltf = GltfExporter.Export(scx, "t");
        Assert.Contains("\"POSITION\"", gltf);
        Assert.Contains("\"NORMAL\"", gltf);
    }

    [Fact]
    public void ScxV4ChunkDirectory()
    {
        var w = new ByteWriter();
        w.Bytes("INVO"u8); w.U32(4); w.U32(2);
        const int dirEnd = 12 + 16;
        const int vtxSize = 8 + 8 + (3 * 12);
        w.U32(4); w.U32(dirEnd);
        w.U32(5); w.U32(dirEnd + vtxSize);
        w.U32(4); w.U32(vtxSize); w.U32(3); w.U32(0x1);
        for (var i = 0; i < 9; i++)
        {
            w.F32(i);
        }

        w.U32(5); w.U32(8 + 4 + 6); w.U32(3); w.U16(0); w.U16(1); w.U16(2);
        var scx = ScxFile.Read(w.ToArray());
        Assert.Equal(12, Assert.Single(scx.Meshes).Vertices.Stride);
        Assert.Equal(scx.TotalBytes, scx.CoveredBytes);
    }

    /// <summary>Mutation smoke test: corrupt real files must fail with SlrrFormatException, never another exception type.</summary>
    [Fact]
    public void CorruptedRealFilesOnlyThrowFormatExceptions()
    {
        var root = GameRoot.Find();
        if (root is null)
        {
            return;
        }

        var fs = new GameFileSystem(root);
        var samples = new[] { ".rpk", ".scx", ".ptx", ".class" }
            .SelectMany(ext => fs.Enumerate(ext).Where(f => new FileInfo(f).Length < 200_000).Take(15))
            .ToList();
        var rng = new Random(1234);
        foreach (var file in samples)
        {
            var original = File.ReadAllBytes(file);
            for (var round = 0; round < 40; round++)
            {
                var data = (byte[])original.Clone();
                if (round % 2 == 0)
                {
                    data = data[..rng.Next(data.Length)];
                }
                else
                {
                    for (var k = 0; k < 4; k++)
                    {
                        data[rng.Next(data.Length)] = (byte)rng.Next(256);
                    }
                }

                try
                {
                    _ = Path.GetExtension(file).ToLowerInvariant() switch
                    {
                        ".rpk" => (object)RpakArchive.Read(data),
                        ".scx" => ScxFile.Read(data),
                        ".ptx" => PtxFile.Read(data),
                        _ => TufaFile.Read(data),
                    };
                }
                catch (SlrrFormatException)
                {
                }
            }
        }
    }

    [Fact]
    public void CoverageCommandParsesEveryShippedFile()
    {
        var root = GameRoot.Find();
        if (root is null)
        {
            return;
        }

        var output = new StringWriter();
        Assert.Equal(0, Slrr.Cli.Commands.Coverage(root, output));
        Assert.Contains("TUFA  1692/1692", output.ToString());
        Assert.Contains("SCX   3224/3224", output.ToString());
    }
}
