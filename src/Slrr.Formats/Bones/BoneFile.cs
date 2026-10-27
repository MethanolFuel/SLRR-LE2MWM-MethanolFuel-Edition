using System.Globalization;
using System.Numerics;
using System.Text;
using Slrr.Core;

namespace Slrr.Formats.Bones;

/// <summary>One bone record: name, parent index (-1 = root), 3x3 rotation rows, position, per-axis (min,max) limits.</summary>
public sealed record Bone(string Name, int Parent, Matrix4x4 Rotation, Vector3 Position, (float Min, float Max)[] Limits);

/// <summary>
/// Text skeleton used by both <c>.bon</c> and <c>.skl</c> files. Each bone is 9 lines:
/// name / parent index / 3 matrix rows "a, b, c" / position "x, y, z" / 3 limit rows "min, max".
/// Blank lines between records are tolerated.
/// </summary>
public sealed class BoneFile
{
    public IReadOnlyList<Bone> Bones { get; init; } = [];

    public static BoneFile Read(string path) => Parse(File.ReadAllBytes(path));

    public static BoneFile Parse(ReadOnlySpan<byte> bytes)
    {
        var lines = Encoding.Latin1.GetString(bytes)
            .Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => l.Trim().Length > 0)
            .ToList();
        if (lines.Count % 9 != 0)
        {
            throw new SlrrFormatException($"{lines.Count} non-blank lines is not a multiple of 9 (one bone = 9 lines)");
        }

        var bones = new List<Bone>();
        for (var i = 0; i < lines.Count; i += 9)
        {
            var name = lines[i].Trim();
            if (!int.TryParse(lines[i + 1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parent))
            {
                throw new SlrrFormatException($"bone '{name}': parent '{lines[i + 1]}' is not an integer");
            }

            var m0 = Floats(lines[i + 2], 3, name);
            var m1 = Floats(lines[i + 3], 3, name);
            var m2 = Floats(lines[i + 4], 3, name);
            var pos = Floats(lines[i + 5], 3, name);
            var limits = new (float, float)[3];
            for (var k = 0; k < 3; k++)
            {
                var l = Floats(lines[i + 6 + k], 2, name);
                limits[k] = (l[0], l[1]);
            }

            var rot = new Matrix4x4(m0[0], m0[1], m0[2], 0, m1[0], m1[1], m1[2], 0, m2[0], m2[1], m2[2], 0, 0, 0, 0, 1);
            bones.Add(new Bone(name, parent, rot, new Vector3(pos[0], pos[1], pos[2]), limits));
        }

        foreach (var b in bones)
        {
            if (b.Parent < -1 || b.Parent >= bones.Count)
            {
                throw new SlrrFormatException($"bone '{b.Name}' parent {b.Parent} out of range");
            }
        }

        return new BoneFile { Bones = bones };
    }

    private static float[] Floats(string line, int expected, string bone)
    {
        var parts = line.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != expected)
        {
            throw new SlrrFormatException($"bone '{bone}': expected {expected} comma-separated values in '{line.Trim()}'");
        }

        var result = new float[expected];
        for (var i = 0; i < expected; i++)
        {
            if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out result[i]))
            {
                throw new SlrrFormatException($"bone '{bone}': '{parts[i]}' is not a number");
            }
        }

        return result;
    }
}
