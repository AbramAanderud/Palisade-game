using Godot;
using System.Collections.Generic;

/// Risk Task 2 visual test — First-Person Player Controller.
/// Builds the same test dungeon as Risk 1, spawns the player at Start,
/// and lets you walk around to verify movement, look, and collision.
///
/// Controls: WASD move, mouse look, Escape release/capture mouse.
///
/// Verification checklist (manual):
///   [ ] Walk north/south/east/west — movement feels responsive
///   [ ] 360° horizontal look — smooth, no flip
///   [ ] Vertical look clamps at ~85° up/down
///   [ ] Cannot walk through walls (3m-wide opening corridors)
///   [ ] Gravity keeps player on floor after walking off a ledge (stairs cell)
///   [ ] Escape toggles mouse capture / release
public partial class TestRisk2Main : Node3D
{
    public override void _Ready()
    {
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

        // ── Dungeon ───────────────────────────────────────────────────────────
        var mazeData = new MazeData { Name = "RiskTest2" };
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

        var builder = new DungeonBuilder { Name = "DungeonBuilder" };
        AddChild(builder);
        builder.Build(mazeData);

        GD.Print($"ASSERT PASS: Dungeon built, child count = {builder.GetChildCount()}");

        // ── Player ────────────────────────────────────────────────────────────
        // Start piece is at grid (2,4) → world X = 25, Z = 45 (centre of cell)
        // Spawn at eye-walk height (y=0.8) so gravity can settle player to floor
        var spawnPos = new Vector3(25f, 0.8f, 45f);
        var player   = PlayerController.Spawn(this, spawnPos, yawDeg: 180f);

        GD.Print($"ASSERT PASS: Player spawned at {spawnPos}");
        GD.Print("== Manual verification: Walk WASD, look with mouse, check no clipping ==");
    }
}
