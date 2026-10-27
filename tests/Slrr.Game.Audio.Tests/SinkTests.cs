using System.Numerics;
using Slrr.Audio;
using Slrr.Game;
using Xunit;

namespace Slrr.GameAudio.Tests;

public class SinkTests
{
    private static void Eq(float e, float a, int d) => Assert.Equal((double)e, (double)a, d);

    private const int Rate = 22050;

    private static GameAudioSink NewSink(out WavFileSink output, IClipProvider? clips = null)
    {
        output = new WavFileSink(Rate);
        return new GameAudioSink(new AudioMixer(Rate), clips ?? new MapClipProvider(), output);
    }

    [Fact]
    public void UnattachedSinkFailsPlayCallsWithZeroLikeAMissingSound()
    {
        using var sink = NewSink(out _);
        Assert.Equal(0, sink.Play(0x00010005, null, 0f, 1f, 1f, 0));
        Assert.Equal(1, sink.FailedPlayCount);
        sink.Stop(123); // unknown handles are ignored
    }

    [Fact]
    public void UpdateRendersExactlyTheElapsedTimeIncludingFractionalFrames()
    {
        using var sink = NewSink(out var output);
        for (var i = 0; i < 90; i++)
        {
            sink.Update(1f / 30f);
        }

        Assert.InRange(output.Frames, (3 * Rate) - 2, (3 * Rate) + 2);
        Assert.InRange(sink.RenderedSeconds, 2.99, 3.01);
    }

    [Fact]
    public void ListenerSourceMovesTheMixerListener()
    {
        using var sink = NewSink(out _);
        sink.ListenerSource = () => new ListenerPose(new Vector3(1, 2, 3), Vector3.UnitX, Vector3.UnitY);
        sink.Update(0.01f);
        Assert.Equal(new Vector3(1, 2, 3), sink.Mixer.Listener.Position);
        Assert.Equal(Vector3.UnitX, sink.Mixer.Listener.Forward);
    }

    [Fact]
    public void BackendPlaysResolvedKeysThroughTheMixer()
    {
        var clips = new MapClipProvider().Add(new SoundKey("sound.rpk", 6), Synth.Sine(440, 0.5, Rate));
        using var sink = NewSink(out var output, clips);
        var id = sink.Backend.Play(new SoundKey("sound.rpk", 6), null, 0f, 1f, 1f, 0, 0);
        Assert.NotEqual(0, id);
        for (var i = 0; i < 10; i++)
        {
            sink.Update(0.05f);
        }

        Assert.True(output.ToClip().Peak() > 0.3f);
        Assert.Equal(0, sink.Backend.Play(new SoundKey("sound.rpk", 99), null, 0f, 1f, 1f, 0, 0));
    }

    [Fact]
    public void BootedHostRoutesMenuSoundsAndMirrorsSoundVolumes()
    {
        if (GameRoot.Find() is not { } root)
        {
            return;
        }

        var cat = SoundCatalog.Scan(new Slrr.Core.GameFileSystem(root));
        var clips = new CatalogClipProvider(cat);
        var output = new WavFileSink(Rate);
        var sink = new GameAudioSink(new AudioMixer(Rate), clips, output, VehicleAudioAssets.Synthetic());
        using var host = new GameHost(new GameHostOptions { Root = root, Audio = sink });
        Assert.True(host.Boot());
        Assert.NotNull(sink.Resolver);

        // a resource literal resolves back to the archive file name + entry id the catalog uses
        var slideId = host.Resources.Archives.ResolveLiteral("sound.rpk", 6);
        Assert.NotEqual(0, slideId);
        Assert.Equal(new SoundKey("sound.rpk", 6), sink.Resolver!.Resolve(slideId));

        // the effects volume the options page sets reaches the mixer on the next frame
        host.UiEngine.Sound.Volumes[0] = 0.25f;
        host.RunFrame();
        Eq(0.25f, sink.Mixer.GetChannelVolume(SoundChannel.Effects), 4);

        // SfxRef.nplay -> IAudioSink.Play -> mixer voice -> rendered frames
        var h = sink.Play(slideId, null, 0f, 1f, 1f, SfxFlags.Loop);
        Assert.NotEqual(0, h);
        host.RunFrames(10);
        Assert.NotNull(sink.Mixer.Find(h));
        sink.Stop(h);
        host.RunFrames(5);
        Assert.True(output.ToClip().Peak() > 0f);
        Assert.True(sink.RenderedSeconds > 0.4);
    }
}
