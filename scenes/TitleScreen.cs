using Godot;
using System;

/// Title screen — Wicked Knight font, black background, flat white buttons.
public partial class TitleScreen : Control
{
    SettingsPanel?      _settingsPanel;
    AudioStreamPlayer? _clickSfx;
    AudioStreamPlayer? _playGameSfx;
    bool _changingScene = false;

    const float CornerPad = 36f;
    const float BtnH      = 52f;

    public override void _Ready()
    {
        Input.MouseMode = Input.MouseModeEnum.Visible;

        // Load the active account's profile so RANK reflects the latest gold.
        PlayerProfile.Load();

        _clickSfx = new AudioStreamPlayer();
        var clickStream = GD.Load<AudioStream>("res://assets/audio/ui/MenuButtonClick.wav");
        if (clickStream != null) _clickSfx.Stream = clickStream;
        AddChild(_clickSfx);

        _playGameSfx = new AudioStreamPlayer();
        var playStream = GD.Load<AudioStream>("res://assets/audio/ui/PlayGameButtonNoise.wav");
        if (playStream != null) _playGameSfx.Stream = playStream;
        AddChild(_playGameSfx);

        SetAnchorsPreset(LayoutPreset.FullRect);
        GrowHorizontal = GrowDirection.Both;
        GrowVertical   = GrowDirection.Both;

        var titleFont = GD.Load<FontFile>("res://assets/fonts/Agmena Pro Book.ttf");
        var font      = GD.Load<FontFile>("res://assets/fonts/Agmena Pro Book.ttf");

        // ── Background ────────────────────────────────────────────────────────
        var bg = new ColorRect { Color = Colors.Black };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        // ── Centre column: title + buttons ────────────────────────────────────
        var outer = new VBoxContainer();
        outer.SetAnchorsPreset(LayoutPreset.FullRect);
        outer.AddThemeConstantOverride("separation", 0);
        AddChild(outer);

        outer.AddChild(VSpacer());

        var hrow = new HBoxContainer();
        hrow.AddThemeConstantOverride("separation", 0);
        outer.AddChild(hrow);
        hrow.AddChild(HSpacer());

        var vbox = new VBoxContainer();
        vbox.CustomMinimumSize = new Vector2(380, 0);
        vbox.AddThemeConstantOverride("separation", 0);
        hrow.AddChild(vbox);
        hrow.AddChild(HSpacer());

        for (int i = 0; i < 3; i++) outer.AddChild(VSpacer());

        // Title
        var title = new Label { Text = "PALISADE", HorizontalAlignment = HorizontalAlignment.Center };
        if (titleFont != null) title.AddThemeFontOverride("font", titleFont);
        title.AddThemeFontSizeOverride("font_size", 108);
        title.AddThemeColorOverride("font_color", Colors.White);
        vbox.AddChild(title);

        // Fame readout (same underlying gold pool — labelled as the player's rank/fame on the menu).
        var fameLbl = new Label
        {
            Text                = $"FAME: {OnlineGameState.Gold}",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        if (font != null) fameLbl.AddThemeFontOverride("font", font);
        fameLbl.AddThemeFontSizeOverride("font_size", 22);
        fameLbl.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.3f));
        vbox.AddChild(fameLbl);

        vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 36) });

        var mazeBtn = MakeBtn("Maze", font, 26, OnMaze);
        if (!HasAnyOnlineMaze())
        {
            mazeBtn.Text = "! Maze";
            mazeBtn.AddThemeColorOverride("font_color",         new Color(1f, 0.85f, 0f));
            mazeBtn.AddThemeColorOverride("font_hover_color",   new Color(1f, 1f,    0.4f));
            mazeBtn.AddThemeColorOverride("font_pressed_color", new Color(0.8f, 0.7f, 0f));
            mazeBtn.AddThemeColorOverride("font_focus_color",   new Color(1f, 0.85f, 0f));
        }
        vbox.AddChild(MakeBtn("Play Game", font, 30, OnPlayGame));
        vbox.AddChild(mazeBtn);
        vbox.AddChild(MakeBtn("Training",  font, 26, OnTraining));
        vbox.AddChild(MakeBtn("Settings",  font, 26, OnSettings));

        // ── Bottom-centre: Exit ───────────────────────────────────────────────
        var exitBtn = MakeBtn("Exit", font, 20, () => { _clickSfx?.Play(); GetTree().Quit(); });
        exitBtn.CustomMinimumSize = new Vector2(120, 42);
        exitBtn.AnchorLeft     = 0.5f; exitBtn.AnchorRight  = 0.5f;
        exitBtn.AnchorTop      = 1f;   exitBtn.AnchorBottom = 1f;
        exitBtn.GrowHorizontal = GrowDirection.Both;
        exitBtn.GrowVertical   = GrowDirection.Begin;
        exitBtn.OffsetLeft     = -60f;
        exitBtn.OffsetRight    =  60f;
        exitBtn.OffsetTop      = -(CornerPad + 42f);
        exitBtn.OffsetBottom   = -CornerPad;
        exitBtn.AddThemeColorOverride("font_color",       new Color(0.45f, 0.45f, 0.45f));
        exitBtn.AddThemeColorOverride("font_hover_color", new Color(0.75f, 0.75f, 0.75f));
        AddChild(exitBtn);

        // ── Settings panel overlay ────────────────────────────────────────────
        _settingsPanel = new SettingsPanel { Name = "SettingsPanel" };
        _settingsPanel.Closed += () => _settingsPanel.Hide();
        AddChild(_settingsPanel);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    static Control VSpacer() =>
        new Control { SizeFlagsVertical = SizeFlags.ExpandFill };

    static Control HSpacer() =>
        new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };

    static Button MakeBtn(string label, FontFile? font, int fontSize, Action pressed)
    {
        var btn = new Button
        {
            Text      = label,
            Flat      = true,
            Alignment = HorizontalAlignment.Center,
            CustomMinimumSize = new Vector2(0, BtnH),
        };

        if (font != null) btn.AddThemeFontOverride("font", font);
        btn.AddThemeFontSizeOverride("font_size", fontSize);
        btn.AddThemeColorOverride("font_color",         Colors.White);
        btn.AddThemeColorOverride("font_hover_color",   new Color(0.6f, 0.6f, 0.6f));
        btn.AddThemeColorOverride("font_pressed_color", new Color(0.4f, 0.4f, 0.4f));
        btn.AddThemeColorOverride("font_focus_color",   Colors.White);

        var empty = new StyleBoxEmpty();
        btn.AddThemeStyleboxOverride("normal",   empty);
        btn.AddThemeStyleboxOverride("hover",    empty);
        btn.AddThemeStyleboxOverride("pressed",  empty);
        btn.AddThemeStyleboxOverride("focus",    empty);
        btn.AddThemeStyleboxOverride("disabled", empty);

        btn.Pressed += pressed;
        return btn;
    }

    // ── Navigation ────────────────────────────────────────────────────────────

    void OnSettings()
    {
        _clickSfx?.Play();
        if (_settingsPanel == null) return;
        _settingsPanel.Visible = !_settingsPanel.Visible;
    }

    void OnPlayGame()
    {
        if (_changingScene) return;
        _changingScene = true;
        _clickSfx?.Play();
        OnlineGameState.Reset();
        GetTree().CreateTimer(0.25).Timeout += () =>
            GetTree().ChangeSceneToFile("res://scenes/PlayGameScreen.tscn");
    }

    void OnMaze()
    {
        if (_changingScene) return;
        _changingScene = true;
        _clickSfx?.Play();
        _playGameSfx?.Play();
        GetTree().CreateTimer(0.5).Timeout += () =>
            GetTree().ChangeSceneToFile("res://scenes/MazeEditor3D.tscn");
    }

    void OnTraining()
    {
        if (_changingScene) return;
        _changingScene = true;
        _clickSfx?.Play();
        GetTree().ChangeSceneToFile("res://scenes/TrainingSetupScreen.tscn");
    }

    static bool HasAnyOnlineMaze()
    {
        for (int i = 0; i < MazeSerializer.SlotCount; i++)
        {
            if (!MazeSerializer.Exists(i)) continue;
            var d = MazeSerializer.Load(i);
            if (d?.IsOnline == true) return true;
        }
        return false;
    }
}
