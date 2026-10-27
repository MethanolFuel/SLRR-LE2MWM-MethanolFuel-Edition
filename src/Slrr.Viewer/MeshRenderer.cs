using System.Numerics;
using Silk.NET.OpenGL;

namespace Slrr.Viewer;

/// <summary>
/// OpenGL 3.3 core renderer for one <see cref="MeshData"/>: one VAO/VBO/EBO, one shader with a directional
/// light (Lambert + ambient + Blinn specular). Normals are flipped towards the viewer in the fragment shader
/// because SCX winding/handedness is not verified yet (see docs/formats/scx.md), so culling is off.
/// </summary>
public sealed unsafe class MeshRenderer : IDisposable
{
    private const string VertexSource = """
        #version 330 core
        layout(location = 0) in vec3 aPos;
        layout(location = 1) in vec3 aNormal;
        uniform mat4 uViewProj;
        out vec3 vNormal;
        out vec3 vWorld;
        void main()
        {
            vNormal = aNormal;
            vWorld = aPos;
            gl_Position = uViewProj * vec4(aPos, 1.0);
        }
        """;

    private const string FragmentSource = """
        #version 330 core
        in vec3 vNormal;
        in vec3 vWorld;
        uniform vec3 uLightDir;   // direction towards the light, normalised
        uniform vec3 uEye;
        uniform vec3 uColor;
        out vec4 FragColor;
        void main()
        {
            vec3 n = normalize(vNormal);
            vec3 v = normalize(uEye - vWorld);
            if (dot(n, v) < 0.0) n = -n;
            float diff = max(dot(n, uLightDir), 0.0);
            vec3 h = normalize(uLightDir + v);
            float spec = pow(max(dot(n, h), 0.0), 32.0) * 0.25;
            float hemi = 0.5 + 0.5 * n.y;
            vec3 c = uColor * (0.15 + 0.15 * hemi + 0.75 * diff) + vec3(spec);
            FragColor = vec4(pow(c, vec3(1.0 / 2.2)), 1.0);
        }
        """;

    private readonly GL gl;
    private readonly uint vao;
    private readonly uint vbo;
    private readonly uint ebo;
    private readonly uint program;
    private readonly int indexCount;
    private readonly int uViewProj;
    private readonly int uLightDir;
    private readonly int uEye;
    private readonly int uColor;

    public MeshRenderer(GL gl, MeshData mesh)
    {
        ArgumentNullException.ThrowIfNull(gl);
        ArgumentNullException.ThrowIfNull(mesh);
        this.gl = gl;
        indexCount = mesh.Indices.Length;

        vao = gl.GenVertexArray();
        gl.BindVertexArray(vao);

        vbo = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
        gl.BufferData<float>(BufferTargetARB.ArrayBuffer, (ReadOnlySpan<float>)mesh.Vertices, BufferUsageARB.StaticDraw);

        ebo = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, ebo);
        gl.BufferData<uint>(BufferTargetARB.ElementArrayBuffer, (ReadOnlySpan<uint>)mesh.Indices, BufferUsageARB.StaticDraw);

        gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, MeshData.StrideBytes, (void*)0);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, MeshData.StrideBytes, (void*)MeshData.NormalOffsetBytes);
        gl.EnableVertexAttribArray(1);
        gl.BindVertexArray(0);

        program = LinkProgram(gl, VertexSource, FragmentSource);
        uViewProj = gl.GetUniformLocation(program, "uViewProj");
        uLightDir = gl.GetUniformLocation(program, "uLightDir");
        uEye = gl.GetUniformLocation(program, "uEye");
        uColor = gl.GetUniformLocation(program, "uColor");
    }

    public bool Wireframe { get; set; }

    public void Draw(OrbitCamera camera, float aspect, Vector3 lightDir)
    {
        ArgumentNullException.ThrowIfNull(camera);
        gl.Enable(EnableCap.DepthTest);
        gl.Disable(EnableCap.CullFace);
        gl.ClearColor(0.18f, 0.20f, 0.24f, 1f);
        gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        gl.PolygonMode(TriangleFace.FrontAndBack, Wireframe ? PolygonMode.Line : PolygonMode.Fill);

        gl.UseProgram(program);
        var viewProj = camera.View * camera.Projection(aspect);
        gl.UniformMatrix4(uViewProj, 1, false, (float*)&viewProj);
        var l = Vector3.Normalize(lightDir);
        gl.Uniform3(uLightDir, l.X, l.Y, l.Z);
        var eye = camera.Eye;
        gl.Uniform3(uEye, eye.X, eye.Y, eye.Z);
        gl.Uniform3(uColor, 0.75f, 0.72f, 0.68f);

        gl.BindVertexArray(vao);
        gl.DrawElements(PrimitiveType.Triangles, (uint)indexCount, DrawElementsType.UnsignedInt, (void*)0);
        gl.BindVertexArray(0);
    }

    public void Dispose()
    {
        gl.DeleteProgram(program);
        gl.DeleteBuffer(ebo);
        gl.DeleteBuffer(vbo);
        gl.DeleteVertexArray(vao);
    }

    private static uint LinkProgram(GL gl, string vs, string fs)
    {
        var v = Compile(gl, ShaderType.VertexShader, vs);
        var f = Compile(gl, ShaderType.FragmentShader, fs);
        var p = gl.CreateProgram();
        gl.AttachShader(p, v);
        gl.AttachShader(p, f);
        gl.LinkProgram(p);
        gl.GetProgram(p, ProgramPropertyARB.LinkStatus, out var ok);
        if (ok == 0)
        {
            throw new InvalidOperationException("shader link failed: " + gl.GetProgramInfoLog(p));
        }

        gl.DeleteShader(v);
        gl.DeleteShader(f);
        return p;
    }

    private static uint Compile(GL gl, ShaderType type, string source)
    {
        var s = gl.CreateShader(type);
        gl.ShaderSource(s, source);
        gl.CompileShader(s);
        gl.GetShader(s, ShaderParameterName.CompileStatus, out var ok);
        if (ok == 0)
        {
            throw new InvalidOperationException($"{type} compile failed: " + gl.GetShaderInfoLog(s));
        }

        return s;
    }
}
