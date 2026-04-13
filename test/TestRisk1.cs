using Godot;
using System.Collections.Generic;

/// Risk Task 1 visual test harness.
/// Builds a test dungeon and orbits a camera through it to capture screenshots.
/// Run: godot --write-movie screenshots/risk1/frame.png --fixed-fps 1 --quit-after 6 --script test/TestRisk1.cs
public partial class TestRisk1 : SceneTree
{
    Camera3D _cam   = null!;
    int      _frame = 0;

    // Camera waypoints — each is (position, yaw_degrees)
    static readonly (Vector3 pos, float yaw)[] Waypoints = {
        // Inside first straight hall, looking north
        (new Vector3(25f, 2.2f, 37f),  180f),
        // At the L-turn corner, looking east
        (new Vector3(25f, 2.2f, 15f),   -90f),
        // In the E-W hall looking east toward the T-junction
        (new Vector3(38f, 2.2f, 15f),   -90f),
        // At the T-junction looking north toward the exit branch
        (new Vector3(45f, 2.2f, 15f),   180f),
        // Wide overhead view from above
        (new Vector3(35f, 12f, 28f),    0f),
        // Low angle at the start, looking up the tall corridor
        (new Vector3(25f, 0.5f, 42f),  180f),
    };

    public override void _Initialize()
    {
        // Build test maze
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

        // Environment — black void
        var worldEnv = new WorldEnvironment();
        var env = new Godot.Environment();
        env.BackgroundMode    = Godot.Environment.BGMode.Color;
        env.BackgroundColor   = new Color(0f, 0f, 0f);
        env.AmbientLightSource = Godot.Environment.AmbientSource.Color;
        env.AmbientLightColor  = new Color(0.15f, 0.15f, 0.18f);
        env.AmbientLightEnergy = 1.0f;
        env.TonemapMode       = Godot.Environment.ToneMapper.Filmic;
        env.TonemapExposure   = 1.2f;
        worldEnv.Environment  = env;
        Root.AddChild(worldEnv);

        // DungeonBuilder — instantiate directly (C# class, no SetScript needed)
        var builder = new DungeonBuilder();
        builder.Name = "DungeonBuilder";
        Root.AddChild(builder);
        builder.Build(mazeData);

        // Camera
        _cam = new Camera3D();
        _cam.Name = "TestCam";
        _cam.Fov  = 90f;
        _cam.Near = 0.05f;
        _cam.Far  = 500f;
        Root.AddChild(_cam);
        SetCameraToWaypoint(0);
        _cam.MakeCurrent();

        GD.Print("ASSERT PASS: DungeonBuilder.Build() completed without crash");
        GD.Print($"ASSERT: Child count after build = {builder.GetChildCount()} (expect > 0)");
        if (builder.GetChildCount() > 0)
            GD.Print("ASSERT PASS: Dungeon mesh nodes were created");
        else
            GD.Print("ASSERT FAIL: No mesh nodes generated");
    }

    public override bool _Process(double delta)
    {
        // Advance camera to next waypoint each frame
        if (_frame < Waypoints.Length)
            SetCameraToWaypoint(_frame);
        _frame++;
        return false;
    }

    void SetCameraToWaypoint(int idx)
    {
        if (idx >= Waypoints.Length) return;
        var (pos, yaw) = Waypoints[idx];
        _cam.Position       = pos;
        _cam.RotationDegrees = new Vector3(-5f, yaw, 0f);
        _cam.MakeCurrent();
    }
}
