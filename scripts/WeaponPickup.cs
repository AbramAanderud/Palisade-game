using Godot;
using System;

/// Spinning arena sword pickup (the powerful "middle sword").
/// Players already spawn with a weaker sword; grabbing this one upgrades them.
/// Press E once within 2.5 m to collect — no hold required.
/// On pickup: the sword vanishes, the player's old (weaker) sword falls to the ground
/// as a static prop, and the player gains middle-sword stats (full damage + lifesteal).
public partial class WeaponPickup : Node3D
{
    // Zweihander GLB is a dedicated two-handed greatsword asset — bigger and more distinct
    // than anything in all_dmc2_swords_remade.glb. Falls back to the vendetta mesh if missing.
    const string ZweihanderPath = "res://assets/zweihander.glb";
    const string FallbackPath   = "res://assets/all_dmc2_swords_remade.glb";
    const string FallbackMesh   = "plane_003_vendetta";
    const float  MiddleScale    = 2.2f;   // zweihander is larger — scale up relative to starter

    float   _spin    = 0f;
    float   _bob     = 0f;
    Vector3 _basePos;
    bool    _consumed = false;

    /// Fired once when the local player collects the sword.
    public event Action? PickedUp;

    PlayerController? _nearPlayer;
    Area3D  _pickupArea = null!;
    AudioStreamPlayer3D _pickupSfx = null!;
    Label3D _prompt = null!;

    public static WeaponPickup Spawn(Node parent, Vector3 pos)
    {
        var wp = new WeaponPickup { Name = "SwordPickup" };
        parent.AddChild(wp);
        wp.Position  = pos;
        wp._basePos  = pos;
        return wp;
    }

    public override void _Ready()
    {
        _basePos = Position;

        bool loaded = TryLoadSwordMesh();
        if (!loaded) BuildPlaceholderMesh();

        // Blue glow — the visual signature of the powerful arena sword
        var light = new OmniLight3D
        {
            LightColor  = new(0.25f, 0.55f, 1.0f),
            LightEnergy = 3.2f,
            OmniRange   = 9.0f,
        };
        AddChild(light);

        // Single-press prompt label
        _prompt = new Label3D
        {
            Name      = "PickupPrompt",
            Text      = "[E] Take Sword",
            Position  = new(0f, 2.4f, 0f),
            FontSize  = 22,
            Modulate  = new Color(0.7f, 0.85f, 1f),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Visible   = false,
        };
        AddChild(_prompt);

        _pickupSfx = new AudioStreamPlayer3D { MaxDistance = 20f };
        var pickupStream = GD.Load<AudioStream>("res://assets/audio/sfx/combat/OnSwordPickUp.wav");
        if (pickupStream != null) _pickupSfx.Stream = pickupStream;
        AddChild(_pickupSfx);

        _pickupArea = new Area3D { Name = "PickupArea" };
        var shape   = new CollisionShape3D { Shape = new SphereShape3D { Radius = 2.5f } };
        _pickupArea.AddChild(shape);
        _pickupArea.BodyEntered += b =>
        {
            if (b is PlayerController p && !p.IsRemotePuppet)
            {
                _nearPlayer    = p;
                _prompt.Visible = !_consumed;
            }
        };
        _pickupArea.BodyExited += b =>
        {
            if (b is PlayerController)
            {
                _nearPlayer    = null;
                _prompt.Visible = false;
            }
        };
        AddChild(_pickupArea);
    }

    bool TryLoadSwordMesh()
    {
        // Try the dedicated zweihander GLB first; fall back to the vendetta mesh
        MeshInstance3D? src      = null;
        Node3D?         tempRoot = null;
        bool            isZwei   = false;

        var zweiPacked = GD.Load<PackedScene>(ZweihanderPath);
        if (zweiPacked != null)
        {
            tempRoot = zweiPacked.Instantiate<Node3D>();
            src      = FindFirst<MeshInstance3D>(tempRoot);
            isZwei   = src != null;
        }

        if (src == null)
        {
            tempRoot?.Free();
            var fallback = GD.Load<PackedScene>(FallbackPath);
            if (fallback == null) return false;
            tempRoot = fallback.Instantiate<Node3D>();
            src = FindFirst<MeshInstance3D>(tempRoot, FallbackMesh)
               ?? FindFirst<MeshInstance3D>(tempRoot);
        }

        if (src == null) { tempRoot?.Free(); return false; }

        // Zweihander blade runs along X → roll +90° Z to stand upright (X→Y).
        // DMC swords run along Z → pitch +90° X to stand upright (Z→Y).
        var inst = new MeshInstance3D
        {
            Name            = "SwordMesh",
            Mesh            = src.Mesh,
            RotationDegrees = isZwei ? new(0f, 0f, 90f) : new(90f, 0f, 0f),
            Position        = new(0f, 0.5f, 0f),
        };
        // Scale to MiddleScale relative to its longest axis (~1.8m)
        var aabb = inst.Mesh.GetAabb();
        float maxD = Mathf.Max(aabb.Size.X, Mathf.Max(aabb.Size.Y, aabb.Size.Z));
        inst.Scale = maxD > 0.01f ? Vector3.One * (MiddleScale / maxD) : Vector3.One * MiddleScale;

        // Blue-tinted emissive material to make the powerful sword visually distinct
        var mat = new StandardMaterial3D
        {
            AlbedoColor      = new Color(0.85f, 0.92f, 1f),
            Metallic         = 0.9f,
            Roughness        = 0.18f,
            EmissionEnabled  = true,
            Emission         = new Color(0.20f, 0.50f, 1.0f),
            EmissionEnergyMultiplier = 1.8f,
        };
        inst.MaterialOverride = mat;

        tempRoot?.Free();
        AddChild(inst);
        return true;
    }

    void BuildPlaceholderMesh()
    {
        var blueMat = new StandardMaterial3D
        {
            AlbedoColor      = new(0.4f, 0.55f, 1.0f),
            Metallic         = 0.95f,
            Roughness        = 0.12f,
            EmissionEnabled  = true,
            Emission         = new Color(0.2f, 0.5f, 1.0f),
            EmissionEnergyMultiplier = 1.6f,
        };
        var gripMat = new StandardMaterial3D { AlbedoColor = new(0.35f, 0.20f, 0.08f), Roughness = 0.90f };

        var blade = new MeshInstance3D { Mesh = new BoxMesh { Size = new(0.10f, 1.80f, 0.06f) }, Position = new(0f, 0.90f, 0f) };
        blade.SetSurfaceOverrideMaterial(0, blueMat);
        AddChild(blade);

        var guard = new MeshInstance3D { Mesh = new BoxMesh { Size = new(0.65f, 0.10f, 0.10f) }, Position = new(0f, 0.0f, 0f) };
        guard.SetSurfaceOverrideMaterial(0, blueMat);
        AddChild(guard);

        var grip = new MeshInstance3D { Mesh = new BoxMesh { Size = new(0.08f, 0.36f, 0.08f) }, Position = new(0f, -0.22f, 0f) };
        grip.SetSurfaceOverrideMaterial(0, gripMat);
        AddChild(grip);
    }

    public override void _Process(double delta)
    {
        if (_consumed) return;

        float dt = (float)delta;
        _spin += dt * 1.5f;
        _bob  += dt * 2.0f;
        Position = _basePos + Vector3.Up * (Mathf.Sin(_bob) * 0.22f);
        Rotation = new(0f, _spin, 0f);

        if (_nearPlayer == null) return;

        // Single press of E collects the sword — no hold required
        if (Input.IsActionJustPressed("interact"))
        {
            CollectSword();
        }
    }

    void CollectSword()
    {
        if (_consumed || _nearPlayer == null) return;
        _consumed = true;

        // Drop the player's current (weaker) sword as a static prop at their feet
        DroppedSword.Spawn(GetTree().CurrentScene ?? (Node)this,
                           _nearPlayer.GlobalPosition + Vector3.Up * 1.2f);

        // Upgrade player to middle-sword stats + start wallhack
        _nearPlayer.EquipMiddleSword();

        _pickupSfx.Play();
        PickedUp?.Invoke();

        // Make the pickup disappear smoothly — no falling-drop animation on itself
        _pickupArea.SetDeferred("monitoring",  false);
        _pickupArea.SetDeferred("monitorable", false);
        _prompt.Visible = false;
        var tw = CreateTween();
        tw.TweenProperty(this, "scale", Vector3.Zero, 0.35f);
        tw.TweenCallback(Callable.From(QueueFree));
    }

    static T? FindFirst<T>(Node node, string name) where T : Node
    {
        if (node is T t && node.Name == name) return t;
        foreach (Node child in node.GetChildren())
        {
            var found = FindFirst<T>(child, name);
            if (found != null) return found;
        }
        return null;
    }

    static T? FindFirst<T>(Node node) where T : Node
    {
        if (node is T t) return t;
        foreach (Node child in node.GetChildren())
        {
            var found = FindFirst<T>(child);
            if (found != null) return found;
        }
        return null;
    }
}
