using Godot;
using System.Collections.Generic;
using System.Linq;

/// res://scenes/DungeonArena.cs
/// Dual-maze arena layout:
///   Maze A is placed so its Exit south face aligns with the arena's north opening.
///   Maze B is Z-flipped and placed so its (flipped) Exit north face aligns with the
///   arena's south opening.
///
///   Maze A origin Z = 0
///   Exit A south face = MazeDepth  (row 9, south face = 10*CellSize = 100 m)
///   Arena north apothem = exit south face  →  ArenaCentreZ = MazeDepth + Apothem
///   Arena south apothem = ArenaCentreZ + Apothem = MazeDepth + 2*Apothem
///   Maze B origin Z    = MazeDepth + 2*Apothem  (flipped maze exit face at this Z)
public partial class DungeonArena : Node3D
{
    const float CellSize  = DungeonBuilder.CellSize;   // 10 m
    const float MazeCells = 10f;
    const float MazeDepth = MazeCells * CellSize;      // 100 m

    // Exact gap-free layout constants derived from ArenaBuilder geometry
    static float Apothem      => ArenaBuilder.Apothem;       // ≈ 29.82 m
    static float ArenaCentreZ => MazeDepth + Apothem;        // ≈ 129.82 m
    static float MazeBOffset  => MazeDepth + 2f * Apothem;   // ≈ 159.63 m

    // ── Spawn options ─────────────────────────────────────────────────────────
    public enum SpawnPoint { MazeA, MazeB, Arena }
    public static SpawnPoint ChosenSpawn = SpawnPoint.MazeA;

    PlayerController? _player;
    MazeTimer?        _mazeTimer;   // stopped by exit trigger when implemented

    public override void _Ready()
    {
        int slotA = GameState.ArenaSlotA;
        int slotB = GameState.ArenaSlotB;

        var dataA = MazeSerializer.Load(slotA);
        var dataB = MazeSerializer.Load(slotB);

        if (dataA == null || dataA.Pieces.Count == 0 ||
            dataB == null || dataB.Pieces.Count == 0)
        {
            GD.PushError("[DungeonArena] Missing maze data — returning to title");
            GetTree().ChangeSceneToFile("res://scenes/TitleScreen.tscn");
            return;
        }

        // ── Environment ───────────────────────────────────────────────────────
        var worldEnv = new WorldEnvironment();
        var env      = new Godot.Environment();
        env.BackgroundMode     = Godot.Environment.BGMode.Color;
        env.BackgroundColor    = new Color(0f, 0f, 0f);
        env.AmbientLightSource = Godot.Environment.AmbientSource.Color;
        env.AmbientLightColor  = new Color(0.10f, 0.07f, 0.04f);
        env.AmbientLightEnergy = 0.25f;
        env.TonemapMode        = Godot.Environment.ToneMapper.Filmic;
        env.TonemapExposure    = 1.3f;
        worldEnv.Environment   = env;
        AddChild(worldEnv);

        // ── Find Exit pieces — their floor drives Y offset so both meet arena Y=0 ─
        var exitA = dataA.Pieces.FirstOrDefault(p => p.Type == PieceType.Exit);
        var exitB = dataB.Pieces.FirstOrDefault(p => p.Type == PieceType.Exit);

        // X centre of the arena aligns with Maze A's exit column.
        float exitAX = exitA != null
            ? exitA.X * CellSize + CellSize * 0.5f
            : MazeDepth * 0.5f;

        // Maze B exit X (X is unchanged by FlipMazeZ). Offset Maze B so its exit column
        // lines up with the arena south opening, which is centred at exitAX.
        float exitBX = exitB != null
            ? exitB.X * CellSize + CellSize * 0.5f
            : exitAX;
        float offsetBX = exitAX - exitBX;

        // Each maze is shifted down in Y so its exit piece's floor sits at Y=0 (arena floor level).
        float offsetAY = exitA != null ? -exitA.Floor  * DungeonBuilder.FloorHeight : 0f;
        float offsetBY = exitB != null ? -exitB.Floor  * DungeonBuilder.FloorHeight : 0f;

        // ── Build Maze A: Y-anchored so its exit floor sits at world Y=0 ──────
        var builderA = new DungeonBuilder { Name = "MazeA" };
        AddChild(builderA);
        builderA.Build(dataA, new Vector3(0f, offsetAY, 0f), Dir.S);

        // ── Flip and build Maze B: X-shifted so exit aligns with arena south opening ──
        var dataFlipped = FlipMazeZ(dataB);
        var builderB    = new DungeonBuilder { Name = "MazeB" };
        AddChild(builderB);
        builderB.Build(dataFlipped, new Vector3(offsetBX, offsetBY, MazeBOffset), Dir.N);

        // ── Torch signal systems — BFS wave from Exit after 20 s ─────────────
        var torchA = new TorchSignalSystem { Name = "TorchSignalA" };
        AddChild(torchA);
        torchA.Init(dataA, builderA);

        var torchB = new TorchSignalSystem { Name = "TorchSignalB" };
        AddChild(torchB);
        torchB.Init(dataFlipped, builderB);

        // ── Build arena centred at (exitAX, 0, ArenaCentreZ) ─────────────────
        var arena = new ArenaBuilder { Name = "Arena" };
        AddChild(arena);
        arena.Build(new Vector3(exitAX, 0f, ArenaCentreZ), openNorth: true, openSouth: true);

        // ── Sword pickup at arena centre ───────────────────────────────────────
        WeaponPickup.Spawn(this, new Vector3(exitAX, 1.5f, ArenaCentreZ));

        // ── Spawn player ───────────────────────────────────────────────────────
        Vector3 spawnPos;
        float   spawnYaw;

        switch (ChosenSpawn)
        {
            case SpawnPoint.MazeB:
            {
                var startB = dataFlipped.Pieces.FirstOrDefault(p => p.Type == PieceType.Start)
                          ?? dataFlipped.Pieces[0];
                float cx = startB.X * CellSize + CellSize * 0.5f + offsetBX;
                float cz = startB.Y * CellSize + CellSize * 0.5f + MazeBOffset;
                float cy = startB.Floor * DungeonBuilder.FloorHeight + 1f + offsetBY;
                spawnPos = new Vector3(cx, cy, cz);
                spawnYaw = DirToYaw(PieceDB.GetOpenings(PieceType.Start, startB.Rotation));
                break;
            }
            case SpawnPoint.Arena:
                spawnPos = new Vector3(exitAX, 1f, ArenaCentreZ);
                spawnYaw = 0f;
                break;
            default: // MazeA
            {
                var startA = dataA.Pieces.FirstOrDefault(p => p.Type == PieceType.Start)
                          ?? dataA.Pieces[0];
                float cx = startA.X * CellSize + CellSize * 0.5f;
                float cz = startA.Y * CellSize + CellSize * 0.5f;
                float cy = startA.Floor * DungeonBuilder.FloorHeight + 1f + offsetAY;
                spawnPos = new Vector3(cx, cy, cz);
                spawnYaw = DirToYaw(PieceDB.GetOpenings(PieceType.Start, startA.Rotation));
                break;
            }
        }

        _player = PlayerController.Spawn(this, spawnPos, spawnYaw);

        _mazeTimer = new MazeTimer { Name = "MazeTimer" };
        AddChild(_mazeTimer);

        GD.Print($"[DungeonArena] A=slot{slotA}(exitX={exitAX:F1},exitFloor={exitA?.Floor ?? 0},offsetY={offsetAY:F1}) " +
                 $"B=slot{slotB}(exitX={exitBX:F1},offsetBX={offsetBX:F1},exitFloor={exitB?.Floor ?? 0},offsetY={offsetBY:F1}) " +
                 $"arenaZ={ArenaCentreZ:F1} mazeBZ={MazeBOffset:F1} spawn={ChosenSpawn}");
    }

    // ── Z-flip a maze so it runs in the opposite direction ────────────────────
    internal static MazeData FlipMazeZ(MazeData src)
    {
        const int GridH = 10;
        var dst = new MazeData { Name = src.Name + "_flipped", GoldSpent = src.GoldSpent };
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

    internal static int FlipRotation(PieceType type, int rot) => type switch
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

    internal static float DirToYaw(Dir dir)
    {
        if ((dir & Dir.N) != 0) return 0f;
        if ((dir & Dir.E) != 0) return 90f;
        if ((dir & Dir.S) != 0) return 180f;
        if ((dir & Dir.W) != 0) return 270f;
        return 0f;
    }
}
