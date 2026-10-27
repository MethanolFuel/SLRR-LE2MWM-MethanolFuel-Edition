using System.Diagnostics;
using System.Numerics;
using Silk.NET.OpenGL;
using Slrr.Formats.Graphics;
using Slrr.Render.Content;
using Slrr.Render.Geometry;
using Slrr.Render.Gl;
using Slrr.Render.Overlay;
using Slrr.Render.Scene;

namespace Slrr.Game.Render.Gl;

/// <summary>
/// The per-frame renderer of the game adapter, built from the public parts of <c>Slrr.Render</c> (<see cref="GlProgram"/>, the shipped
/// shaders, <see cref="GpuMesh"/>, <see cref="TextureCache"/>). It repeats <c>GameRenderer.Draw</c>'s batching (frustum culling, LOD,
/// instancing, blended pass) and adds what that class cannot take without being edited: several views per frame with screen
/// rectangles (Viewports), texture substitution per item (car paint), light glow billboards, a per-material cull mode, GPU
/// cache eviction for streamed map chunks and a texture memory budget.
/// </summary>
public sealed unsafe class SceneRenderer : IDisposable
{
    /// <summary>Texture bytes (estimated, mipmaps included) above which the texture cache is rebuilt at the next frame.</summary>
    public const long DefaultTextureBudget = 640L * 1024 * 1024;

    private const string BillboardVertex = """
        #version 330 core
        layout(location = 0) in vec3 aPos;
        layout(location = 1) in vec2 aUv;
        layout(location = 2) in vec4 aColor;
        uniform mat4 uViewProj;
        out vec2 vUv;
        out vec4 vColor;
        void main()
        {
            vUv = aUv;
            vColor = aColor;
            gl_Position = uViewProj * vec4(aPos, 1.0);
        }
        """;

    private const string BillboardFragment = """
        #version 330 core
        in vec2 vUv;
        in vec4 vColor;
        uniform sampler2D uTex;
        out vec4 FragColor;
        void main()
        {
            vec4 t = texture(uTex, vUv);
            FragColor = vec4(t.rgb * vColor.rgb, 1.0) * t.a * vColor.a;
        }
        """;

    private readonly GL _gl;
    private readonly GameContent _content;
    private readonly GlProgram _mesh;
    private readonly GlProgram _overlay;
    private readonly GlProgram _billboard;
    private TextureCache _textures;
    private readonly Dictionary<ResRef, GpuMesh?> _meshes = [];
    private readonly Dictionary<StaticChunk, GpuMesh> _static = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ResRef, (FontData Font, string? Atlas)?> _fonts = [];
    private readonly Dictionary<string, bool> _alpha = new(StringComparer.OrdinalIgnoreCase);
    private readonly uint _overlayVao;
    private readonly uint _overlayVbo;
    private readonly uint _billboardVao;
    private readonly uint _billboardVbo;
    private readonly uint _clampSampler;
    private uint _envTexture;
    private uint _glowTexture;
    private int _textureResets;

    public SceneRenderer(GL gl, GameContent content)
    {
        _gl = gl;
        _content = content;
        // Same shader as Slrr.Render plus one global scale of the environment reflection (the unscaled reflection makes paint look chrome).
        var fragment = Shaders.MeshFragment
            .Replace("uniform float uEnvStrength;", "uniform float uEnvStrength;\n        uniform float uEnvScale;", StringComparison.Ordinal)
            .Replace("* uEnvStrength;", "* uEnvStrength * uEnvScale;", StringComparison.Ordinal);
        if (!fragment.Contains("uEnvScale", StringComparison.Ordinal) || fragment.Split("uEnvScale").Length != 3)
        {
            throw new InvalidOperationException("Slrr.Render's mesh fragment shader changed; update the env scale patch in SceneRenderer");
        }

        _mesh = new GlProgram(gl, Shaders.MeshVertex, fragment);
        _overlay = new GlProgram(gl, Shaders.OverlayVertex, Shaders.OverlayFragment);
        _billboard = new GlProgram(gl, BillboardVertex, BillboardFragment);
        _textures = NewTextureCache();

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

        _billboardVao = gl.GenVertexArray();
        gl.BindVertexArray(_billboardVao);
        _billboardVbo = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _billboardVbo);
        gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 36, (void*)0);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 36, (void*)12);
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, 36, (void*)20);
        gl.EnableVertexAttribArray(2);
        gl.BindVertexArray(0);

        _clampSampler = gl.GenSampler();
        gl.SamplerParameter(_clampSampler, SamplerParameterI.WrapS, (int)GLEnum.ClampToEdge);
        gl.SamplerParameter(_clampSampler, SamplerParameterI.WrapT, (int)GLEnum.ClampToEdge);
        gl.SamplerParameter(_clampSampler, SamplerParameterI.MinFilter, (int)GLEnum.Linear);
        gl.SamplerParameter(_clampSampler, SamplerParameterI.MagFilter, (int)GLEnum.Linear);
    }

    public CullMode Cull { get; set; } = CullMode.Off;

    /// <summary>Multiplier of every material's environment-reflection strength (GUESSED look setting; 1 = the Slrr.Render look).</summary>
    public float EnvScale { get; set; } = 0.6f;

    public bool TwoSidedLighting { get; set; } = true;

    public long TextureBudget { get; set; } = DefaultTextureBudget;

    public int TextureCount => _textures.Count;

    public long TextureBytes => _textures.Bytes;

    /// <summary>How often the texture cache was rebuilt because of <see cref="TextureBudget"/>.</summary>
    public int TextureResets => _textureResets;

    public int MeshCount => _meshes.Count;

    public int StaticChunksOnGpu => _static.Count;

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

    /// <summary>Frees the GPU copies of static chunks that are no longer part of the loaded region.</summary>
    public int RetainStatic(IEnumerable<StaticChunk> keep)
    {
        var set = new HashSet<StaticChunk>(keep, ReferenceEqualityComparer.Instance);
        var dropped = 0;
        foreach (var c in _static.Keys.Where(c => !set.Contains(c)).ToList())
        {
            _static[c].Dispose();
            _static.Remove(c);
            dropped++;
        }

        return dropped;
    }

    private TextureCache NewTextureCache()
    {
        var cache = new TextureCache(_gl, p => _content.Texture(p, keep: false));
        _envTexture = cache.Get(_content.Files.Resolve(GameRenderer.DefaultEnvMap));
        _glowTexture = cache.Upload(MakeGlowImage());
        return cache;
    }

    private static RgbaImage MakeGlowImage()
    {
        const int n = 64;
        var img = new RgbaImage(n, n);
        for (var y = 0; y < n; y++)
        {
            for (var x = 0; x < n; x++)
            {
                var dx = (x + 0.5f) / n * 2 - 1;
                var dy = (y + 0.5f) / n * 2 - 1;
                var r = MathF.Sqrt((dx * dx) + (dy * dy));
                var a = Math.Clamp(1 - r, 0, 1);
                a *= a;
                var v = (byte)Math.Clamp(a * 255f, 0, 255);
                img.Set(x, y, 255, 255, 255, v);
            }
        }

        return img;
    }

    private bool HasAlpha(string path)
    {
        if (!_alpha.TryGetValue(path, out var a))
        {
            a = _content.Texture(path, keep: false)?.HasTransparency() ?? false;
            _alpha[path] = a;
        }

        return a;
    }

    /// <summary>Draws all views (in order) into the bound framebuffer, then the overlay. The first view clears colour and depth.</summary>
    public FrameStats Draw(IReadOnlyList<SceneView> views, OverlayFrame? overlay, int width, int height, Vector4 clear)
    {
        ArgumentNullException.ThrowIfNull(views);
        var stats = new FrameStats();
        var sw = Stopwatch.StartNew();
        if (_textures.Bytes > TextureBudget)
        {
            // Memory bound: drop every GL texture and let the frame re-upload what it needs.
            _textures.Dispose();
            _textures = NewTextureCache();
            _textureResets++;
        }

        _gl.Disable(EnableCap.ScissorTest);
        _gl.Viewport(0, 0, (uint)width, (uint)height);
        _gl.ClearColor(clear.X, clear.Y, clear.Z, clear.W);
        _gl.DepthMask(true);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        foreach (var view in views)
        {
            DrawView(view, width, height, stats);
            stats.Views++;
        }

        _gl.Disable(EnableCap.ScissorTest);
        _gl.Viewport(0, 0, (uint)width, (uint)height);
        if (overlay is not null)
        {
            DrawOverlay(overlay, width, height, stats);
        }

        stats.CpuMs = sw.Elapsed.TotalMilliseconds;
        _gl.Finish();
        stats.FrameMs = sw.Elapsed.TotalMilliseconds;
        return stats;
    }

    private void DrawView(SceneView view, int width, int height, FrameStats stats)
    {
        var scene = view.Scene;
        var cam = view.Camera;
        var px = (int)MathF.Round(view.Rect.X * width);
        var pw = Math.Max(1, (int)MathF.Round(view.Rect.W * width));
        var ph = Math.Max(1, (int)MathF.Round(view.Rect.H * height));
        var py = height - (int)MathF.Round(view.Rect.Y * height) - ph;
        _gl.Viewport(px, py, (uint)pw, (uint)ph);
        _gl.Enable(EnableCap.ScissorTest);
        _gl.Scissor(px, py, (uint)pw, (uint)ph);
        if (view.ClearColor)
        {
            _gl.ClearColor(scene.ClearColor.X, scene.ClearColor.Y, scene.ClearColor.Z, scene.ClearColor.W);
            _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        }
        else
        {
            _gl.Clear(ClearBufferMask.DepthBufferBit);
        }

        var aspect = pw / (float)ph;
        var viewProj = cam.ViewProjection(aspect);
        var frustum = new Frustum(viewProj);
        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Lequal);
        _gl.Disable(EnableCap.Blend);
        _gl.Disable(EnableCap.CullFace);
        _gl.FrontFace(FrontFaceDirection.Ccw);

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
        _mesh.Set("uFog", view.Fog);
        _mesh.Set("uUnlit", 0);
        _mesh.Set("uEnvScale", EnvScale);

        foreach (var sky in scene.Sky)
        {
            if (Mesh(sky) is not { } gm)
            {
                continue;
            }

            _gl.Disable(EnableCap.DepthTest);
            _gl.DepthMask(false);
            _mesh.Set("uUnlit", 1);
            _mesh.Set("uFog", Vector2.Zero);
            var scale = Math.Min(cam.Far * 0.5f / Math.Max(gm.Mesh.Radius, 1e-3f), 1f);
            Matrix4x4[] w = [Matrix4x4.CreateScale(scale) * Matrix4x4.CreateTranslation(cam.Position)];
            gm.SetInstances(w);
            DrawSubMeshes(gm, 1, null, null, null, stats);
            _mesh.Set("uUnlit", 0);
            _mesh.Set("uFog", view.Fog);
            _gl.DepthMask(true);
            _gl.Enable(EnableCap.DepthTest);
        }

        var batches = new Dictionary<(GpuMesh Mesh, string? From, string? To, bool Cull), List<Matrix4x4>>(new BatchComparer());
        foreach (var painted in view.Items)
        {
            var item = painted.Item;
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
            var key = (gm, painted.PaintFrom, painted.PaintTo, painted.BackfaceCull);
            if (!batches.TryGetValue(key, out var list))
            {
                batches[key] = list = [];
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

        foreach (var gm in staticVisible)
        {
            DrawSubMeshes(gm, 1, false, null, null, stats);
        }

        foreach (var (key, worlds) in batches)
        {
            key.Mesh.SetInstances(worlds.ToArray());
            DrawSubMeshes(key.Mesh, worlds.Count, false, key.From, key.To, stats, key.Cull);
        }

        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        _gl.DepthMask(false);
        foreach (var gm in staticVisible)
        {
            DrawSubMeshes(gm, 1, true, null, null, stats);
        }

        foreach (var (key, worlds) in batches.OrderByDescending(b => Vector3.DistanceSquared(Vector3.Transform(b.Key.Mesh.Mesh.Center, b.Value[0]), cam.Position)))
        {
            if (key.Mesh.Mesh.SubMeshes.Any(s => s.Material.Alpha == AlphaMode.Blend))
            {
                key.Mesh.SetInstances(worlds.ToArray());
                DrawSubMeshes(key.Mesh, worlds.Count, true, key.From, key.To, stats, key.Cull);
            }
        }

        _gl.Disable(EnableCap.CullFace);
        if (view.Glows.Count > 0)
        {
            DrawGlows(view, viewProj, stats);
        }

        _gl.DepthMask(true);
        _gl.Disable(EnableCap.Blend);
        _gl.Disable(EnableCap.ScissorTest);
    }

    private sealed class BatchComparer : IEqualityComparer<(GpuMesh Mesh, string? From, string? To, bool Cull)>
    {
        public bool Equals((GpuMesh Mesh, string? From, string? To, bool Cull) a, (GpuMesh Mesh, string? From, string? To, bool Cull) b) =>
            ReferenceEquals(a.Mesh, b.Mesh) && a.Cull == b.Cull && string.Equals(a.From, b.From, StringComparison.OrdinalIgnoreCase) && string.Equals(a.To, b.To, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((GpuMesh Mesh, string? From, string? To, bool Cull) k) =>
            HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(k.Mesh), k.From?.ToLowerInvariant(), k.To?.ToLowerInvariant(), k.Cull);
    }

    /// <param name="blended">null = all sub-meshes (sky), false = opaque + cutout, true = blended only.</param>
    private void DrawSubMeshes(GpuMesh gm, int instances, bool? blended, string? paintFrom, string? paintTo, FrameStats stats, bool itemCull = false)
    {
        _gl.BindVertexArray(gm.Vao);
        foreach (var sm in gm.Mesh.SubMeshes)
        {
            var isBlend = sm.Material.Alpha == AlphaMode.Blend;
            if (blended is { } b && b != isBlend)
            {
                continue;
            }

            var cull = Cull switch
            {
                CullMode.All => true,
                CullMode.OpaqueOnly => sm.Material.Alpha == AlphaMode.Opaque,
                _ => itemCull && sm.Material.Alpha == AlphaMode.Opaque,
            };
            if (cull)
            {
                _gl.Enable(EnableCap.CullFace);
            }
            else
            {
                _gl.Disable(EnableCap.CullFace);
            }

            Bind(sm.Material, paintFrom, paintTo);
            _gl.DrawElementsInstanced(PrimitiveType.Triangles, (uint)sm.IndexCount, DrawElementsType.UnsignedInt, (void*)(sm.FirstIndex * sizeof(uint)), (uint)instances);
            stats.DrawCalls++;
            stats.Triangles += (long)sm.IndexCount / 3 * instances;
        }
    }

    private void Bind(MaterialDesc m, string? paintFrom, string? paintTo)
    {
        var basePath = m.Base?.TexturePath;
        if (paintFrom is not null && paintTo is not null && string.Equals(basePath, paintFrom, StringComparison.OrdinalIgnoreCase))
        {
            basePath = paintTo;
        }

        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, _textures.Get(basePath));
        _mesh.Set("uBaseXf", m.Base?.ScaleOffset ?? new Vector4(1, 1, 0, 0));
        _mesh.Set("uBaseAttr", m.Base?.Attribute ?? 0);
        _mesh.Set("uHasDecal", m.Decal is null ? 0 : 1);
        if (m.Decal is { } d)
        {
            _gl.ActiveTexture(TextureUnit.Texture1);
            var decalPath = d.TexturePath;
            if (paintFrom is not null && paintTo is not null && string.Equals(decalPath, paintFrom, StringComparison.OrdinalIgnoreCase))
            {
                decalPath = paintTo;
            }

            _gl.BindTexture(TextureTarget.Texture2D, _textures.Get(decalPath));
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

    private void DrawGlows(SceneView view, Matrix4x4 viewProj, FrameStats stats)
    {
        var cam = view.Camera;
        var fwd = Vector3.Normalize(cam.Target - cam.Position);
        var right = Vector3.Normalize(Vector3.Cross(fwd, cam.Up));
        var up = Vector3.Cross(right, fwd);
        var byTexture = view.Glows.GroupBy(g => g.Texture ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        _gl.BlendFunc(BlendingFactor.One, BlendingFactor.One);
        _billboard.Use();
        _billboard.Set("uViewProj", viewProj);
        _billboard.Set("uTex", 0);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindVertexArray(_billboardVao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _billboardVbo);
        foreach (var group in byTexture)
        {
            var list = group.ToList();
            var data = new float[list.Count * 6 * 9];
            var k = 0;
            foreach (var g in list)
            {
                var r = right * g.Size;
                var u = up * g.Size;
                var uv = g.Uv == default ? new Vector4(0, 0, 1, 1) : g.Uv;
                void V(Vector3 p, float tu, float tv)
                {
                    data[k++] = p.X;
                    data[k++] = p.Y;
                    data[k++] = p.Z;
                    data[k++] = uv.X + ((uv.Z - uv.X) * tu);
                    data[k++] = uv.Y + ((uv.W - uv.Y) * tv);
                    data[k++] = g.Color.X;
                    data[k++] = g.Color.Y;
                    data[k++] = g.Color.Z;
                    data[k++] = 1f;
                }

                var p00 = g.Position - r + u;
                var p10 = g.Position + r + u;
                var p01 = g.Position - r - u;
                var p11 = g.Position + r - u;
                V(p00, 0, 0);
                V(p10, 1, 0);
                V(p01, 0, 1);
                V(p01, 0, 1);
                V(p10, 1, 0);
                V(p11, 1, 1);
            }

            _gl.BufferData<float>(BufferTargetARB.ArrayBuffer, (ReadOnlySpan<float>)data, BufferUsageARB.StreamDraw);
            var tex = group.Key.Length == 0 ? _glowTexture : _textures.Get(group.Key);
            _gl.BindTexture(TextureTarget.Texture2D, tex);
            _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)(list.Count * 6));
            stats.GlowDrawCalls++;
            stats.Glows += list.Count;
        }

        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
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

    private void DrawOverlay(OverlayFrame overlay, int width, int height, FrameStats stats)
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
        _gl.DeleteBuffer(_billboardVbo);
        _gl.DeleteVertexArray(_billboardVao);
        _textures.Dispose();
        _billboard.Dispose();
        _overlay.Dispose();
        _mesh.Dispose();
    }
}
