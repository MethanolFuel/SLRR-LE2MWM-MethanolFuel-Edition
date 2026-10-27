using System.Globalization;
using Slrr.Engine.Ui;
using Slrr.Vm;

namespace Slrr.Game;

/// <summary>A screen the game reached: active GameState, the Osd on top of the input queue and its owner.</summary>
public sealed record ScreenVisit(float Time, long Frame, string Section, string? Dialog, string? Header)
{
    public string Key => Dialog is null ? Section : $"{Section} > {Dialog}";

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"[{Time,8:F2}s] {Key}{(Header is null ? string.Empty : $"  \"{Header}\"")}");
}

/// <summary>
/// Follows what is on screen: <c>GameLogic.actualState</c> (the scripts' section: SplashScreen, MainMenu, Garage,
/// CarMarket, Track ...) and the top Osd of <c>Frontend.inputQueue</c> (a dialog or the section's own menu), with the
/// owner class (<c>Osd.globalHandler</c>) and the first visible text of that Osd as its header. Every change is
/// appended to <see cref="Visits"/>.
/// </summary>
public sealed class ScreenTracker(GameHost host)
{
    public List<ScreenVisit> Visits { get; } = [];

    public string Current => Visits.Count == 0 ? "boot" : Visits[^1].Key;

    public ScreenVisit? Last => Visits.Count == 0 ? null : Visits[^1];

    public static string Short(string className) => className[(className.LastIndexOf('.') + 1)..];

    public void Observe()
    {
        var section = host.ActiveSection?.Class.Name is { } s ? Short(s) : "-";
        var osd = host.TopOsd;
        string? dialog = null;
        string? header = null;
        if (osd is not null)
        {
            var owner = Probe.Field(osd, "globalHandler").AsObject;
            dialog = owner is null ? Short(osd.Class.Name) : Short(owner.Class.Name);
            if (string.Equals(dialog, section, StringComparison.Ordinal))
            {
                dialog = null;
            }

            header = host.Driver.TextsOf(osd).Where(host.Driver.IsVisible).Select(t => t.Text).FirstOrDefault();
        }

        var visit = new ScreenVisit(host.Vm.Time, host.Frame, section, dialog, header);
        if (Last is not { } last || last.Key != visit.Key || (last.Header != visit.Header && visit.Header is not null && last.Header is null))
        {
            Visits.Add(visit);
        }
    }

    /// <summary>
    /// Live Osds: the input queue from top to bottom, then every other Osd instance whose viewport is active (panels
    /// such as the inventory/catalog item buttons are Osds outside the input queue).
    /// </summary>
    public IEnumerable<VmObject> LiveOsds()
    {
        var seen = new HashSet<VmObject>(ReferenceEqualityComparer.Instance);
        if (Script.PeekStatic(host.Vm, "java.render.Frontend", "inputQueue") is { Ref: { } q })
        {
            var items = Script.VectorItems(host.Vm, q);
            for (var i = items.Count - 1; i >= 0; i--)
            {
                if (items[i].AsObject is { } o && seen.Add(o))
                {
                    yield return o;
                }
            }
        }

        foreach (var node in host.Resources.Tree.Instances.Where(n => n.Script is not null).OrderByDescending(n => n.Id).ToList())
        {
            var o = node.Script!;
            if (Script.IsA(o, "java.render.Osd") && !seen.Contains(o)
                && host.UiEngine.FindViewport(Probe.Field(o, "vp").AsObject) is { Active: true, Destroyed: false } && seen.Add(o))
            {
                yield return o;
            }
        }
    }

    /// <summary>Visible strings of the top Osd (what the player reads now).</summary>
    public IEnumerable<string> TopTexts() =>
        host.TopOsd is { } osd ? host.Driver.TextsOf(osd).Where(host.Driver.IsVisible).Select(t => t.Text!) : [];

    /// <summary>Labels of the clickable gadgets of active groups, top Osd first.</summary>
    public IEnumerable<string> Choices() =>
        host.Input.AllGadgets().Where(g => g.GroupActive).Select(g => (g, t: host.Input.TextOf(g)))
            .Where(x => !string.IsNullOrEmpty(x.t)).Select(x => x.g.Disabled ? $"({x.t})" : x.t!);
}
