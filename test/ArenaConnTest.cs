using Godot;
using System.Collections.Generic;

/// Arena connection test: builds Maze A and Maze B the same way DungeonArena does,
/// with an ArenaBuilder in between.  Captures camera shots from the connection zones
/// to verify that the exit geometry aligns with the arena arch geometry.
///
/// Maze A: Start(5,0) → Straight(5,1..8) → Exit(5,9) at floor 0, exitOpenDir=Dir.S
/// Maze B: Start(5,0) → Straight(5,1..8) → Exit(5,9) at floor 0
///         → Z-flipped: Exit ends up at Y'=0 with exitOpenDir=Dir.N
///
/// After flip, Maze B origin at Z = MazeDepth + 2*Apothem ≈ 159.63 m
/// Arena centre at Z = MazeDepth + Apothem ≈ 129.82 m, X = exit X centre = 55
public partial class ArenaConnTest : Node3D
{
    int _frame = 0;

    const float CellSize   = DungeonBuilder.CellSize;
    const float FloorH     = DungeonBuilder.FloorHeight;
    const float MazeCells  = 10f;
    const float MazeDepth  = MazeCells * CellSize;  // 100 m

    static float Apothem     => ArenaBuilder.Apothem;
    static float ArenaCentreZ => MazeDepth + Apothem;
    static float MazeBOffset  => MazeDepth + 2f * Apothem;

    // Exit X centre (column 5 = X=[50,60], centre=55)
    const float ExitCX = 55f;

    // Camera positions: (ax,ay,az, tx,ty,tz)
    static readonly (float ax, float ay, float az, float tx, float ty, float tz)[] Cameras = new[]
    {
        // ── Maze A → Arena (north arch) ──────────────────────────────────────
        // 1. Inside Maze A exit cell looking south toward arena connection
        (ExitCX, 5f, 88f,   ExitCX, 3f, 101f),
        // 2. Just inside arena north arch, looking north into Maze A
        (ExitCX, 5f, 102f,  ExitCX, 3f, 95f),
        // 3. Wide shot of arena north arch from inside arena
        (ExitCX, 8f, 110f,  ExitCX, 5f, 100f),
        // 4. At arena floor looking at the north arch corridor opening
        (ExitCX, 1.7f, 120f, ExitCX, 1.7f, 100f),

        // ── Arena → Maze B (south arch) ──────────────────────────────────────
        // 5. Inside arena south arch, looking south into Maze B
        (ExitCX, 5f, ArenaCentreZ + Apothem - 2f,  ExitCX, 3f, ArenaCentreZ + Apothem + 8f),
        // 6. Inside Maze B (first corridor after exit), looking north toward arena
        (ExitCX, 5f, MazeBOffset + 12f,             ExitCX, 3f, MazeBOffset + 2f),
        // 7. Wide aerial showing both mazes and arena from above
        (ExitCX + 2f, 80f, MazeDepth * 0.5f,        ExitCX, 0f, ArenaCentreZ),
        // 8. Inside arena looking south, showing both arches
        (ExitCX, 6f, ArenaCentreZ,                   ExitCX, 4f, ArenaCentreZ + 50f),
    };

    public override void _Ready()
    {
        // ── Build Maze A (simple straight corridor + exit at Y=9) ────────────
        var dataA = new MazeData { Name = "MazeA" };
        dataA.Pieces = new List<MazePiece>
        {
            new() { Type = PieceType.Start,    X=5, Y=0, Floor=0, Rotation=0 },
            new() { Type = PieceType.Straight, X=5, Y=1, Floor=0, Rotation=0 },
            new() { Type = PieceType.Straight, X=5, Y=2, Floor=0, Rotation=0 },
            new() { Type = PieceType.Straight, X=5, Y=3, Floor=0, Rotation=0 },
            new() { Type = PieceType.Straight, X=5, Y=4, Floor=0, Rotation=0 },
            new() { Type = PieceType.Straight, X=5, Y=5, Floor=0, Rotation=0 },
            new() { Type = PieceType.Straight, X=5, Y=6, Floor=0, Rotation=0 },
            new() { Type = PieceType.Straight, X=5, Y=7, Floor=0, Rotation=0 },
            new() { Type = PieceType.Straight, X=5, Y=8, Floor=0, Rotation=0 },
            new() { Type = PieceType.Exit,     X=5, Y=9, Floor=0, Rotation=0 },
        };

        // ── Build Maze B (same layout, Z-flipped) ────────────────────────────
        var dataBRaw = new MazeData { Name = "MazeB" };
        dataBRaw.Pieces = new List<MazePiece>
        {
            new() { Type = PieceType.Start,    X=5, Y=0, Floor=0, Rotation=0 },
            new() { Type = PieceType.Straight, X=5, Y=1, Floor=0, Rotation=0 },
            new() { Type = PieceType.Straight, X=5, Y=2, Floor=0, Rotation=0 },
            new() { Type = PieceType.Straight, X=5, Y=3, Floor=0, Rotation=0 },
            new() { Type = PieceType.Straight, X=5, Y=4, Floor=0, Rotation=0 },
            new() { Type = PieceType.Straight, X=5, Y=5, Floor=0, Rotation=0 },
            new() { Type = PieceType.Straight, X=5, Y=6, Floor=0, Rotation=0 },
            new() { Type = PieceType.Straight, X=5, Y=7, Floor=0, Rotation=0 },
            new() { Type = PieceType.Straight, X=5, Y=8, Floor=0, Rotation=0 },
            new() { Type = PieceType.Exit,     X=5, Y=9, Floor=0, Rotation=0 },
        };

        // Z-flip Maze B: Y → (10-1-Y), Exit 9→0 rot=0→2
        var dataB = FlipMazeZ(dataBRaw);

        // ── Compute offsets (same logic as DungeonArena) ──────────────────────
        float exitAX = 5 * CellSize + CellSize * 0.5f;  // 55
        float exitBX = exitAX;
        float offsetBX = exitAX - exitBX;  // 0
        float offsetAY = 0f;
        float offsetBY = 0f;

        // ── Create DungeonBuilders ────────────────────────────────────────────
        var builderA = new DungeonBuilder { Name = "MazeA" };
        AddChild(builderA);
        builderA.Build(dataA, new Vector3(0f, offsetAY, 0f), Dir.S);

        var builderB = new DungeonBuilder { Name = "MazeB" };
        AddChild(builderB);
        builderB.Build(dataB, new Vector3(offsetBX, offsetBY, MazeBOffset), Dir.N);

        // ── Build arena ───────────────────────────────────────────────────────
        var arena = new ArenaBuilder { Name = "Arena" };
        AddChild(arena);
        arena.Build(new Vector3(exitAX, 0f, ArenaCentreZ), openNorth: true, openSouth: true);

        // ── Ambient light ─────────────────────────────────────────────────────
        var env = new WorldEnvironment();
        var e = new Godot.Environment();
        e.AmbientLightSource = Godot.Environment.AmbientSource.Color;
        e.AmbientLightColor  = new Color(0.40f, 0.40f, 0.40f);
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

    static MazeData FlipMazeZ(MazeData src)
    {
        const int GridH = 10;
        var dst = new MazeData { Name = src.Name + "_flip", GoldSpent = src.GoldSpent };
        dst.Pieces = new List<MazePiece>();
        foreach (var p in src.Pieces)
            dst.Pieces.Add(new MazePiece
            {
                Type     = p.Type,
                X        = p.X,
                Y        = (GridH - 1) - p.Y,
                Floor    = p.Floor,
                Rotation = FlipRotation(p.Type, p.Rotation),
            });
        return dst;
    }

    static int FlipRotation(PieceType type, int rot) => type switch
    {
        PieceType.LHall      => rot ^ 1,
        PieceType.THall      => rot switch { 1 => 3, 3 => 1, _ => rot },
        PieceType.Start      => (rot + 2) % 4,
        PieceType.Exit       => (rot + 2) % 4,
        PieceType.Stairs     => rot switch { 0 => 2, 2 => 0, _ => rot },
        PieceType.StairsUp   => rot switch { 0 => 2, 2 => 0, _ => rot },
        PieceType.StairsDown => rot switch { 0 => 2, 2 => 0, _ => rot },
        _                    => rot,
    };
}
