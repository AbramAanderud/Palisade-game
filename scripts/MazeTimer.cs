using Godot;

/// res://scripts/MazeTimer.cs
/// Counts elapsed time from when the dungeon loads.
/// Call Stop() when the player exits — it freezes the display and stores the
/// result in LastRunSeconds for future analytics use.
///
/// Font: expects res://assets/fonts/Agmena Pro Book.ttf — falls back to default if missing.
public partial class MazeTimer : Node
{
    // ── Analytics hook — write to this on Stop(), read from it anywhere ────────
    public static float LastRunSeconds = 0f;

    public float ElapsedSeconds { get; private set; } = 0f;
    public bool  Running        { get; private set; } = true;

    Label _label = null!;

    public override void _Ready()
    {
        var canvas = new CanvasLayer { Name = "TimerCanvas", Layer = 1 };
        AddChild(canvas);

        _label = new Label
        {
            Text                = "0:00",
            AnchorLeft          = 0.5f,
            AnchorRight         = 0.5f,
            AnchorTop           = 0f,
            AnchorBottom        = 0f,
            GrowHorizontal      = Control.GrowDirection.Both,
            OffsetLeft          = -80f,
            OffsetRight         = 80f,
            OffsetTop           = 52f,
            OffsetBottom        = 90f,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
        };

        if (ResourceLoader.Exists("res://assets/fonts/Agmena Pro Book.ttf"))
        {
            var font = ResourceLoader.Load<FontFile>("res://assets/fonts/Agmena Pro Book.ttf");
            _label.AddThemeFontOverride("font", font);
        }

        _label.AddThemeFontSizeOverride("font_size", 26);
        _label.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f));
        canvas.AddChild(_label);
    }

    public override void _Process(double delta)
    {
        if (!Running) return;
        ElapsedSeconds += (float)delta;
        UpdateDisplay();
    }

    /// Freeze the timer and record the run time for analytics.
    public float Stop()
    {
        if (!Running) return ElapsedSeconds;
        Running        = false;
        LastRunSeconds = ElapsedSeconds;
        _label.AddThemeColorOverride("font_color", new Color(0.55f, 0.55f, 0.55f));
        GD.Print($"[MazeTimer] Run finished: {FormatTime(ElapsedSeconds)}  ({ElapsedSeconds:F2}s)");
        return ElapsedSeconds;
    }

    void UpdateDisplay() => _label.Text = FormatTime(ElapsedSeconds);

    static string FormatTime(float s)
    {
        int mins = (int)(s / 60f);
        int secs = (int)(s % 60f);
        return $"{mins}:{secs:D2}";
    }
}
