using Godot;
using System.Linq;

/// res://scenes/DungeonGame.cs
/// Loads the player's saved maze, builds the 3D dungeon, and spawns the first-person player.
/// Entered from MapEditorMain via GameState.ActiveSlot.
/// Escape once = release mouse. Escape again = return to map editor.
public partial class DungeonGame : Node3D
{
    PlayerController? _player;

    public override void _Ready()
    {
        // ── Load maze ─────────────────────────────────────────────────────────
        int slot = GameState.ActiveSlot;
        var data = MazeSerializer.Load(slot);
        if (data == null || data.Pieces == null || data.Pieces.Count == 0)
        {
            GD.PushError($"[DungeonGame] No maze data in slot {slot} — returning to editor");
            GetTree().ChangeSceneToFile("res://scenes/MapEditor.tscn");
            return;
        }

        // ── Environment ───────────────────────────────────────────────────────
        var worldEnv = new WorldEnvironment();
        var env      = new Godot.Environment();
        env.BackgroundMode     = Godot.Environment.BGMode.Color;
        env.BackgroundColor    = new Color(0f, 0f, 0f);
        env.AmbientLightSource = Godot.Environment.AmbientSource.Color;
        env.AmbientLightColor  = new Color(0.10f, 0.07f, 0.04f); // warm dark fill
        env.AmbientLightEnergy = 0.25f;  // low but non-zero — prevents total blackout
        env.TonemapMode        = Godot.Environment.ToneMapper.Filmic;
        env.TonemapExposure    = 1.3f;
        worldEnv.Environment   = env;
        AddChild(worldEnv);

        // ── Build dungeon ─────────────────────────────────────────────────────
        var builder = new DungeonBuilder { Name = "DungeonBuilder" };
        AddChild(builder);
        builder.Build(data);

        // ── Spawn player at Start piece ───────────────────────────────────────
        var startPiece = data.Pieces.FirstOrDefault(p => p.Type == PieceType.Start)
                      ?? data.Pieces[0];

        float cx = startPiece.X * DungeonBuilder.CellSize + DungeonBuilder.CellSize * 0.5f;
        float cz = startPiece.Y * DungeonBuilder.CellSize + DungeonBuilder.CellSize * 0.5f;
        float cy = startPiece.Floor * DungeonBuilder.FloorHeight + 1.0f; // land above floor

        // Face toward the Start piece's single opening (into the dungeon)
        Dir openings = PieceDB.GetOpenings(PieceType.Start, startPiece.Rotation);
        float spawnYaw = DirToYaw(openings);

        _player = PlayerController.Spawn(this, new Vector3(cx, cy, cz), spawnYaw);

        // ── Minimal HUD ───────────────────────────────────────────────────────
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

        GD.Print($"[DungeonGame] Loaded slot {slot}: {data.Pieces.Count} pieces. " +
                 $"Player at ({cx:F0},{cy:F0},{cz:F0}) yaw={spawnYaw}°");
    }

    public override void _Input(InputEvent ev)
    {
        // If mouse is already released (visible) and player presses Escape again → back to editor
        if (ev.IsActionPressed("pause") && Input.MouseMode == Input.MouseModeEnum.Visible)
        {
            _player?.ReleaseMouse();
            GetTree().ChangeSceneToFile("res://scenes/MapEditor.tscn");
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    /// Convert a Dir flag to a yaw in degrees for PlayerController.
    /// yaw=0 → looks north (-Z), yaw=90 → east (+X), yaw=180 → south (+Z), yaw=270 → west (-X)
    static float DirToYaw(Dir dir)
    {
        if ((dir & Dir.N) != 0) return 0f;
        if ((dir & Dir.E) != 0) return 90f;
        if ((dir & Dir.S) != 0) return 180f;
        if ((dir & Dir.W) != 0) return 270f;
        return 0f;
    }
}
