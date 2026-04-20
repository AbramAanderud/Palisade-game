using Godot;

/// Player setup verification harness.
/// Frames 0-19:  first-person view (default)
/// Frames 20-44: third-person — Rascal model visible
/// Quits at frame 45.
public partial class TestPlayerMain : Node3D
{
    int               _frame  = 0;
    PlayerController? _player;

    public override void _Ready()
    {
        // ── Floor ────────────────────────────────────────────────────────────
        var floor = new StaticBody3D { Name = "Floor" };
        floor.Position = new Vector3(0f, -0.2f, 0f);
        var floorShape = new CollisionShape3D
            { Shape = new BoxShape3D { Size = new Vector3(40f, 0.4f, 40f) } };
        var floorMesh  = new MeshInstance3D
            { Mesh = new BoxMesh { Size = new Vector3(40f, 0.4f, 40f) } };
        var floorMat   = new StandardMaterial3D
            { AlbedoColor = new Color(0.30f, 0.30f, 0.35f), Roughness = 0.9f };
        floorMesh.SetSurfaceOverrideMaterial(0, floorMat);
        floor.AddChild(floorShape);
        floor.AddChild(floorMesh);
        AddChild(floor);

        // ── Reference columns so scale is readable ────────────────────────
        for (int i = 0; i < 4; i++)
        {
            float angle = i * Mathf.Pi * 0.5f;
            var col = new MeshInstance3D
            {
                Mesh     = new BoxMesh { Size = new Vector3(0.3f, 2.0f, 0.3f) },
                Position = new Vector3(Mathf.Sin(angle) * 3f, 1.0f, Mathf.Cos(angle) * 3f),
            };
            var colMat = new StandardMaterial3D
                { AlbedoColor = new Color(0.7f, 0.3f, 0.2f), Roughness = 0.8f };
            col.SetSurfaceOverrideMaterial(0, colMat);
            AddChild(col);
        }

        // ── Lighting ──────────────────────────────────────────────────────
        AddChild(new DirectionalLight3D
        {
            LightColor    = new Color(1f, 0.95f, 0.85f),
            LightEnergy   = 1.4f,
            RotationDegrees = new Vector3(-50f, 30f, 0f),
        });
        AddChild(new OmniLight3D
        {
            LightColor  = new Color(0.4f, 0.5f, 0.9f),
            LightEnergy = 0.6f,
            OmniRange   = 25f,
            Position    = new Vector3(-4f, 6f, -4f),
        });

        // ── Sky environment ───────────────────────────────────────────────
        var env = new Environment();
        env.BackgroundMode = Environment.BGMode.Sky;
        var sky = new Sky { SkyMaterial = new ProceduralSkyMaterial() };
        env.Sky = sky;
        AddChild(new WorldEnvironment { Environment = env });

        // ── Spawn player ──────────────────────────────────────────────────
        _player = PlayerController.Spawn(this, new Vector3(0f, 1.5f, 0f), 180f);

        // Allow rendering without captured mouse
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    public override void _Process(double delta)
    {
        _frame++;

        // At frame 20: switch to third-person so Rascal model is visible
        if (_frame == 20 && _player != null)
            _player.ToggleCameraMode();

        if (_frame >= 45)
            GetTree().Quit();
    }
}
