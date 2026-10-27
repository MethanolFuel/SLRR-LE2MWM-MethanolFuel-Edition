using System.Numerics;
using Slrr.App;
using Slrr.Formats.Tests;
using Slrr.Game;
using Slrr.Race;
using Xunit;
using Xunit.Abstractions;

namespace Slrr.App.Tests;

/// <summary>Needs the game data (pass vacuously without it). Headless: no window, GL or audio device.</summary>
public class CorpusTests(ITestOutputHelper output)
{
    /// <summary>
    /// Every labelled gadget of the main menu: the label text the script placed for the button lies inside the clickable box rebuilt from its
    /// physics box (so a player clicking on the text hits the button), and clicking the box centre through the router reaches that gadget.
    /// </summary>
    [Fact]
    public void MainMenuLabelsLieInsideTheirClickableBoxes()
    {
        if (GameRoot.Find() is not { } root)
        {
            return;
        }

        using var race = RaceHost.Create(root, new RaceHostOptions { MapNatives = false });
        var host = race.Host;
        Assert.True(host.Boot() && host.RunUntil(() => race.Section == "MainMenu", 30));
        var hits = new GadgetHitTester(host);
        var targets = hits.Targets().Where(t => !string.IsNullOrEmpty(t.Text)).ToList();
        Assert.True(targets.Count >= 5, $"{targets.Count} labelled targets");
        foreach (var t in targets)
        {
            output.WriteLine(t.ToString());
            var label = Probe.Field(Probe.Field(t.Target, "label").AsObject, "txt").AsObject;
            var model = host.UiEngine.Texts.First(m => ReferenceEquals(m.Object, label));
            Assert.True(t.Rect.Contains(new Vector2(model.X, model.Y)), $"label of {t} at ({model.X:F3},{model.Y:F3}) is outside its box");
            Assert.Same(t.Target, hits.At(t.Rect.Center)?.Target);
        }
    }

    /// <summary>The proof run without a window: builtin:drive (menu → career → dealer → buy → Drag Track → throttle) through the app's input path.</summary>
    [Fact]
    public void HeadlessAppDrivesTheBuiltInScript()
    {
        if (GameRoot.Find() is not { } root)
        {
            return;
        }

        var save = Path.Combine(Path.GetTempPath(), "slrr-app-save-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var o = new StringWriter();
            var code = AppMain.Run(["--root", root, "--save-root", save, "--log", Path.Combine(save, "app.log"), "--headless-frames", "3000", "--input-script", "builtin:drive"], o, o);
            output.WriteLine(o.ToString());
            Assert.Equal(0, code);
            Assert.Contains("input script finished", o.ToString());
            Assert.Contains("in track True", o.ToString());
            Assert.True(Directory.EnumerateFileSystemEntries(save).Any(), "nothing was written to the save root");
        }
        finally
        {
            try
            {
                Directory.Delete(save, true);
            }
            catch (IOException)
            {
            }
        }
    }
}
