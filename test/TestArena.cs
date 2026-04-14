using Godot;

/// Test: Arena visual verification — floor, walls, dome apex light, arch connectivity.
/// Camera steps through 4 positions inside the arena for screenshot capture.
public partial class TestArena : Node3D
{
    Camera3D _cam  = null!;
    int      _frame = 0;

    // Yaw: 0° = looking -Z (north); 180° = looking +Z (south).
    // The test corridor is NORTH of the arena (at z < -Apothem), so yaw=0 looks at it.
    static readonly (Vector3 pos, float yaw, float pitch)[] Waypoints =
    {
        // Centre of arena, looking north toward north arch entrance
        (new(0f,  2f,  0f),    0f,   0f),
        // Looking up at the dome apex from centre
        (new(0f,  5f,  0f),    0f, -60f),
        // Just inside the north arch, looking south into the arena interior
        (new(0f,  2f, -24f), 180f,   0f),
        // South arch area, looking north into arena (shows both arches + floor)
        (new(0f,  4f,  20f),   0f,  -5f),
    };

    public override void _Ready()
    {
        // ── Environment ───────────────────────────────────────────────────────
        var worldEnv = new WorldEnvironment();
        var env      = new Godot.Environment();
        env.BackgroundMode     = Godot.Environment.BGMode.Color;
        env.BackgroundColor    = new Color(0f, 0f, 0f);
        env.AmbientLightSource = Godot.Environment.AmbientSource.Color;
        env.AmbientLightColor  = new Color(0.10f, 0.07f, 0.04f);
        env.AmbientLightEnergy = 0.25f;
        env.TonemapMode        = Godot.Environment.ToneMapper.Filmic;
        env.TonemapExposure    = 1.3f;
        worldEnv.Environment   = env;
        AddChild(worldEnv);

        // ── Test directional light (broad illumination for screenshot) ────────
        AddChild(new DirectionalLight3D
        {
            Name            = "TestSun",
            LightColor      = new Color(1f, 0.95f, 0.85f),
            LightEnergy     = 1.2f,
            Rotation        = new(-0.6f, 0.5f, 0f),
            ShadowEnabled   = false,
        });

        // ── Arena ─────────────────────────────────────────────────────────────
        var arena = new ArenaBuilder { Name = "Arena" };
        AddChild(arena);
        arena.Build(Vector3.Zero, openNorth: true, openSouth: true);


        // ── Short corridor north of arena (simulates maze exit piece arm) ─────
        BuildTestCorridor();

        // ── Camera ────────────────────────────────────────────────────────────
        _cam = new Camera3D { Name = "Cam", Fov = 90f, Near = 0.05f, Far = 600f };
        AddChild(_cam);
        ApplyWaypoint(0);
        _cam.MakeCurrent();

        GD.Print("[TestArena] Ready — 4-shot capture");
    }

    public override void _Process(double delta)
    {
        _frame++;
        if (_frame < Waypoints.Length)
            ApplyWaypoint(_frame);
    }

    void ApplyWaypoint(int idx)
    {
        var (pos, yaw, pitch) = Waypoints[idx];
        _cam.Position        = pos;
        _cam.RotationDegrees = new(pitch, yaw, 0f);
        _cam.MakeCurrent();
    }

    // ── Minimal straight corridor segment flush with north arena arch ──────────
    void BuildTestCorridor()
    {
        float apothem = ArenaBuilder.Apothem;
        float hw      = DungeonBuilder.OpeningW * 0.5f;  // 3 m
        float wallH   = DungeonBuilder.CellHeight;        // 8.4 m
        float archR   = DungeonBuilder.ArchRise;          // 4 m
        float z0      = -(apothem + DungeonBuilder.CellSize); // back of corridor
        float z1      = -apothem;                         // south face, flush with arena
        float y1      = wallH;
        float yTop    = wallH + archR;

        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        // Floor
        Quad(st, new(-hw, 0, z0), new(hw, 0, z0), new(hw, 0, z1), new(-hw, 0, z1));
        // West wall
        Quad(st, new(-hw, 0, z1), new(-hw, 0, z0), new(-hw, y1, z0), new(-hw, y1, z1));
        // East wall
        Quad(st, new(hw, 0, z0), new(hw, 0, z1), new(hw, y1, z1), new(hw, y1, z0));
        // North cap (back of segment)
        Quad(st, new(-hw, 0, z0), new(hw, 0, z0), new(hw, yTop, z0), new(-hw, yTop, z0));
        // Barrel vault (8 segments)
        const int N = 8;
        for (int i = 0; i < N; i++)
        {
            float t0 = (float)i / N, t1 = (float)(i + 1) / N;
            float xi0 = -hw + hw * 2f * t0, xi1 = -hw + hw * 2f * t1;
            float yi0 = wallH + archR * 4f * t0 * (1f - t0);
            float yi1 = wallH + archR * 4f * t1 * (1f - t1);
            Quad(st, new(xi0, yi0, z1), new(xi1, yi1, z1),
                     new(xi1, yi1, z0), new(xi0, yi0, z0));
        }

        st.GenerateNormals();
        var mesh = st.Commit();
        if (mesh.GetSurfaceCount() == 0) return;

        var sh  = new Shader { Code = StoneShader };
        var mat = new ShaderMaterial { Shader = sh };

        var body = new StaticBody3D { Name = "TestCorridor" };
        AddChild(body);
        var mi = new MeshInstance3D { Name = "CorridorMesh", Mesh = mesh };
        mi.SetSurfaceOverrideMaterial(0, mat);
        body.AddChild(mi);
        body.AddChild(new CollisionShape3D { Shape = mesh.CreateTrimeshShape() });

        AddChild(new OmniLight3D
        {
            Position      = new(0f, wallH * 0.5f, (z0 + z1) * 0.5f),
            LightColor    = new(1.0f, 0.68f, 0.22f),
            LightEnergy   = 3.5f,
            OmniRange     = 20f,
            ShadowEnabled = false,
        });
    }

    static void Quad(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        st.AddVertex(a); st.AddVertex(b); st.AddVertex(c);
        st.AddVertex(a); st.AddVertex(c); st.AddVertex(d);
    }

    const string StoneShader = @"
shader_type spatial;
render_mode diffuse_burley, specular_schlick_ggx;
uniform vec3 base_color : source_color = vec3(0.42, 0.37, 0.30);
void fragment() { ALBEDO = base_color; ROUGHNESS = 0.93; METALLIC = 0.0; }
";
}
