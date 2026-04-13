using Godot;
using System.Collections.Generic;

/// Risk Task 1 visual test — extends Node3D so it runs as a normal scene.
/// Creates a test dungeon and steps a camera through it for screenshot capture.
public partial class TestRisk1Main : Node3D
{
    Camera3D _cam    = null!;
    int      _frame  = 0;

    // Camera waypoints — one screenshot per waypoint
    static readonly (Vector3 pos, float yaw, float pitch)[] Waypoints =
    {
        // Inside first straight hall, looking north (into the corridor)
        (new Vector3(25f, 2.2f, 37f),  180f, -5f),
        // At the L-turn, looking east along the cross-corridor
        (new Vector3(25f, 2.2f, 15f),  -90f, -5f),
        // In the E-W hall, looking toward the T-junction
        (new Vector3(38f, 2.2f, 15f),  -90f, -5f),
        // Low-angle looking up the tall corridor wall — liminal feel
        (new Vector3(25f, 0.5f, 38f),  180f,  8f),
        // Wide overhead view
        (new Vector3(35f, 14f, 28f),     0f, -55f),
        // Start piece looking north — eye-level in the dungeon
        (new Vector3(25f, 1.7f, 45f),  180f,  0f),
    };

    public override void _Ready()
    {
        // ── Build test maze ───────────────────────────────────────────────────
        var mazeData = new MazeData { Name = "RiskTest1" };
        mazeData.Pieces = new List<MazePiece>
        {
            new() { Type = PieceType.Start,    X = 2, Y = 4, Floor = 0, Rotation = 0 },
            new() { Type = PieceType.Straight,  X = 2, Y = 3, Floor = 0, Rotation = 0 },
            new() { Type = PieceType.Straight,  X = 2, Y = 2, Floor = 0, Rotation = 0 },
            new() { Type = PieceType.LHall,     X = 2, Y = 1, Floor = 0, Rotation = 0 },
            new() { Type = PieceType.Straight,  X = 3, Y = 1, Floor = 0, Rotation = 1 },
            new() { Type = PieceType.THall,     X = 4, Y = 1, Floor = 0, Rotation = 1 },
            new() { Type = PieceType.Exit,      X = 4, Y = 0, Floor = 0, Rotation = 0 },
        };

        // ── Environment ───────────────────────────────────────────────────────
        var worldEnv = new WorldEnvironment();
        var env      = new Godot.Environment();
        env.BackgroundMode     = Godot.Environment.BGMode.Color;
        env.BackgroundColor    = new Color(0f, 0f, 0f);
        env.AmbientLightSource = Godot.Environment.AmbientSource.Color;
        env.AmbientLightColor  = new Color(0.12f, 0.12f, 0.15f);
        env.AmbientLightEnergy = 1.0f;
        env.TonemapMode        = Godot.Environment.ToneMapper.Filmic;
        env.TonemapExposure    = 1.1f;
        worldEnv.Environment   = env;
        AddChild(worldEnv);

        // ── Dungeon mesh ──────────────────────────────────────────────────────
        var builder = new DungeonBuilder();
        builder.Name = "DungeonBuilder";
        AddChild(builder);
        builder.Build(mazeData);

        GD.Print($"ASSERT PASS: Build() completed. Child count = {builder.GetChildCount()}");
        if (builder.GetChildCount() == 0)
            GD.Print("ASSERT FAIL: DungeonBuilder produced no child nodes");

        // ── Camera ────────────────────────────────────────────────────────────
        _cam = new Camera3D { Name = "TestCam", Fov = 90f, Near = 0.05f, Far = 500f };
        AddChild(_cam);
        ApplyWaypoint(0);
        _cam.MakeCurrent();
    }

    public override void _Process(double delta)
    {
        // Each frame → advance to next waypoint
        if (_frame < Waypoints.Length)
            ApplyWaypoint(_frame);
        _frame++;
    }

    void ApplyWaypoint(int idx)
    {
        if (idx >= Waypoints.Length) return;
        var (pos, yaw, pitch)    = Waypoints[idx];
        _cam.Position            = pos;
        _cam.RotationDegrees     = new Vector3(pitch, yaw, 0f);
        _cam.MakeCurrent();
    }
}
