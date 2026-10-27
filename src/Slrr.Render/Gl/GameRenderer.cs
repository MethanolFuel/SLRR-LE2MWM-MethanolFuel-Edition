using System.Diagnostics;
using System.Numerics;
using Silk.NET.OpenGL;
using Slrr.Render.Content;
using Slrr.Render.Geometry;
using Slrr.Render.Overlay;
using Slrr.Render.Scene;

namespace Slrr.Render.Gl;

/// <summary>Per-frame counters.</summary>
public sealed class RenderStats
{
    public int DrawCalls { get; set; }
    public int OverlayDrawCalls { get; set; }
    public int ItemsVisible { get; set; }
    public int ItemsCulled { get; set; }
    public int ItemsLodRejected { get; set; }
    public int StaticVisible { get; set; }
    public int StaticCulled { get; set; }
    public int Instances { get; set; }
    public long Triangles { get; set; }
    public int MeshesWithManyInstances { get; set; }
    public double CpuMs { get; set; }
    public double FrameMs { get; set; }

    public override string ToString() =>
        $"draw calls {DrawCalls} (+{OverlayDrawCalls} overlay), items visible {ItemsVisible} culled {ItemsCulled} lod-rejected {ItemsLodRejected}, " +
        $"static visible {StaticVisible} culled {StaticCulled}, instances {Instances}, triangles {Triangles:N0}, instanced meshes {MeshesWithManyInstances}, " +
        $"cpu {CpuMs:0.0} ms, frame {FrameMs:0.0} ms";
}

/// <summary>
/// The game renderer on OpenGL 3.3 core: sky dome, frustum-culled + LOD-selected render entries drawn instanced
/// (one <c>glDrawElementsInstanced</c> per mesh x sub-mesh), static map chunks, blended sub-meshes back to front without depth
/// writes, then the 2D overlay. All GL calls of the game stay behind this class (backend swap point).
/// </summary>
public sealed unsafe class GameRenderer : IDisposable
{
    /// <summary>Default for unassigned slot-3 (reflection) stages: the first envmap of maps\skydome.rpk (GUESSED choice; the
    /// game sets it through GfxEngine.setGlobalEnvmap).</summary>
    public const string DefaultEnvMap = "maps\\skydome\\envmaps\\General_envMap2.dds";

    private readonly GL _gl;
    private readonly GameContent _content;
    private readonly GlProgram _mesh;
    private readonly GlProgram _overlay;
    private readonly TextureCache _textures;
    private readonly Dictionary<ResRef, GpuMesh?> _meshes = [];
    private readonly Dictionary<StaticChunk, GpuMesh> _static = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ResRef, (FontData Font, string? Atlas)?> _fonts = [];
    private readonly Dictionary<string, bool> _alpha = new(StringComparer.OrdinalIgnoreCase);
    private readonly uint _overlayVao;
    private readonly uint _overlayVbo;
    private readonly uint _clampSampler;
    private readonly uint _envTexture;

    public GameRenderer(GL gl, GameContent content)
    {
        _gl = gl;
        _content = content;
        _mesh = new GlProgram(gl, Shaders.MeshVertex, Shaders.MeshFragment);
        _overlay = new GlProgram(gl, Shaders.OverlayVertex, Shaders.OverlayFragment);
        _textures = new TextureCache(gl, p => content.Texture(p, keep: false));
        _envTexture = _textures.Get(content.Files.Resolve(DefaultEnvMap));

        _overlayVao = gl.GenVertexArray();
        gl.BindVertexArray(_overlayVao);
        _overlayVbo = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _overlayVbo);
        gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 32, (void*)0);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 32, (void*)8);
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, 32, (void*)16);
        gl.EnableVertexAttribArray(2);
        gl.BindVertexArray(0);

        _clampSampler = gl.GenSampler();
        gl.SamplerParameter(_clampSampler, SamplerParameterI.WrapS, (int)GLEnum.ClampToEdge);
        gl.SamplerParameter(_clampSampler, SamplerParameterI.WrapT, (int)GLEnum.ClampToEdge);
        gl.SamplerParameter(_clampSampler, SamplerParameterI.MinFilter, (int)GLEnum.Linear);
        gl.SamplerParameter(_clampSampler, SamplerParameterI.MagFilter, (int)GLEnum.Linear);
    }

    /// <summary>Back-face culling with counter-clockwise front faces. Off by default (see docs/phase4/render.md: winding measured, but meshes are not all closed).</summary>
    public bool CullBackFaces { get; set; }

    public bool TwoSidedLighting { get; set; } = true;

    /// <summary>Linear fog start/end in metres (0/0 = off).</summary>
    public Vector2 Fog { get; set; }

    public int TextureCount => _textures.Count;

    public long TextureBytes => _textures.Bytes;

    public GpuMesh? Mesh(RenderDef def)
    {
        if (!_meshes.TryGetValue(def.Ref, out var gm))
        {
            gm = null;
            if (_content.Scx(def.MeshPath) is { } scx)
            {
                var cpu = MeshBuilder.FromScx(scx, def.TexturePaths, HasAlpha);
                gm = cpu.VertexCount > 0 ? new GpuMesh(_gl, cpu) : null;
            }

            _meshes[def.Ref] = gm;
        }

        return gm;
    }

    public CpuMesh? CpuMeshOf(RenderDef def) => Mesh(def)?.Mesh;

    private bool HasAlpha(string path)
    {
        if (!_alpha.TryGetValue(path, out var a))
        {
            a = _content.Texture(path, keep: false)?.HasTransparency() ?? false;
            _alpha[path] = a;
        }

        return a;
    }

    public RenderStats Draw(SceneDescription scene, Camera cam, int width, int height, OverlayFrame? overlay)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(cam);
        var stats = new RenderStats();
        var sw = Stopwatch.StartNew();
        var aspect = width / (float)Math.Max(height, 1);
        var viewProj = cam.ViewProjection(aspect);
        var frustum = new Frustum(viewProj);

        _gl.ClearColor(scene.ClearColor.X, scene.ClearColor.Y, scene.ClearColor.Z, scene.ClearColor.W);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Lequal);
        _gl.Disable(EnableCap.Blend);
        if (CullBackFaces)
        {
            _gl.Enable(EnableCap.CullFace);
            _gl.FrontFace(FrontFaceDirection.Ccw);
        }
        else
        {
            _gl.Disable(EnableCap.CullFace);
        }

        _mesh.Use();
        _mesh.Set("uViewProj", viewProj);
        _mesh.Set("uLightDir", Vector3.Normalize(scene.LightDirection));
        _mesh.Set("uLightColor", scene.LightColor);
        _mesh.Set("uAmbient", scene.Ambient);
        _mesh.Set("uEye", cam.Position);
        _mesh.Set("uTwoSided", TwoSidedLighting ? 1 : 0);
        _mesh.Set("uBase", 0);
        _mesh.Set("uDecal", 1);
        _mesh.Set("uEnv", 2);
        _mesh.Set("uFogColor", new Vector3(scene.ClearColor.X, scene.ClearColor.Y, scene.ClearColor.Z));
        _mesh.Set("uFog", Fog);

        // Sky: centred on the eye, unlit, no depth.
        foreach (var sky in scene.Sky)
        {
            if (Mesh(sky) is { } gm)
            {
                _gl.Disable(EnableCap.DepthTest);
                _gl.DepthMask(false);
                _mesh.Set("uUnlit", 1);
                _mesh.Set("uFog", Vector2.Zero);
                var scale = Math.Min(cam.Far * 0.5f / Math.Max(gm.Mesh.Radius, 1e-3f), 1f);
                Matrix4x4[] w = [Matrix4x4.CreateScale(scale) * Matrix4x4.CreateTranslation(cam.Position)];
                gm.SetInstances(w);
                DrawSubMeshes(gm, 1, null, stats);
                _mesh.Set("uUnlit", 0);
                _mesh.Set("uFog", Fog);
                _gl.DepthMask(true);
                _gl.Enable(EnableCap.DepthTest);
            }
        }

        // Visibility + LOD, grouped by mesh for instancing.
        var batches = new Dictionary<GpuMesh, List<Matrix4x4>>(ReferenceEqualityComparer.Instance);
        foreach (var item in scene.Items)
        {
            if (Mesh(item.Def) is not { } gm)
            {
                continue;
            }

            var (wmin, wmax) = Frustum.Transform(gm.Mesh.Min, gm.Mesh.Max, item.World);
            if (!frustum.Intersects(wmin, wmax))
            {
                stats.ItemsCulled++;
                continue;
            }

            if (item.LodMax != int.MaxValue || item.Lods is not null)
            {
                var center = (wmin + wmax) * 0.5f;
                var radius = (wmax - wmin).Length() * 0.5f;
                var level = item.Lods?.Level(cam.ScreenFraction(center, radius), item.LodLevels) ?? item.LodLevels;
                if (level < item.LodMin || level > item.LodMax)
                {
                    stats.ItemsLodRejected++;
                    continue;
                }
            }

            stats.ItemsVisible++;
            if (!batches.TryGetValue(gm, out var list))
            {
                batches[gm] = list = [];
            }

            list.Add(item.World);
        }

        Matrix4x4[] identity = [Matrix4x4.Identity];
        var staticVisible = new List<GpuMesh>();
        foreach (var chunk in scene.Static)
        {
            if (!frustum.Intersects(chunk.Min, chunk.Max))
            {
                stats.StaticCulled++;
                continue;
            }

            if (!_static.TryGetValue(chunk, out var gm))
            {
                _static[chunk] = gm = new GpuMesh(_gl, chunk.Mesh);
                gm.SetInstances(identity);
            }

            stats.StaticVisible++;
            staticVisible.Add(gm);
        }

        // Opaque + alpha-tested.
        foreach (var gm in staticVisible)
        {
            DrawSubMeshes(gm, 1, false, stats);
        }

        foreach (var (gm, worlds) in batches)
        {
            gm.SetInstances(worlds.ToArray());
            stats.Instances += worlds.Count;
            stats.MeshesWithManyInstances += worlds.Count > 1 ? 1 : 0;
            DrawSubMeshes(gm, worlds.Count, false, stats);
        }

        // Blended, far to near per mesh batch.
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        _gl.DepthMask(false);
        foreach (var gm in staticVisible)
        {
            DrawSubMeshes(gm, 1, true, stats);
        }

        foreach (var (gm, worlds) in batches.OrderByDescending(b => Vector3.DistanceSquared(Vector3.Transform(b.Key.Mesh.Center, b.Value[0]), cam.Position)))
        {
            if (gm.Mesh.SubMeshes.Any(s => s.Material.Alpha == AlphaMode.Blend))
            {
                gm.SetInstances(worlds.ToArray());
                DrawSubMeshes(gm, worlds.Count, true, stats);
            }
        }

        _gl.DepthMask(true);
        _gl.Disable(EnableCap.Blend);

        if (overlay is not null)
        {
            DrawOverlay(overlay, width, height, stats);
        }

        stats.CpuMs = sw.Elapsed.TotalMilliseconds;
        _gl.Finish();
        stats.FrameMs = sw.Elapsed.TotalMilliseconds;
        return stats;
    }

    /// <param name="blended">null = all sub-meshes (sky), false = opaque + cutout, true = blended only.</param>
    private void DrawSubMeshes(GpuMesh gm, int instances, bool? blended, RenderStats stats)
    {
        _gl.BindVertexArray(gm.Vao);
        foreach (var sm in gm.Mesh.SubMeshes)
        {
            var isBlend = sm.Material.Alpha == AlphaMode.Blend;
            if (blended is { } b && b != isBlend)
            {
                continue;
            }

            Bind(sm.Material);
            _gl.DrawElementsInstanced(PrimitiveType.Triangles, (uint)sm.IndexCount, DrawElementsType.UnsignedInt, (void*)(sm.FirstIndex * sizeof(uint)), (uint)instances);
            stats.DrawCalls++;
            stats.Triangles += (long)sm.IndexCount / 3 * instances;
        }
    }

    private void Bind(MaterialDesc m)
    {
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, _textures.Get(m.Base?.TexturePath));
        _mesh.Set("uBaseXf", m.Base?.ScaleOffset ?? new Vector4(1, 1, 0, 0));
        _mesh.Set("uBaseAttr", m.Base?.Attribute ?? 0);
        _mesh.Set("uHasDecal", m.Decal is null ? 0 : 1);
        if (m.Decal is { } d)
        {
            _gl.ActiveTexture(TextureUnit.Texture1);
            _gl.BindTexture(TextureTarget.Texture2D, _textures.Get(d.TexturePath));
            _mesh.Set("uDecalXf", d.ScaleOffset);
            _mesh.Set("uDecalAttr", d.Attribute);
        }

        _mesh.Set("uHasEnv", m.Env ? 1 : 0);
        if (m.Env)
        {
            _gl.ActiveTexture(TextureUnit.Texture2);
            _gl.BindTexture(TextureTarget.Texture2D, m.EnvTexturePath is null ? _envTexture : _textures.Get(m.EnvTexturePath));
            _mesh.Set("uEnvStrength", m.EnvStrength);
        }

        _mesh.Set("uDiffuse", m.Diffuse);
        _mesh.Set("uSpecular", m.Specular);
        _mesh.Set("uPower", m.Power);
        _mesh.Set("uAlphaMode", (int)m.Alpha);
        _mesh.Set("uVertexColor", m.VertexColor ? 1 : 0);
        _gl.ActiveTexture(TextureUnit.Texture0);
    }

    public (FontData Font, string? Atlas)? Font(ResRef? font)
    {
        if (font is not { } f)
        {
            return null;
        }

        if (!_fonts.TryGetValue(f, out var hit))
        {
            hit = null;
            if (_content.Render(f) is { } def && _content.Scx(def.MeshPath) is { } scx)
            {
                hit = (FontData.FromScx(scx), def.TexturePaths.FirstOrDefault());
            }

            _fonts[f] = hit;
        }

        return hit;
    }

    private void DrawOverlay(OverlayFrame overlay, int width, int height, RenderStats stats)
    {
        var sprites = OverlayLayout.Layout(overlay, width, height, Font);
        if (sprites.Count == 0)
        {
            return;
        }

        var data = new float[sprites.Count * 6 * 8];
        var k = 0;
        foreach (var s in sprites)
        {
            void V(float x, float y, float u, float v)
            {
                data[k++] = x;
                data[k++] = y;
                data[k++] = u;
                data[k++] = v;
                data[k++] = s.Color.X;
                data[k++] = s.Color.Y;
                data[k++] = s.Color.Z;
                data[k++] = s.Color.W;
            }

            V(s.P0.X, s.P0.Y, s.Uv0.X, s.Uv0.Y);
            V(s.P1.X, s.P0.Y, s.Uv1.X, s.Uv0.Y);
            V(s.P0.X, s.P1.Y, s.Uv0.X, s.Uv1.Y);
            V(s.P0.X, s.P1.Y, s.Uv0.X, s.Uv1.Y);
            V(s.P1.X, s.P0.Y, s.Uv1.X, s.Uv0.Y);
            V(s.P1.X, s.P1.Y, s.Uv1.X, s.Uv1.Y);
        }

        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.CullFace);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        _overlay.Use();
        _overlay.Set("uScreen", new Vector2(width, height));
        _overlay.Set("uTex", 0);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindSampler(0, _clampSampler);
        _gl.BindVertexArray(_overlayVao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _overlayVbo);
        _gl.BufferData<float>(BufferTargetARB.ArrayBuffer, (ReadOnlySpan<float>)data, BufferUsageARB.StreamDraw);

        // One draw per run of sprites sharing a texture (order matters for blending).
        var start = 0;
        for (var i = 1; i <= sprites.Count; i++)
        {
            if (i == sprites.Count || !string.Equals(sprites[i].Texture, sprites[start].Texture, StringComparison.OrdinalIgnoreCase))
            {
                _gl.BindTexture(TextureTarget.Texture2D, _textures.Get(sprites[start].Texture));
                _gl.DrawArrays(PrimitiveType.Triangles, start * 6, (uint)((i - start) * 6));
                stats.OverlayDrawCalls++;
                start = i;
            }
        }

        _gl.BindSampler(0, 0);
        _gl.Disable(EnableCap.Blend);
        _gl.Enable(EnableCap.DepthTest);
    }

    public void Dispose()
    {
        foreach (var m in _meshes.Values)
        {
            m?.Dispose();
        }

        foreach (var m in _static.Values)
        {
            m.Dispose();
        }

        _gl.DeleteSampler(_clampSampler);
        _gl.DeleteBuffer(_overlayVbo);
        _gl.DeleteVertexArray(_overlayVao);
        _textures.Dispose();
        _overlay.Dispose();
        _mesh.Dispose();
    }
}
