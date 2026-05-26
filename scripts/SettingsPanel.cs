using Godot;

/// Reusable programmatic settings panel.
/// Add to any scene; call Show() / Hide() to control visibility.
/// Fires Closed when the user clicks Close.
public partial class SettingsPanel : Control
{
    [Signal] public delegate void ClosedEventHandler();

    FontFile? _font;

    public override void _Ready()
    {
        _font = GD.Load<FontFile>("res://assets/fonts/Agmena Pro Book.ttf");

        SetAnchorsPreset(LayoutPreset.FullRect);
        Visible = false;

        // Dim backdrop
        var bg = new ColorRect { Color = new Color(0f, 0f, 0f, 0.75f) };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        bg.MouseFilter = MouseFilterEnum.Stop; // block clicks behind panel
        AddChild(bg);

        // CenterContainer fills the whole panel and centers the card reliably
        // regardless of whether the parent is a Control or a CanvasLayer.
        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var card = new PanelContainer();
        card.CustomMinimumSize = new Vector2(440, 0);
        center.AddChild(card);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 18);
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left",   28);
        margin.AddThemeConstantOverride("margin_right",  28);
        margin.AddThemeConstantOverride("margin_top",    24);
        margin.AddThemeConstantOverride("margin_bottom", 24);
        margin.AddChild(vbox);
        card.AddChild(margin);

        // Title
        var title = new Label { Text = "SETTINGS", HorizontalAlignment = HorizontalAlignment.Center };
        if (_font != null) title.AddThemeFontOverride("font", _font);
        title.AddThemeFontSizeOverride("font_size", 28);
        vbox.AddChild(title);

        vbox.AddChild(new HSeparator());

        // ── Fullscreen ────────────────────────────────────────────────────────
        vbox.AddChild(MakeToggleRow("Fullscreen",
            GameSettings.Instance?.Fullscreen ?? true,
            v => GameSettings.Instance?.SetFullscreen(v), _font));

        // ── PSX Retro Effect ──────────────────────────────────────────────────
        vbox.AddChild(MakeToggleRow("PSX Retro Effect",
            GameSettings.Instance?.PSXEffect ?? true,
            v => GameSettings.Instance?.SetPSXEffect(v), _font));

        // ── VSync ─────────────────────────────────────────────────────────────
        vbox.AddChild(MakeToggleRow("VSync",
            GameSettings.Instance?.VSync ?? true,
            v => GameSettings.Instance?.SetVSync(v), _font));

        // ── FOV ───────────────────────────────────────────────────────────────
        vbox.AddChild(MakeFovRow());

        vbox.AddChild(new HSeparator());

        // ── Close ─────────────────────────────────────────────────────────────
        var close = new Button
        {
            Text = "Close",
            CustomMinimumSize = new Vector2(0, 44),
        };
        if (_font != null) close.AddThemeFontOverride("font", _font);
        close.AddThemeFontSizeOverride("font_size", 18);
        close.Pressed += () => { Hide(); EmitSignal(SignalName.Closed); };
        vbox.AddChild(close);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    static HBoxContainer MakeToggleRow(string label, bool initial, System.Action<bool>? onChange, FontFile? font = null)
    {
        var row = new HBoxContainer();

        var lbl = new Label { Text = label, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        if (font != null) lbl.AddThemeFontOverride("font", font);
        lbl.AddThemeFontSizeOverride("font_size", 18);
        row.AddChild(lbl);

        var btn = new CheckButton { ButtonPressed = initial };
        btn.Toggled += on => onChange?.Invoke(on);
        row.AddChild(btn);

        return row;
    }

    Control MakeFovRow()
    {
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 4);

        float curFov = GameSettings.Instance?.FOV ?? 90f;

        var headerRow = new HBoxContainer();
        var lbl = new Label { Text = "Field of View", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        if (_font != null) lbl.AddThemeFontOverride("font", _font);
        lbl.AddThemeFontSizeOverride("font_size", 18);
        headerRow.AddChild(lbl);

        var valLabel = new Label { Text = $"{(int)curFov}" };
        if (_font != null) valLabel.AddThemeFontOverride("font", _font);
        valLabel.AddThemeFontSizeOverride("font_size", 18);
        headerRow.AddChild(valLabel);

        col.AddChild(headerRow);

        var slider = new HSlider
        {
            MinValue = 60,
            MaxValue = 120,
            Step     = 1,
            Value    = curFov,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        slider.ValueChanged += v =>
        {
            valLabel.Text = $"{(int)v}";
            GameSettings.Instance?.SetFOV((float)v);
        };
        col.AddChild(slider);

        return col;
    }
}
