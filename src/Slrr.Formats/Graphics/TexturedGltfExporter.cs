using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Slrr.Formats.Scx;

namespace Slrr.Formats.Graphics;

/// <summary>A decoded texture for one index of a render entry's texture list.</summary>
public sealed record GltfTexture(string Name, RgbaImage Image);

/// <summary>
/// glTF 2.0 export with UVs, materials and embedded PNG textures (single self-contained .gltf, base64 buffer).
/// One primitive per SCX mesh. For each material the base stage (lowest slot that does not use generated
/// coordinates) becomes <c>baseColorTexture</c>; its UV channel is written as TEXCOORD_0 with the stage's
/// scale/offset baked in (uv * scale + offset; hypothesis). The diffuse colour becomes <c>baseColorFactor</c>.
/// All stages, the SCX material flags and unresolved texture slots are kept in <c>material.extras</c>.
/// </summary>
public static class TexturedGltfExporter
{
    /// <param name="scx">Mesh file.</param>
    /// <param name="name">Node / mesh name.</param>
    /// <param name="textures">Texture list of the render entry (index = stage TextureIndex); null entries are unresolved.</param>
    /// <param name="textureNames">Optional names for the list (used in extras even when the image is missing).</param>
    public static string Export(ScxFile scx, string name, IReadOnlyList<GltfTexture?> textures, IReadOnlyList<string>? textureNames = null)
    {
        var buffer = new MemoryStream();
        var bufferViews = new JsonArray();
        var accessors = new JsonArray();
        var primitives = new JsonArray();
        var materials = new JsonArray();
        var gltfTextures = new JsonArray();
        var images = new JsonArray();
        var imageByIndex = new Dictionary<int, int>();

        foreach (var mesh in scx.Meshes)
        {
            var vb = mesh.Vertices;
            if (vb.Count == 0 || mesh.Indices.Count == 0)
            {
                continue;
            }

            var mat = ScxMaterial.Decode((int)scx.Version, mesh.Material);
            var layout = ScxVertexLayout.For(vb);
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

            var attributes = new JsonObject { ["POSITION"] = Add(buffer, bufferViews, accessors, Floats(positions), 5126, vb.Count, "VEC3", 34962, min, max) };
            if (vb.HasNormal)
            {
                var normals = new float[vb.Count * 3];
                for (var i = 0; i < vb.Count; i++)
                {
                    var n = vb.Normal(i)!.Value;
                    n = n.LengthSquared() > 1e-12f && float.IsFinite(n.LengthSquared()) ? Vector3.Normalize(n) : Vector3.UnitY;
                    normals[i * 3] = n.X;
                    normals[(i * 3) + 1] = n.Y;
                    normals[(i * 3) + 2] = n.Z;
                }

                attributes["NORMAL"] = Add(buffer, bufferViews, accessors, Floats(normals), 5126, vb.Count, "VEC3", 34962, null, null);
            }

            var stage = mat.BaseStage;
            var baseChannel = stage is { UvSource: >= 0 and <= 2 } && layout.UvOffset(stage.UvSource) >= 0 ? stage.UvSource : 0;
            if (layout.UvOffset(baseChannel) >= 0)
            {
                attributes["TEXCOORD_0"] = AddUv(buffer, bufferViews, accessors, vb, layout, baseChannel, stage);
                var second = baseChannel == 0 ? 1 : 0;
                if (layout.UvOffset(second) >= 0)
                {
                    attributes["TEXCOORD_1"] = AddUv(buffer, bufferViews, accessors, vb, layout, second, null);
                }
            }

            var indexBytes = new byte[mesh.Indices.Count * 4];
            for (var i = 0; i < mesh.Indices.Count; i++)
            {
                BitConverter.TryWriteBytes(indexBytes.AsSpan(i * 4), (uint)mesh.Indices[i]);
            }

            var indices = Add(buffer, bufferViews, accessors, indexBytes, 5125, mesh.Indices.Count, "SCALAR", 34963, null, null);

            int? texIndex = null;
            if (stage is not null && stage.TextureIndex >= 0 && stage.TextureIndex < textures.Count && textures[stage.TextureIndex] is { } tex && attributes.ContainsKey("TEXCOORD_0"))
            {
                if (!imageByIndex.TryGetValue(stage.TextureIndex, out var imageIdx))
                {
                    var png = PngCodec.Encode(tex.Image);
                    var view = AddView(buffer, bufferViews, png, null);
                    images.Add(new JsonObject { ["name"] = tex.Name, ["mimeType"] = "image/png", ["bufferView"] = view });
                    imageIdx = images.Count - 1;
                    gltfTextures.Add(new JsonObject { ["source"] = imageIdx, ["sampler"] = 0, ["name"] = tex.Name });
                    imageByIndex[stage.TextureIndex] = gltfTextures.Count - 1;
                    imageIdx = gltfTextures.Count - 1;
                }

                texIndex = imageIdx;
            }

            materials.Add(BuildMaterial(mat, texIndex, texIndex is not null && textures[stage!.TextureIndex]!.Image.HasTransparency(), textures, textureNames));
            primitives.Add(new JsonObject { ["attributes"] = attributes, ["indices"] = indices, ["mode"] = 4, ["material"] = materials.Count - 1 });
        }

        var root = new JsonObject
        {
            ["asset"] = new JsonObject { ["version"] = "2.0", ["generator"] = "Slrr.Formats TexturedGltfExporter" },
            ["scene"] = 0,
            ["scenes"] = new JsonArray(new JsonObject { ["nodes"] = new JsonArray(0) }),
            ["nodes"] = new JsonArray(new JsonObject { ["mesh"] = 0, ["name"] = name }),
            ["meshes"] = new JsonArray(new JsonObject { ["name"] = name, ["primitives"] = primitives }),
            ["materials"] = materials,
        };
        if (images.Count > 0)
        {
            root["samplers"] = new JsonArray(new JsonObject { ["magFilter"] = 9729, ["minFilter"] = 9987, ["wrapS"] = 10497, ["wrapT"] = 10497 });
            root["images"] = images;
            root["textures"] = gltfTextures;
        }

        root["buffers"] = new JsonArray(new JsonObject
        {
            ["byteLength"] = buffer.Length,
            ["uri"] = "data:application/octet-stream;base64," + Convert.ToBase64String(buffer.ToArray()),
        });
        root["bufferViews"] = bufferViews;
        root["accessors"] = accessors;
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonObject BuildMaterial(ScxMaterial mat, int? texture, bool transparent, IReadOnlyList<GltfTexture?> textures, IReadOnlyList<string>? names)
    {
        var d = mat.Diffuse;
        var pbr = new JsonObject
        {
            ["baseColorFactor"] = new JsonArray(Clamp(d.X), Clamp(d.Y), Clamp(d.Z), Clamp(d.W)),
            ["metallicFactor"] = 0.0,
            ["roughnessFactor"] = 0.8,
        };
        if (texture is { } t)
        {
            pbr["baseColorTexture"] = new JsonObject { ["index"] = t, ["texCoord"] = 0 };
        }

        var stages = new JsonArray();
        foreach (var s in mat.Stages)
        {
            var resolved = s.TextureIndex < textures.Count && textures[s.TextureIndex] is not null;
            var texName = names is not null && s.TextureIndex < names.Count ? names[s.TextureIndex] : null;
            stages.Add(new JsonObject
            {
                ["slot"] = s.Slot, ["textureIndex"] = s.TextureIndex, ["uvSource"] = s.UvSource, ["unknown"] = s.Unknown,
                ["scale"] = new JsonArray(s.UScale, s.VScale), ["offset"] = new JsonArray(s.UOffset, s.VOffset),
                ["texture"] = texName, ["resolved"] = resolved,
            });
        }

        var m = new JsonObject
        {
            ["name"] = mat.Name.Length > 0 ? mat.Name : "material",
            ["pbrMetallicRoughness"] = pbr,
            ["doubleSided"] = false,
            ["extras"] = new JsonObject
            {
                ["scxVersion"] = mat.Version, ["flags"] = $"0x{mat.Flags:X}", ["flags2"] = $"0x{mat.Flags2:X8}",
                ["specularPower"] = mat.SpecularPower is { } sp && float.IsFinite(sp) ? sp : null, ["stages"] = stages, ["parsed"] = mat.Parsed,
            },
        };
        if (transparent)
        {
            m["alphaMode"] = "BLEND";
        }

        return m;
    }

    private static double Clamp(float v) => float.IsFinite(v) ? Math.Clamp(v, 0f, 1f) : 1.0;

    private static int AddUv(MemoryStream buffer, JsonArray views, JsonArray accessors, ScxVertexBuffer vb, ScxVertexLayout layout, int channel, ScxTextureStage? transform)
    {
        var uv = new float[vb.Count * 2];
        for (var i = 0; i < vb.Count; i++)
        {
            var t = layout.ReadUv(vb, i, channel);
            if (transform is not null)
            {
                t = (t * new Vector2(transform.UScale, transform.VScale)) + new Vector2(transform.UOffset, transform.VOffset);
            }

            uv[i * 2] = float.IsFinite(t.X) ? t.X : 0f;
            uv[(i * 2) + 1] = float.IsFinite(t.Y) ? t.Y : 0f;
        }

        return Add(buffer, views, accessors, Floats(uv), 5126, vb.Count, "VEC2", 34962, null, null);
    }

    private static byte[] Floats(float[] values)
    {
        var bytes = new byte[values.Length * 4];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static int AddView(MemoryStream buffer, JsonArray views, byte[] data, int? target)
    {
        while (buffer.Length % 4 != 0)
        {
            buffer.WriteByte(0);
        }

        var offset = buffer.Length;
        buffer.Write(data);
        var v = new JsonObject { ["buffer"] = 0, ["byteOffset"] = offset, ["byteLength"] = data.Length };
        if (target is { } t)
        {
            v["target"] = t;
        }

        views.Add(v);
        return views.Count - 1;
    }

    private static int Add(MemoryStream buffer, JsonArray views, JsonArray accessors, byte[] data, int componentType, int count, string type, int target, Vector3? min, Vector3? max)
    {
        var view = AddView(buffer, views, data, target);
        var accessor = new JsonObject { ["bufferView"] = view, ["componentType"] = componentType, ["count"] = count, ["type"] = type };
        if (min is { } mn && max is { } mx)
        {
            accessor["min"] = new JsonArray(mn.X, mn.Y, mn.Z);
            accessor["max"] = new JsonArray(mx.X, mx.Y, mx.Z);
        }

        accessors.Add(accessor);
        return accessors.Count - 1;
    }
}
