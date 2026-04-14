using Godot;
using System.Collections.Generic;
using System.Linq;

/// res://scenes/DungeonArena.cs
/// Dual-maze arena layout:
///   Maze A is placed so its Exit south face aligns with the arena's north opening.
///   Maze B is Z-flipped and placed so its (flipped) Exit north face aligns with the
///   arena's south opening.
///
/// The Arena acts as the bridge — no fixed coordinates. The Exit tile's world position
/// determines where the entire maze sits. Arena centre is derived from that.
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

    public override void _Ready()
    {
        int slotA = GameState.ArenaSlotA;
        int slotB = GameState.ArenaSlotB;

        var dataA = MazeSerializer.Load(slotA);
        var dataB = MazeSerializer.Load(slotB);

        if (dataA == null || dataA.Pieces.Count == 0 ||
            dataB == null || dataB.Pieces.Count == 0)
        {
            GD.PushError("[DungeonArena] Missing maze data — returning to editor");
            GetTree().ChangeSceneToFile("res://scenes/MapEditor.tscn");
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

        // ── Find Exit pieces to determine arena X alignment ───────────────────
        var exitA = dataA.Pieces.FirstOrDefault(p => p.Type == PieceType.Exit);
        float exitAX = exitA != null
            ? exitA.X * CellSize + CellSize * 0.5f
            : MazeDepth * 0.5f;

        // ── Build Maze A: origin (0,0,0), Exit opens South toward arena ───────
        var builderA = new DungeonBuilder { Name = "MazeA" };
        AddChild(builderA);
        builderA.Build(dataA, Vector3.Zero, Dir.S);

        // ── Flip and build Maze B: origin (0,0,MazeBOffset), Exit opens North ─
        var dataFlipped = FlipMazeZ(dataB);
        var builderB    = new DungeonBuilder { Name = "MazeB" };
        AddChild(builderB);
        builderB.Build(dataFlipped, new Vector3(0f, 0f, MazeBOffset), Dir.N);

        // ── Build arena centred at (exitAX, 0, ArenaCentreZ) ─────────────────
        var arena = new ArenaBuilder { Name = "Arena" };
        AddChild(arena);
        arena.Build(new Vector3(exitAX, 0f, ArenaCentreZ), openNorth: true, openSouth: true);

        // ── Spawn player ───────────────────────────────────────────────────────
        Vector3 spawnPos;
        float   spawnYaw;

        switch (ChosenSpawn)
        {
            case SpawnPoint.MazeB:
            {
                var startB = dataFlipped.Pieces.FirstOrDefault(p => p.Type == PieceType.Start)
                          ?? dataFlipped.Pieces[0];
                float cx = startB.X * CellSize + CellSize * 0.5f;
                float cz = startB.Y * CellSize + CellSize * 0.5f + MazeBOffset;
                float cy = startB.Floor * DungeonBuilder.FloorHeight + 1f;
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
                float cy = startA.Floor * DungeonBuilder.FloorHeight + 1f;
                spawnPos = new Vector3(cx, cy, cz);
                spawnYaw = DirToYaw(PieceDB.GetOpenings(PieceType.Start, startA.Rotation));
                break;
            }
        }

        _player = PlayerController.Spawn(this, spawnPos, spawnYaw);

        // ── HUD ───────────────────────────────────────────────────────────────
        var canvas = new CanvasLayer();
        AddChild(canvas);
        var hint = new Label
        {
            Text     = "ESC = release mouse   ESC again = back to editor",
            Position = new Vector2(10, 10),
        };
        hint.AddThemeFontSizeOverride("font_size", 13);
        hint.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
        canvas.AddChild(hint);

        GD.Print($"[DungeonArena] A=slot{slotA} B=slot{slotB} " +
                 $"arenaZ={ArenaCentreZ:F1} mazeBZ={MazeBOffset:F1} spawn={ChosenSpawn}");
    }

    public override void _Input(InputEvent ev)
    {
        if (ev.IsActionPressed("pause") && Input.MouseMode == Input.MouseModeEnum.Visible)
        {
            _player?.ReleaseMouse();
            GetTree().ChangeSceneToFile("res://scenes/MapEditor.tscn");
        }
    }

    // ── Z-flip a maze so it runs in the opposite direction ────────────────────
    static MazeData FlipMazeZ(MazeData src)
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

    static int FlipRotation(PieceType type, int rot) => type switch
    {
        PieceType.LHall => rot ^ 1,
        PieceType.THall => rot switch { 1 => 3, 3 => 1, _ => rot },
        PieceType.Start => (rot + 2) % 4,
        PieceType.Exit  => (rot + 2) % 4,
        _               => rot,
    };

    static float DirToYaw(Dir dir)
    {
        if ((dir & Dir.N) != 0) return 0f;
        if ((dir & Dir.E) != 0) return 90f;
        if ((dir & Dir.S) != 0) return 180f;
        if ((dir & Dir.W) != 0) return 270f;
        return 0f;
    }
}
