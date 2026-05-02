using Godot;

/// Spinning sword pickup. Place with WeaponPickup.Spawn(parent, pos).
/// Player must hold E for HoldTime seconds while within 2.5 m to pick up.
public partial class WeaponPickup : Node3D
{
    const float HoldTime = 0.8f;   // seconds of continuous hold required

    float   _spin    = 0f;
    float   _bob     = 0f;
    Vector3 _basePos;
    PlayerController? _nearPlayer;

    float   _holdProgress = 0f;    // how long E has been held this attempt
    Label3D _progressLabel = null!;

    public static WeaponPickup Spawn(Node parent, Vector3 pos)
    {
        var wp = new WeaponPickup { Name = "SwordPickup" };
        parent.AddChild(wp);
        wp.Position = pos;
        wp._basePos  = pos;
        return wp;
    }

    public override void _Ready()
    {
        _basePos = Position;

        var silverMat = new StandardMaterial3D
        {
            AlbedoColor = new(0.78f, 0.80f, 0.85f),
            Metallic    = 0.95f,
            Roughness   = 0.12f,
        };
        var gripMat = new StandardMaterial3D
        {
            AlbedoColor = new(0.35f, 0.20f, 0.08f),
            Roughness   = 0.90f,
        };

        // Blade
        var blade = new MeshInstance3D
        {
            Mesh     = new BoxMesh { Size = new(0.06f, 1.10f, 0.04f) },
            Position = new(0f, 0.55f, 0f),
        };
        blade.SetSurfaceOverrideMaterial(0, silverMat);
        AddChild(blade);

        // Crossguard
        var guard = new MeshInstance3D
        {
            Mesh     = new BoxMesh { Size = new(0.40f, 0.06f, 0.06f) },
            Position = new(0f, 0.18f, 0f),
        };
        guard.SetSurfaceOverrideMaterial(0, silverMat);
        AddChild(guard);

        // Grip
        var grip = new MeshInstance3D
        {
            Mesh     = new BoxMesh { Size = new(0.05f, 0.22f, 0.05f) },
            Position = new(0f, -0.03f, 0f),
        };
        grip.SetSurfaceOverrideMaterial(0, gripMat);
        AddChild(grip);

        // Gold glow
        var light = new OmniLight3D
        {
            LightColor  = new(1.0f, 0.85f, 0.3f),
            LightEnergy = 1.8f,
            OmniRange   = 6.0f,
        };
        AddChild(light);

        // Progress label floating above the sword
        _progressLabel = new Label3D
        {
            Name       = "ProgressLabel",
            Text       = "",
            Position   = new(0f, 1.6f, 0f),
            FontSize   = 18,
            Billboard  = BaseMaterial3D.BillboardModeEnum.Enabled,
            Visible    = false,
        };
        AddChild(_progressLabel);

        // Pickup trigger area
        var area  = new Area3D { Name = "PickupArea" };
        var shape = new CollisionShape3D { Shape = new SphereShape3D { Radius = 2.5f } };
        area.AddChild(shape);
        area.BodyEntered += b => { if (b is PlayerController p) _nearPlayer = p; };
        area.BodyExited  += b =>
        {
            if (b is PlayerController)
            {
                _nearPlayer   = null;
                _holdProgress = 0f;
                _progressLabel.Visible = false;
            }
        };
        AddChild(area);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        // Spin and bob animation
        _spin += dt * 1.5f;
        _bob  += dt * 2.0f;
        Position = _basePos + Vector3.Up * (Mathf.Sin(_bob) * 0.18f);
        Rotation = new(0f, _spin, 0f);

        if (_nearPlayer == null || _nearPlayer.HasWeapon)
        {
            _holdProgress = 0f;
            _progressLabel.Visible = false;
            return;
        }

        if (Input.IsActionPressed("interact"))
        {
            _holdProgress += dt;
            _progressLabel.Visible = true;

            // Build a simple fill bar: 10 chars total
            int filled = Mathf.Clamp((int)(_holdProgress / HoldTime * 10f), 0, 10);
            string bar = new string('#', filled) + new string(' ', 10 - filled);
            _progressLabel.Text = $"Hold E... [{bar}]";

            if (_holdProgress >= HoldTime)
            {
                _nearPlayer.PickupWeapon();
                QueueFree();
            }
        }
        else
        {
            // Released E — cancel
            if (_holdProgress > 0f)
            {
                _holdProgress = 0f;
                _progressLabel.Visible = false;
            }
        }
    }
}
