using Godot;
using System.Collections.Generic;
using System.Linq;

/// res://scenes/DungeonArena.cs
/// Orchestrates the dual-maze arena layout:
///   Maze A  →  z = 0 … 100,  Exit opens South into arena
///   Arena   →  centre z = arenaZ (between the two maze exit faces)
///   Maze B  →  z-flipped, placed beyond the arena, Exit opens North into arena
///
/// The arena room is an ArenaBuilder (28-sided polygon, radius=30m).
/// Face 0 (north side, angle=0) opens toward Maze A's exit.
/// Face 14 (south side, angle=π) opens toward Maze B's exit.
public partial class DungeonArena : Node3D
{
    // Each maze grid is 10×10 cells × CellSize=10 m → 100 m footprint.
    // Exit is always in the last row (y=9 on floor 0).
    // The exit cell's south face sits at z = (9+1)*10 = 100 m from that maze's origin.
    // We leave a 60 m gap for the arena (radius 30 m), so:
    //   Maze A: origin = (0,0,0),   exit face at z=100
    //   Arena:  centre z = 130 (100 + arena radius 30)
    //   Maze B: flipped and shifted so its exit face also sits at z=160 (130+30)
    //           Its origin is at z = 160 + 100 = 260, but since it's Z-flipped,
    //           we pass offset (0,0,160) and flip the maze data.
    const float MazeCells   = 10f;
    const float CellSize    = DungeonBuilder.CellSize;
    const float ArenaRadius = 30f;
    const float MazeDepth   = MazeCells * CellSize;   // 100 m
    const float ArenaCentreZ = MazeDepth + ArenaRadius; // 130 m
    const float MazeBOffset = MazeDepth + ArenaRadius * 2f; // 160 m

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

        // ── Find exit X positions (for arena centre X alignment) ──────────────
        var exitA = dataA.Pieces.FirstOrDefault(p => p.Type == PieceType.Exit);
        var exitB = dataB.Pieces.FirstOrDefault(p => p.Type == PieceType.Exit);

        float exitAX = exitA != null
            ? exitA.X * CellSize + CellSize * 0.5f
            : MazeCells * CellSize * 0.5f;

        // Determine the open direction: Exit piece at rotation=0 opens North (Dir.N).
        // The default Exit opens North. In our layout, Maze A's exit should open South
        // (toward the arena). We tell DungeonBuilder to leave the South face of that
        // Exit tile open. For Maze B (flipped), the exit opens North (toward arena).
        Dir openA = Dir.S;   // Maze A exit faces south toward arena
        Dir openB = Dir.N;   // Maze B exit faces north toward arena (after Z-flip)

        // ── Build Maze A ──────────────────────────────────────────────────────
        var builderA = new DungeonBuilder { Name = "MazeA" };
        AddChild(builderA);
        builderA.Build(dataA, new Vector3(0f, 0f, 0f), openA);

        // ── Flip Maze B and build it beyond the arena ─────────────────────────
        var dataFlipped = FlipMazeZ(dataB);
        var builderB    = new DungeonBuilder { Name = "MazeB" };
        AddChild(builderB);
        builderB.Build(dataFlipped, new Vector3(0f, 0f, MazeBOffset), openB);

        // ── Build arena room ───────────────────────────────────────────────────
        // Centre X = same as Maze A exit for alignment; Z = midpoint between exits.
        float arenaCX = exitAX;
        var arena = new ArenaBuilder { Name = "Arena" };
        AddChild(arena);
        arena.Build(new Vector3(arenaCX, 0f, ArenaCentreZ), openNorth: 0, openSouth: 14);

        // ── Spawn player at Maze A Start ──────────────────────────────────────
        var startA = dataA.Pieces.FirstOrDefault(p => p.Type == PieceType.Start)
                  ?? dataA.Pieces[0];
        float cx = startA.X * CellSize + CellSize * 0.5f;
        float cz = startA.Y * CellSize + CellSize * 0.5f;
        float cy = startA.Floor * DungeonBuilder.FloorHeight + 1.0f;
        Dir spawnDir = PieceDB.GetOpenings(PieceType.Start, startA.Rotation);
        float spawnYaw = DirToYaw(spawnDir);

        _player = PlayerController.Spawn(this, new Vector3(cx, cy, cz), spawnYaw);

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

        GD.Print($"[DungeonArena] SlotA={slotA} SlotB={slotB}  " +
                 $"Player at ({cx:F0},{cy:F0},{cz:F0})");
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
    // Mirrors Y coordinates within the 10×10 grid and adjusts rotations so the
    // corridor openings still face the right directions after the flip.
    static MazeData FlipMazeZ(MazeData src)
    {
        const int GridH = 10;
        var dst = new MazeData { Name = src.Name + "_flipped", GoldSpent = src.GoldSpent };
        dst.Pieces = new List<MazePiece>();

        foreach (var p in src.Pieces)
        {
            int newY = (GridH - 1) - p.Y;
            int newRot = FlipRotation(p.Type, p.Rotation);
            dst.Pieces.Add(new MazePiece
            {
                Type     = p.Type,
                X        = p.X,
                Y        = newY,
                Floor    = p.Floor,
                Rotation = newRot,
            });
        }
        return dst;
    }

    // Adjusts rotation after a Z (north-south) mirror.
    // N↔S are swapped; E and W are unchanged.
    // Rotations represent clockwise quarter-turns, so we need to remap
    // each piece's "effective direction" accordingly.
    static int FlipRotation(PieceType type, int rot)
    {
        // Helper: rotate a dir mask through Z-flip (N↔S swap)
        // We check each piece type's base opening and find which rotation
        // after flipping maps back to the desired opening set.
        //
        // Simpler: just remap the rotation using the pattern for each type.
        // Straight / Stairs: N↔S at rot=0; rot=1 has E+W (unchanged under N↔S flip).
        //   rot0(N+S) → same  rot1(E+W) → same  so both unchanged.
        // LHall base=(N+E): flip → (S+E); which rotation gives (S+E)?
        //   rot0=N+E, rot1=E+S, so rot1. Pattern: rot→(rot+1)%4... no:
        //   rot0→rot1, rot1→rot0? Let's enumerate all 4:
        //   rot0=(N+E)→(S+E)=rot1, rot1=(E+S)→(E+N)=rot0, rot2=(S+W)→(N+W)=rot3, rot3=(W+N)→(W+S)=rot2
        //   So: {0→1, 1→0, 2→3, 3→2} → rot ^ 1
        // THall base=(N+E+S): flip → (S+E+N) = same set → rot unchanged?
        //   rot0=(N+E+S), flip=(S+E+N)=same. rot1=(E+S+W), flip=(E+N+W)=rot3.
        //   rot2=(S+W+N), flip=(N+W+S)=same. rot3=(W+N+E), flip=(W+S+E)=rot1.
        //   So: {0→0, 1→3, 2→2, 3→1}
        // Start base=S: flip→N=rot2. rot0→rot2, rot1→rot3, rot2→rot0, rot3→rot1 → (rot+2)%4
        // Exit base=N: flip→S=rot2. same: (rot+2)%4
        // Stairs: same as Straight (has both N and S), unchanged.

        return type switch
        {
            PieceType.LHall    => rot ^ 1,
            PieceType.THall    => rot switch { 1 => 3, 3 => 1, _ => rot },
            PieceType.Start    => (rot + 2) % 4,
            PieceType.Exit     => (rot + 2) % 4,
            _                  => rot,  // Straight, Stairs — symmetric under N↔S
        };
    }

    static float DirToYaw(Dir dir)
    {
        if ((dir & Dir.N) != 0) return 0f;
        if ((dir & Dir.E) != 0) return 90f;
        if ((dir & Dir.S) != 0) return 180f;
        if ((dir & Dir.W) != 0) return 270f;
        return 0f;
    }
}
