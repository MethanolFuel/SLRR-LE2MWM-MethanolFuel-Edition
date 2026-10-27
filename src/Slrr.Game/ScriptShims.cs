using Slrr.Formats.Tufa;
using Slrr.Vm;

namespace Slrr.Game;

/// <summary>
/// In-memory compatibility shims applied to loaded script classes (never to files). Each one stands for a VM
/// behaviour the shipped scripts rely on that Slrr.Vm does not have yet; the proper fix belongs in the VM and is
/// listed in docs/phase4/game.md.
/// </summary>
public static class ScriptShims
{
    /// <summary>
    /// Methods returning an object type whose body is EMPTY (no instructions) get the compiled <c>return null;</c>
    /// sequence <c>07 28, 0C, 28, 03 0, 2B</c> (MEASURED: copied from <c>Vehicle.iteratePartsInit</c>, source line 114
    /// <c>return null;</c>). Slrr.Vm pushes <c>Void</c> when a body falls off its end, and <c>Void</c> cannot be passed to
    /// a String/Object parameter, so <c>InventoryPanel.attachItem</c> → <c>button.setToolTip(invItem.getInfo())</c> with
    /// the empty <c>InventoryItem.getInfo()</c> stops the garage's enter thread ("unknown method Button.setToolTip()
    /// with 1 arguments"). The original evidently returns null/0 there (GUESSED; the garage works in the game).
    /// Returns the number of patched methods.
    /// </summary>
    public static int PatchEmptyObjectReturns(VmClassPath path, IEnumerable<string> classNames, List<string>? patched = null)
    {
        var n = 0;
        foreach (var name in classNames)
        {
            if (path.Find(name) is not { } src)
            {
                continue;
            }

            var c = src.Class;
            var changed = false;
            foreach (var m in c.Methods)
            {
                var desc = c.Utf8(m.DescriptorIndex);
                if (desc is null || (m.Flags & 0x40) != 0 || m.Value >= c.Bodies.Count)
                {
                    continue;
                }

                var ret = desc[(desc.LastIndexOf(')') + 1)..];
                if (!(ret.StartsWith('L') || ret.StartsWith('[')) || c.Bodies[(int)m.Value].Instructions.Count != 0)
                {
                    continue;
                }

                c.Bodies[(int)m.Value] = new TufaMethodBody(
                [
                    new TufaInstruction(0, 0x07, 0, 28, true),
                    new TufaInstruction(5, 0x0C, 0, 0, false),
                    new TufaInstruction(6, 0x28, 0, 0, false),
                    new TufaInstruction(7, 0x03, 0, 0, true),
                    new TufaInstruction(12, 0x2B, 0, 0, false),
                ]);
                changed = true;
                n++;
                patched?.Add($"{c.Name}.{c.Utf8(m.NameIndex)}{desc}");
            }

            if (changed)
            {
                path.AddClass(c, src.File);
            }
        }

        return n;
    }
}
