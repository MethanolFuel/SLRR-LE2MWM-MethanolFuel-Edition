using System.Numerics;
using Silk.NET.OpenGL;

namespace Slrr.Render.Gl;

/// <summary>A linked GLSL program with cached uniform locations.</summary>
public sealed unsafe class GlProgram : IDisposable
{
    private readonly GL _gl;
    private readonly Dictionary<string, int> _locations = [];

    public GlProgram(GL gl, string vertex, string fragment)
    {
        _gl = gl;
        var v = Compile(ShaderType.VertexShader, vertex);
        var f = Compile(ShaderType.FragmentShader, fragment);
        Handle = gl.CreateProgram();
        gl.AttachShader(Handle, v);
        gl.AttachShader(Handle, f);
        gl.LinkProgram(Handle);
        gl.GetProgram(Handle, ProgramPropertyARB.LinkStatus, out var ok);
        if (ok == 0)
        {
            throw new InvalidOperationException("shader link failed: " + gl.GetProgramInfoLog(Handle));
        }

        gl.DeleteShader(v);
        gl.DeleteShader(f);
    }

    public uint Handle { get; }

    public void Use() => _gl.UseProgram(Handle);

    public int Loc(string name)
    {
        if (!_locations.TryGetValue(name, out var l))
        {
            l = _gl.GetUniformLocation(Handle, name);
            _locations[name] = l;
        }

        return l;
    }

    public void Set(string name, int v) => _gl.Uniform1(Loc(name), v);

    public void Set(string name, float v) => _gl.Uniform1(Loc(name), v);

    public void Set(string name, Vector2 v) => _gl.Uniform2(Loc(name), v.X, v.Y);

    public void Set(string name, Vector3 v) => _gl.Uniform3(Loc(name), v.X, v.Y, v.Z);

    public void Set(string name, Vector4 v) => _gl.Uniform4(Loc(name), v.X, v.Y, v.Z, v.W);

    public void Set(string name, Matrix4x4 m) => _gl.UniformMatrix4(Loc(name), 1, false, (float*)&m);

    public void Dispose() => _gl.DeleteProgram(Handle);

    private uint Compile(ShaderType type, string source)
    {
        var s = _gl.CreateShader(type);
        _gl.ShaderSource(s, source);
        _gl.CompileShader(s);
        _gl.GetShader(s, ShaderParameterName.CompileStatus, out var ok);
        if (ok == 0)
        {
            throw new InvalidOperationException($"{type} compile failed: " + _gl.GetShaderInfoLog(s));
        }

        return s;
    }
}

/// <summary>GLSL sources (330 core).</summary>
public static class Shaders
{
    /// <summary>Mesh vertex shader: per-vertex attributes 0..4 (see CpuMesh), per-instance world matrix at 5..8.</summary>
    public const string MeshVertex = """
        #version 330 core
        layout(location = 0) in vec3 aPos;
        layout(location = 1) in vec3 aNormal;
        layout(location = 2) in vec2 aUvA;
        layout(location = 3) in vec2 aUvB;
        layout(location = 4) in vec4 aColor;
        layout(location = 5) in mat4 aWorld;
        uniform mat4 uViewProj;
        uniform vec4 uBaseXf;   // uv scale xy, offset zw
        uniform vec4 uDecalXf;
        uniform int uBaseAttr;  // 0 = aUvA, 1 = aUvB
        uniform int uDecalAttr;
        out vec3 vNormal;
        out vec3 vWorld;
        out vec2 vUvBase;
        out vec2 vUvDecal;
        out vec4 vColor;
        void main()
        {
            vec4 w = aWorld * vec4(aPos, 1.0);
            vWorld = w.xyz;
            vNormal = mat3(aWorld) * aNormal;
            vec2 b = uBaseAttr == 1 ? aUvB : aUvA;
            vec2 d = uDecalAttr == 1 ? aUvB : aUvA;
            vUvBase = b * uBaseXf.xy + uBaseXf.zw;
            vUvDecal = d * uDecalXf.xy + uDecalXf.zw;
            vColor = aColor;
            gl_Position = uViewProj * w;
        }
        """;

    /// <summary>
    /// Fixed-function-like material: base x diffuse, decal blended by its alpha, optional vertex colour, alpha test,
    /// Lambert + Blinn-Phong with the material power, sphere-mapped environment reflection (slot 3) added by strength.
    /// Normals are flipped towards the viewer when <c>uTwoSided</c> is set (winding not trusted).
    /// </summary>
    public const string MeshFragment = """
        #version 330 core
        in vec3 vNormal;
        in vec3 vWorld;
        in vec2 vUvBase;
        in vec2 vUvDecal;
        in vec4 vColor;
        uniform sampler2D uBase;
        uniform sampler2D uDecal;
        uniform sampler2D uEnv;
        uniform int uHasDecal;
        uniform int uHasEnv;
        uniform float uEnvStrength;
        uniform vec4 uDiffuse;
        uniform vec3 uSpecular;
        uniform float uPower;
        uniform vec3 uLightDir;
        uniform vec3 uLightColor;
        uniform vec3 uAmbient;
        uniform vec3 uEye;
        uniform int uAlphaMode;
        uniform int uVertexColor;
        uniform int uUnlit;
        uniform int uTwoSided;
        uniform vec3 uFogColor;
        uniform vec2 uFog;      // start, end (0,0 = off)
        out vec4 FragColor;
        void main()
        {
            vec4 c = texture(uBase, vUvBase) * uDiffuse;
            if (uHasDecal == 1)
            {
                vec4 d = texture(uDecal, vUvDecal);
                c.rgb = mix(c.rgb, d.rgb, d.a);
            }
            if (uVertexColor == 1) c.rgb *= vColor.rgb;
            if (uAlphaMode == 1 && c.a < 0.5) discard;
            if (uAlphaMode == 0) c.a = 1.0;
            if (uUnlit == 0)
            {
                vec3 n = normalize(vNormal);
                vec3 v = normalize(uEye - vWorld);
                if (uTwoSided == 1 && dot(n, v) < 0.0) n = -n;
                float diff = max(dot(n, uLightDir), 0.0);
                vec3 h = normalize(uLightDir + v);
                float spec = diff > 0.0 ? pow(max(dot(n, h), 0.0), max(uPower, 1.0)) : 0.0;
                vec3 lit = c.rgb * (uAmbient + uLightColor * diff) + uSpecular * spec * uLightColor;
                if (uHasEnv == 1)
                {
                    vec3 r = reflect(-v, n);
                    float m = 2.0 * sqrt(r.x * r.x + r.y * r.y + (r.z + 1.0) * (r.z + 1.0));
                    vec2 suv = r.xy / max(m, 1e-4) + 0.5;
                    suv.y = 1.0 - suv.y;
                    lit += texture(uEnv, suv).rgb * uEnvStrength;
                }
                c.rgb = lit;
            }
            if (uFog.y > uFog.x)
            {
                float f = clamp((distance(uEye, vWorld) - uFog.x) / (uFog.y - uFog.x), 0.0, 1.0);
                c.rgb = mix(c.rgb, uFogColor, f);
            }
            FragColor = c;
        }
        """;

    public const string OverlayVertex = """
        #version 330 core
        layout(location = 0) in vec2 aPos;   // pixels, origin top-left
        layout(location = 1) in vec2 aUv;
        layout(location = 2) in vec4 aColor;
        uniform vec2 uScreen;
        out vec2 vUv;
        out vec4 vColor;
        void main()
        {
            vUv = aUv;
            vColor = aColor;
            gl_Position = vec4(aPos.x / uScreen.x * 2.0 - 1.0, 1.0 - aPos.y / uScreen.y * 2.0, 0.0, 1.0);
        }
        """;

    public const string OverlayFragment = """
        #version 330 core
        in vec2 vUv;
        in vec4 vColor;
        uniform sampler2D uTex;
        out vec4 FragColor;
        void main()
        {
            FragColor = texture(uTex, vUv) * vColor;
        }
        """;
}
