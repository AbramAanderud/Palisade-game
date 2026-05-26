using Godot;

/// res://scripts/DroppedSword.cs
/// Static visual prop — a sword that falls to the floor and stays there.
/// Spawned when a player picks up the middle sword: their old weaker sword
/// is "dropped" at their feet for visual feedback. Not interactable.
public partial class DroppedSword : Node3D
{
    const string SwordGlbPath = "res://assets/all_dmc2_swords_remade.glb";
    const string ArenaMesh    = "plane_001_merciless";   // matches the starting (weaker) sword

    float _vel    = 0f;     // downward velocity (m/s)
    float _floorY = 0f;
    bool  _resting = false;

    public static DroppedSword Spawn(Node parent, Vector3 pos)
    {
        var prop = new DroppedSword { Name = "DroppedSword", Position = pos };
        parent.AddChild(prop);
        prop._floorY = pos.Y - 1.2f;
        return prop;
    }

    public override void _Ready()
    {
        if (!TryLoadSwordMesh()) BuildPlaceholderMesh();
        // Random spin around Y so successive drops don't look identical
        RotationDegrees = new(0f, GD.Randf() * 360f, 0f);
    }

    public override void _Process(double delta)
    {
        if (_resting) { SetProcess(false); return; }
        float dt = (float)delta;
        _vel += 9.8f * dt;
        var nextY = Position.Y - _vel * dt;
        if (nextY <= _floorY)
        {
            // Hit the floor — lay flat and stop processing
            Position    = Position with { Y = _floorY };
            RotationDegrees = new(90f, RotationDegrees.Y, 0f);
            _resting    = true;
        }
        else
        {
            Position = Position with { Y = nextY };
        }
    }

    bool TryLoadSwordMesh()
    {
        var packed = GD.Load<PackedScene>(SwordGlbPath);
        if (packed == null) return false;
        var tempRoot = packed.Instantiate<Node3D>();
        var src = FindFirst(tempRoot, ArenaMesh) ?? FindFirst(tempRoot);
        if (src == null) { tempRoot.Free(); return false; }

        var inst = new MeshInstance3D
        {
            Name = "SwordMesh",
            Mesh = src.Mesh,
        };
        var aabb = inst.Mesh.GetAabb();
        float maxD = Mathf.Max(aabb.Size.X, Mathf.Max(aabb.Size.Y, aabb.Size.Z));
        inst.Scale = maxD > 0.01f ? Vector3.One * (1.0f / maxD) : Vector3.One;
        tempRoot.Free();
        AddChild(inst);
        return true;
    }

    void BuildPlaceholderMesh()
    {
        var silverMat = new StandardMaterial3D { AlbedoColor = new(0.78f, 0.80f, 0.85f), Metallic = 0.95f, Roughness = 0.20f };
        var blade = new MeshInstance3D { Mesh = new BoxMesh { Size = new(0.05f, 0.95f, 0.04f) }, Position = new(0f, 0.40f, 0f) };
        blade.SetSurfaceOverrideMaterial(0, silverMat);
        AddChild(blade);
        var guard = new MeshInstance3D { Mesh = new BoxMesh { Size = new(0.32f, 0.05f, 0.05f) }, Position = new(0f, 0f, 0f) };
        guard.SetSurfaceOverrideMaterial(0, silverMat);
        AddChild(guard);
    }

    static MeshInstance3D? FindFirst(Node node, string? name = null)
    {
        if (node is MeshInstance3D mi && (name == null || node.Name == name)) return mi;
        foreach (Node child in node.GetChildren())
        {
            var found = FindFirst(child, name);
            if (found != null) return found;
        }
        return null;
    }
}
