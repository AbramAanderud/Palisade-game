using Godot;

/// Animation verification capture.
/// Spawns player in third-person immediately so the character mesh is visible.
/// Runs for enough frames to show idle animation movement.
public partial class TestAnimCapture : Node3D
{
    int _frame = 0;
    PlayerController? _player;

    public override void _Ready()
    {
        // Floor
        var floor = new StaticBody3D { Name = "Floor" };
        floor.Position = new Vector3(0f, -0.2f, 0f);
        floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(40f, 0.4f, 40f) } });
        var floorMesh = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(40f, 0.4f, 40f) } };
        floorMesh.SetSurfaceOverrideMaterial(0, new StandardMaterial3D
            { AlbedoColor = new Color(0.25f, 0.25f, 0.3f), Roughness = 0.9f });
        floor.AddChild(floorMesh);
        AddChild(floor);

        // Lighting
        AddChild(new DirectionalLight3D
        {
            LightEnergy = 1.4f,
            RotationDegrees = new Vector3(-50f, 30f, 0f),
        });
        AddChild(new OmniLight3D
        {
            LightColor = new Color(0.5f, 0.6f, 1.0f),
            LightEnergy = 0.8f,
            OmniRange = 20f,
            Position = new Vector3(-3f, 5f, -3f),
        });

        var env = new Environment();
        env.BackgroundMode = Environment.BGMode.Sky;
        env.Sky = new Sky { SkyMaterial = new ProceduralSkyMaterial() };
        AddChild(new WorldEnvironment { Environment = env });

        // Spawn player and immediately switch to third-person
        _player = PlayerController.Spawn(this, new Vector3(0f, 1f, 0f), 180f);
        _player.ToggleCameraMode();

        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    public override void _Process(double delta)
    {
        _frame++;
        // No quit — controlled by --quit-after in the capture command
    }
}
