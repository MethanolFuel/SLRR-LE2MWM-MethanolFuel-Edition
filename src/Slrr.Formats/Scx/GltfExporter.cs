using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Slrr.Formats.Scx;

/// <summary>
/// Writes an <see cref="ScxFile"/> as a self-contained glTF 2.0 file (.gltf, buffer embedded as base64).
/// Geometry only: positions, normals (when the vertex layout has them) and triangle indices; one primitive
/// per SCX mesh. Materials/UVs are not exported yet (Phase 2).
/// </summary>
public static class GltfExporter
{
    public static string Export(ScxFile scx, string name)
    {
        var buffer = new MemoryStream();
        var bufferViews = new JsonArray();
        var accessors = new JsonArray();
        var primitives = new JsonArray();

        foreach (var mesh in scx.Meshes)
        {
            var vb = mesh.Vertices;
            if (vb.Count == 0 || mesh.Indices.Count == 0)
            {
                continue;
            }

            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            var positions = new float[vb.Count * 3];
            for (var i = 0; i < vb.Count; i++)
            {
                var p = vb.Position(i);
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
                positions[i * 3] = p.X;
                positions[(i * 3) + 1] = p.Y;
                positions[(i * 3) + 2] = p.Z;
            }

            var attributes = new JsonObject { ["POSITION"] = AddAccessor(buffer, bufferViews, accessors, Floats(positions), 5126, vb.Count, "VEC3", 34962, min, max) };

            if (vb.HasNormal)
            {
                var normals = new float[vb.Count * 3];
                for (var i = 0; i < vb.Count; i++)
                {
                    var n = vb.Normal(i)!.Value;
                    if (n.LengthSquared() > 1e-12f)
                    {
                        n = Vector3.Normalize(n);
                    }

                    normals[i * 3] = n.X;
                    normals[(i * 3) + 1] = n.Y;
                    normals[(i * 3) + 2] = n.Z;
                }

                attributes["NORMAL"] = AddAccessor(buffer, bufferViews, accessors, Floats(normals), 5126, vb.Count, "VEC3", 34962, null, null);
            }

            var indexBytes = new byte[mesh.Indices.Count * 4];
            for (var i = 0; i < mesh.Indices.Count; i++)
            {
                var idx = mesh.Indices[i];
                if (idx < 0 || idx >= vb.Count)
                {
                    throw new InvalidDataException($"index {idx} out of range for {vb.Count} vertices");
                }

                BitConverter.TryWriteBytes(indexBytes.AsSpan(i * 4), (uint)idx);
            }

            var indices = AddAccessor(buffer, bufferViews, accessors, indexBytes, 5125, mesh.Indices.Count, "SCALAR", 34963, null, null);
            primitives.Add(new JsonObject { ["attributes"] = attributes, ["indices"] = indices, ["mode"] = 4 });
        }

        var root = new JsonObject
        {
            ["asset"] = new JsonObject { ["version"] = "2.0", ["generator"] = "Slrr.Formats GltfExporter" },
            ["scene"] = 0,
            ["scenes"] = new JsonArray(new JsonObject { ["nodes"] = new JsonArray(0) }),
            ["nodes"] = new JsonArray(new JsonObject { ["mesh"] = 0, ["name"] = name }),
            ["meshes"] = new JsonArray(new JsonObject { ["name"] = name, ["primitives"] = primitives }),
            ["buffers"] = new JsonArray(new JsonObject
            {
                ["byteLength"] = buffer.Length,
                ["uri"] = "data:application/octet-stream;base64," + Convert.ToBase64String(buffer.ToArray()),
            }),
            ["bufferViews"] = bufferViews,
            ["accessors"] = accessors,
        };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static byte[] Floats(float[] values)
    {
        var bytes = new byte[values.Length * 4];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static int AddAccessor(MemoryStream buffer, JsonArray views, JsonArray accessors, byte[] data, int componentType, int count, string type, int target, Vector3? min, Vector3? max)
    {
        while (buffer.Length % 4 != 0)
        {
            buffer.WriteByte(0);
        }

        var offset = buffer.Length;
        buffer.Write(data);
        views.Add(new JsonObject { ["buffer"] = 0, ["byteOffset"] = offset, ["byteLength"] = data.Length, ["target"] = target });
        var accessor = new JsonObject
        {
            ["bufferView"] = views.Count - 1,
            ["componentType"] = componentType,
            ["count"] = count,
            ["type"] = type,
        };
        if (min is { } mn && max is { } mx)
        {
            accessor["min"] = new JsonArray(mn.X, mn.Y, mn.Z);
            accessor["max"] = new JsonArray(mx.X, mx.Y, mx.Z);
        }

        accessors.Add(accessor);
        return accessors.Count - 1;
    }
}
