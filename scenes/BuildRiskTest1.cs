using Godot;
using System.Collections.Generic;

/// Headless scene builder for Risk Task 1 verification.
/// Builds a small test dungeon (straight hall + L-corner + T-junction) and saves to RiskTest1.tscn.
/// Run: dotnet build && godot --headless --script scenes/BuildRiskTest1.cs
public partial class BuildRiskTest1 : SceneBuilderBase
{
    public override void _Initialize()
    {
        GD.Print("Building RiskTest1 scene...");

        var temp = new Node();
        var root = new Node3D();
        root.Name = "RiskTest1";
        temp.AddChild(root);

        // ── Environment (black void — liminal) ────────────────────────────────
        var worldEnv = new WorldEnvironment();
        var env = new Godot.Environment();
        env.BackgroundMode = Godot.Environment.BGMode.Color;
        env.BackgroundColor = new Color(0f, 0f, 0f);
        env.AmbientLightSource = Godot.Environment.AmbientSource.Color;
        env.AmbientLightColor = new Color(0.12f, 0.12f, 0.14f);
        env.AmbientLightEnergy = 1.0f;
        env.TonemapMode = Godot.Environment.ToneMapper.Filmic;
        worldEnv.Environment = env;
        root.AddChild(worldEnv);

        // ── Test maze data (multi-piece dungeon) ─────────────────────────────
        var mazeData = new MazeData { Name = "RiskTest" };
        // Column of straight halls going north, then an L-turn east, then a T
        var pieces = new List<MazePiece>
        {
            new() { Type = PieceType.Start,    X = 2, Y = 4, Floor = 0, Rotation = 0 },
            new() { Type = PieceType.Straight,  X = 2, Y = 3, Floor = 0, Rotation = 0 },
            new() { Type = PieceType.Straight,  X = 2, Y = 2, Floor = 0, Rotation = 0 },
            new() { Type = PieceType.LHall,     X = 2, Y = 1, Floor = 0, Rotation = 0 }, // N+E
            new() { Type = PieceType.Straight,  X = 3, Y = 1, Floor = 0, Rotation = 1 }, // E+W (rot=1 rotates N+S → E+W)
            new() { Type = PieceType.THall,     X = 4, Y = 1, Floor = 0, Rotation = 1 }, // W+N+E (rot=1)
            new() { Type = PieceType.Exit,      X = 4, Y = 0, Floor = 0, Rotation = 0 },
        };
        mazeData.Pieces = pieces;

        // ── DungeonBuilder node ───────────────────────────────────────────────
        var builder = new Node3D();
        builder.Name = "DungeonBuilder";
        builder.SetScript(GD.Load("res://scripts/DungeonBuilder.cs"));
        root.AddChild(builder);

        // ── Camera for the test harness ───────────────────────────────────────
        var cam = new Camera3D();
        cam.Name = "TestCamera";
        // Position inside the straight hall looking north toward the L-turn
        cam.Position = new Vector3(
            2 * DungeonBuilder.CellSize + DungeonBuilder.CellSize * 0.5f,
            DungeonBuilder.CellHeight  * 0.35f,
            3 * DungeonBuilder.CellSize + DungeonBuilder.CellSize * 0.5f
        );
        cam.RotationDegrees = new Vector3(-8f, 180f, 0f); // look north
        cam.Fov = 90f;
        cam.Near = 0.1f;
        root.AddChild(cam);

        // Store maze data as metadata so the test harness can call Build()
        // (The test harness script reads it from the node tree)
        root.SetMeta("maze_pieces_count", mazeData.Pieces.Count);

        var rootNode = temp.GetChild(0);
        temp.RemoveChild(rootNode);
        temp.Free();

        PackAndSave(rootNode, "res://scenes/RiskTest1.tscn");
    }
}
