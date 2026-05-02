using Godot;
using System.Collections.Generic;

/// Stair inspection harness: 18 camera angles.
/// Frames  1-12: StairsUp   at (5,2,0) — connected floor-1 landing above.
/// Frames 13-18: StairsDown at (2,7,1) — NO floor-0 piece south → cap wall must appear.
///
/// StairsUp (5,2,0):
///   X=[50,60] cx=55  Z=[20,30] cz=25  (z0=20 high/N, z1=30 low/S)
///   geomBase=0  springLo=8.4  springHi=20.8  archPeakHi=24.8
///   Floor-1 landing at (5,1,1), Y=12.4
///
/// StairsDown (2,7,1):
///   X=[20,30] cx=25  Z=[70,80] cz=75  (z0=70 flat/N high end, z1=80 cross/S low end)
///   floor=1 → geomBase = 1*12.4 - 12.4 = 0
///   springLo(S)=8.4  springHi(N)=20.8  archPeakHi(N)=24.8  archPeakLo(S)=12.4
///   NO floor-0 piece at (2,8,0) → south cap wall MUST be present after fix.
public partial class StairInspect : Node3D
{
    int _frame = 0;

    static readonly (float ax, float ay, float az, float tx, float ty, float tz)[] Cameras = new[]
    {
        // ── StairsUp (5,2,0): floors 0→1, high end N (z0=20), low end S (z1=30) ──
        // 1. South approach — player-eye at Straight(5,4,0), looking north toward entrance
        (55f,  1.7f, 45f,   55f,  3f, 28f),
        // 2. Inside stair at low end — just inside south opening, looking up the steps
        (55f,  0.5f, 28.5f, 55f,  8f, 22f),
        // 3. Inside stair midpoint — halfway up, looking at vault ceiling
        (55f,  6f,   25f,   55f, 18f, 21f),
        // 4. Top of stair — floor-1 height near high end, looking back down
        (55f, 13.5f, 21.5f, 55f,  5f, 29f),
        // 5. Floor-1 corridor looking south into stair opening
        (55f, 14f,   14f,   55f, 13f, 22f),
        // 6. Exterior south face (flat entrance side, outside)
        (55f,  6f,   46f,   55f,  4f, 30f),
        // 7. Exterior north face at floor-1 height (high-end opening from outside)
        (55f, 16f,    5f,   55f, 14f, 21f),
        // 8. Exterior east side — full stair profile
        (82f, 10f,   25f,   55f,  6f, 25f),
        // 9. Exterior west side
        (28f, 10f,   25f,   55f,  6f, 25f),
        // 10. Elevated south-east — top/roof of wing shoulders
        (75f, 28f,   50f,   55f,  8f, 25f),
        // 11. Aerial top-down (X offset avoids LookAt singularity)
        (57f, 50f,   25f,   55f,  0f, 25f),
        // 12. Below-floor looking up (X offset avoids LookAt singularity)
        (57f, -5f,   25f,   55f, 10f, 25f),

        // ── StairsDown (2,7,1): floor=1→0, high/flat end N (z0=70), cross/low end S (z1=80) ──
        // No floor-0 piece at (2,8,0) → BUG FIX: south cap wall must be solid here.
        // 13. Floor-1 north approach — looking south into the flat (same-floor) entrance
        (25f, 14f,   56f,   25f, 13f, 72f),
        // 14. Inside tunnel — at floor-1 level near north end, looking south/down the steps
        (25f, 13.5f, 71.5f, 25f,  5f, 79f),
        // 15. Inside midpoint — halfway down, looking toward the south cap wall
        (25f,  6f,   75f,   25f,  1f, 79f),
        // 16. Exterior south face — close-up showing the cap wall (BUG FIX verification)
        (25f,  6f,   92f,   25f,  4f, 80f),
        // 17. Exterior east side — full stair profile
        (52f, 10f,   75f,   25f,  6f, 75f),
        // 18. Aerial top-down (X offset avoids LookAt singularity)
        (27f, 50f,   75f,   25f,  0f, 75f),
    };

    public override void _Ready()
    {
        var data = new MazeData
        {
            Name = "StairInspect",
            Pieces = new List<MazePiece>
            {
                // ── StairsUp section (floor 0→1) ──
                // Floor 0: south approach → StairsUp at (5,2)
                new() { Type = PieceType.Start,     X=5, Y=5, Floor=0, Rotation=0 },
                new() { Type = PieceType.Straight,  X=5, Y=4, Floor=0, Rotation=0 },
                new() { Type = PieceType.Straight,  X=5, Y=3, Floor=0, Rotation=0 },
                new() { Type = PieceType.StairsUp,  X=5, Y=2, Floor=0, Rotation=0 },
                // Floor 1: connected landing at top (high end has neighbor → should stay open)
                new() { Type = PieceType.Straight,  X=5, Y=1, Floor=1, Rotation=0 },
                new() { Type = PieceType.Straight,  X=5, Y=0, Floor=1, Rotation=0 },
                new() { Type = PieceType.Exit,      X=5, Y=9, Floor=1, Rotation=0 },

                // ── StairsDown section (floor 1→0) ──
                // Floor 1: corridor leading to StairsDown
                new() { Type = PieceType.Straight,  X=2, Y=5, Floor=1, Rotation=0 },
                new() { Type = PieceType.Straight,  X=2, Y=6, Floor=1, Rotation=0 },
                new() { Type = PieceType.StairsDown, X=2, Y=7, Floor=1, Rotation=0 },
                // NO floor-0 piece at (2,8,0) → south cap wall must be present
            }
        };

        var builder = new DungeonBuilder();
        AddChild(builder);
        builder.Build(data);

        var env = new WorldEnvironment();
        var e = new Godot.Environment();
        e.AmbientLightSource = Godot.Environment.AmbientSource.Color;
        e.AmbientLightColor  = new Color(0.35f, 0.35f, 0.35f);
        e.AmbientLightEnergy = 1.0f;
        env.Environment = e;
        AddChild(env);

        SetupCamera(0);
    }

    void SetupCamera(int idx)
    {
        var old = GetNodeOrNull<Camera3D>("Cam");
        old?.QueueFree();

        var (ax, ay, az, tx, ty, tz) = Cameras[idx];
        var cam = new Camera3D { Name = "Cam", Current = true };
        cam.Position = new Vector3(ax, ay, az);
        AddChild(cam);
        cam.LookAt(new Vector3(tx, ty, tz));
        cam.Current = true;
    }

    public override void _Process(double delta)
    {
        _frame++;
        if (_frame < 2) return;

        int camIdx = (_frame - 2) / 2;
        if (camIdx < Cameras.Length)
            SetupCamera(camIdx);
        else
            GetTree().Quit();
    }
}
