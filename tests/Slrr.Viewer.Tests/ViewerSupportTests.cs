using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;
using Xunit;

namespace Slrr.Viewer.Tests;

public class OrbitCameraTests
{
    [Fact]
    public void Framed_target_projects_to_screen_centre_and_sphere_fits()
    {
        var center = new Vector3(10, -3, 40);
        var cam = OrbitCamera.Frame(center, 5f);
        var vp = cam.View * cam.Projection(4f / 3f);

        var c = Vector4.Transform(new Vector4(center, 1), vp);
        Assert.InRange(c.X / c.W, -1e-4f, 1e-4f);
        Assert.InRange(c.Y / c.W, -1e-4f, 1e-4f);
        Assert.InRange(c.Z / c.W, 0f, 1f); // System.Numerics projection maps depth to [0, 1]

        var top = Vector4.Transform(new Vector4(center + new Vector3(0, 5, 0), 1), vp);
        Assert.InRange(top.Y / top.W, 0.5f, 1.5f);
    }

    [Fact]
    public void Pitch_and_zoom_are_clamped()
    {
        var cam = OrbitCamera.Frame(Vector3.Zero, 1f);
        cam.Rotate(0, 10f);
        Assert.True(cam.Pitch < MathF.PI / 2);
        cam.Zoom(1000);
        Assert.Equal(cam.MinDistance, cam.Distance);
        cam.Zoom(-1000);
        Assert.Equal(cam.MaxDistance, cam.Distance);
        Assert.True(float.IsFinite(cam.View.M11));
    }
}

public class PngWriterTests
{
    [Fact]
    public void Encodes_valid_png_and_flips_rows()
    {
        // 2x2: bottom-up input rows (GL order): row0 red, row1 green.
        byte[] rgba = [255, 0, 0, 255, 255, 0, 0, 255, 0, 255, 0, 255, 0, 255, 0, 255];
        var png = PngWriter.Encode(rgba, 2, 2, flipVertically: true);

        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png[..8]);
        Assert.Equal("IHDR", System.Text.Encoding.ASCII.GetString(png, 12, 4));
        Assert.Equal(2, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)));
        Assert.Equal(0xAE426082u, BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(png.Length - 4))); // IEND CRC

        var idatLen = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(33));
        Assert.Equal("IDAT", System.Text.Encoding.ASCII.GetString(png, 37, 4));
        using var z = new ZLibStream(new MemoryStream(png, 41, idatLen), CompressionMode.Decompress);
        var raw = new MemoryStream();
        z.CopyTo(raw);
        var bytes = raw.ToArray();
        Assert.Equal(18, bytes.Length);
        Assert.Equal(0, bytes[0]);
        Assert.Equal(new byte[] { 0, 255, 0, 255 }, bytes[1..5]);   // top row is green after flip
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, bytes[10..14]);
    }

    [Fact]
    public void Rejects_wrong_buffer_size() => Assert.Throws<ArgumentException>(() => PngWriter.Encode(new byte[3], 1, 1));
}

public class ViewerOptionsTests
{
    [Fact]
    public void Parses_all_options()
    {
        var o = ViewerOptions.Parse(["a.scx", "--screenshot", "o.png", "--size", "640x480", "--yaw", "1.5", "--pitch", "-0.25", "--wireframe"], out var err);
        Assert.Null(err);
        Assert.Equal(new ViewerOptions { MeshPath = "a.scx", ScreenshotPath = "o.png", Width = 640, Height = 480, Yaw = 1.5f, Pitch = -0.25f, Wireframe = true }, o);
    }

    [Theory]
    [InlineData]
    [InlineData("--size", "10x10")]
    [InlineData("a.scx", "--size", "0x10")]
    [InlineData("a.scx", "--size", "-1x10")]
    [InlineData("a.scx", "--yaw")]
    [InlineData("a.scx", "b.scx")]
    [InlineData("a.scx", "--bogus")]
    public void Rejects_bad_arguments(params string[] args)
    {
        Assert.Null(ViewerOptions.Parse(args, out var err));
        Assert.False(string.IsNullOrEmpty(err));
    }
}
