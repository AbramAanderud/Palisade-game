using Godot;

/// Autoload CanvasLayer — hidden by default.
/// ESC opens/closes the pause menu in every scene except the title screen
/// (the title already has its own Settings panel).
/// Three panels swap: Main → Character Settings → Exit Confirmation.
public partial class PauseMenu : CanvasLayer
{
    Control   _mainPanel     = null!;
    Control   _settingsPanel = null!;
    Control   _confirmPanel  = null!;
    Label     _confirmLabel  = null!;
    Button    _exitBtn       = null!;
    FontFile? _font;

    // Mouse mode at the moment Open() was called — restored on Close()
    // so editor scenes (visible) don't get auto-captured like gameplay does.
    Input.MouseModeEnum _prevMouseMode = Input.MouseModeEnum.Visible;

    public override void _Ready()
    {
        _font = GD.Load<FontFile>("res://assets/fonts/Agmena Pro Book.ttf");
        Layer       = 50;
        Visible     = false;
        ProcessMode = ProcessModeEnum.Always;

        var dim = new ColorRect { Color = new Color(0f, 0f, 0f, 0.68f) };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        dim.MouseFilter = Control.MouseFilterEnum.Stop;
        AddChild(dim);

        _mainPanel     = BuildMain();
        _settingsPanel = BuildCharSettings();
        _confirmPanel  = BuildConfirm();

        AddChild(_mainPanel);
        AddChild(_settingsPanel);
        AddChild(_confirmPanel);

        _settingsPanel.Visible = false;
        _confirmPanel.Visible  = false;
    }

    // ── Input ─────────────────────────────────────────────────────────────────

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey key || !key.Pressed || key.Echo) return;
        if (key.Keycode != Key.Escape) return;
        if (!IsGameplayScene()) return;

        if (_confirmPanel.Visible)  { ShowMain(); GetViewport().SetInputAsHandled(); return; }
        if (_settingsPanel.Visible) { ShowMain(); GetViewport().SetInputAsHandled(); return; }
        if (Visible)                { Close();    GetViewport().SetInputAsHandled(); return; }

        Open();
        GetViewport().SetInputAsHandled();
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public void Open()
    {
        _prevMouseMode   = Input.MouseMode;
        Visible          = true;
        GetTree().Paused = true;
        Input.MouseMode  = Input.MouseModeEnum.Visible;

        // In the editor scenes the "exit" action returns to the title screen, so call it
        // "Main Menu" there. In gameplay scenes it leaves the match/playtest, so "Exit Game"
        // matches the player's mental model.
        var p = GetTree().CurrentScene?.SceneFilePath ?? "";
        bool isGameplay = p.Contains("DungeonArena") || p.Contains("TrainingArena");
        _exitBtn.Text = isGameplay ? "Exit Game" : "Main Menu";

        ShowMain();
    }

    public void Close()
    {
        Visible          = false;
        GetTree().Paused = false;
        Input.MouseMode  = _prevMouseMode;
    }

    // ── Panel builders ────────────────────────────────────────────────────────

    Control BuildMain()
    {
        var root = MakeCard(out var content);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 14);
        content.AddChild(vbox);

        var header = new Label { Text = "PAUSED", HorizontalAlignment = HorizontalAlignment.Center };
        if (_font != null) header.AddThemeFontOverride("font", _font);
        header.AddThemeFontSizeOverride("font_size", 32);
        vbox.AddChild(header);

        vbox.AddChild(new HSeparator());
        vbox.AddChild(MakeBtn("Resume",    Close));
        vbox.AddChild(MakeBtn("Settings",  ShowSettings));
        vbox.AddChild(new HSeparator());
        _exitBtn = MakeBtn("Exit Game", ShowConfirm);   // label refreshed per-scene on Open()
        vbox.AddChild(_exitBtn);

        return root;
    }

    Control BuildCharSettings()
    {
        var root = MakeCard(out var content, minW: 400);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 16);
        content.AddChild(vbox);

        var header = new Label { Text = "SETTINGS", HorizontalAlignment = HorizontalAlignment.Center };
        if (_font != null) header.AddThemeFontOverride("font", _font);
        header.AddThemeFontSizeOverride("font_size", 28);
        vbox.AddChild(header);

        vbox.AddChild(new HSeparator());

        // FOV
        float curFov = GameSettings.Instance?.FOV ?? 90f;

        var fovRow = new HBoxContainer();
        var fovLbl = new Label { Text = "Field of View", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        if (_font != null) fovLbl.AddThemeFontOverride("font", _font);
        fovLbl.AddThemeFontSizeOverride("font_size", 17);
        fovRow.AddChild(fovLbl);

        var fovVal = new Label { Text = $"{(int)curFov}" };
        if (_font != null) fovVal.AddThemeFontOverride("font", _font);
        fovVal.AddThemeFontSizeOverride("font_size", 17);
        fovRow.AddChild(fovVal);
        vbox.AddChild(fovRow);

        var fovSlider = new HSlider { MinValue = 60, MaxValue = 120, Step = 1, Value = curFov,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        fovSlider.ValueChanged += v =>
        {
            fovVal.Text = $"{(int)v}";
            GameSettings.Instance?.SetFOV((float)v);
        };
        vbox.AddChild(fovSlider);

        vbox.AddChild(new HSeparator());
        vbox.AddChild(MakeBtn("Back", ShowMain));

        return root;
    }

    Control BuildConfirm()
    {
        var root = MakeCard(out var content, minW: 340);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 20);
        content.AddChild(vbox);

        _confirmLabel = new Label
        {
            Text                = "Exit to main menu?",
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode        = TextServer.AutowrapMode.Word,
        };
        if (_font != null) _confirmLabel.AddThemeFontOverride("font", _font);
        _confirmLabel.AddThemeFontSizeOverride("font_size", 22);
        vbox.AddChild(_confirmLabel);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 16);
        vbox.AddChild(row);

        var yes = new Button { Text = "Yes", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 44) };
        if (_font != null) yes.AddThemeFontOverride("font", _font);
        yes.AddThemeFontSizeOverride("font_size", 18);
        yes.Pressed += ExitToTitle;
        row.AddChild(yes);

        var no = new Button { Text = "No", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 44) };
        if (_font != null) no.AddThemeFontOverride("font", _font);
        no.AddThemeFontSizeOverride("font_size", 18);
        no.Pressed += ShowMain;
        row.AddChild(no);

        return root;
    }

    // ── Panel switching ───────────────────────────────────────────────────────

    void ShowMain()
    {
        _mainPanel.Visible     = true;
        _settingsPanel.Visible = false;
        _confirmPanel.Visible  = false;
    }

    void ShowSettings()
    {
        _mainPanel.Visible     = false;
        _settingsPanel.Visible = true;
        _confirmPanel.Visible  = false;
    }

    void ShowConfirm()
    {
        var p = GetTree().CurrentScene?.SceneFilePath ?? "";

        // OnlineDungeonArena must be checked BEFORE the more permissive "DungeonArena"
        // substring match, since the online scene path also contains "DungeonArena".
        bool isOnlineMatch = p.Contains("OnlineDungeonArena");
        bool isPlayTest    = !isOnlineMatch && p.Contains("DungeonArena");
        bool isTraining    = !isOnlineMatch && p.Contains("TrainingArena");

        _confirmLabel.Text = isOnlineMatch  ? "Forfeit and exit match?"
                           : isPlayTest     ? "Exit to Maze Editor?"
                           : isTraining     ? "Exit to Training?"
                           : "Exit to main menu?";

        _mainPanel.Visible     = false;
        _settingsPanel.Visible = false;
        _confirmPanel.Visible  = true;
    }

    void ExitToTitle()
    {
        GetTree().Paused = false;
        Visible          = false;
        var path = GetTree().CurrentScene?.SceneFilePath ?? "";

        bool isOnlineMatch = path.Contains("OnlineDungeonArena");
        bool isPlayTest    = !isOnlineMatch && path.Contains("DungeonArena");
        bool isTraining    = !isOnlineMatch && path.Contains("TrainingArena");

        string dest = isOnlineMatch  ? "res://scenes/PlayGameScreen.tscn"
                    : isPlayTest     ? GameState.EditorReturnScene
                    : isTraining     ? "res://scenes/TrainingSetupScreen.tscn"
                                     : "res://scenes/TitleScreen.tscn";
        GetTree().ChangeSceneToFile(dest);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    bool IsGameplayScene()
    {
        var scene = GetTree().CurrentScene;
        if (scene == null) return false;
        var p = scene.SceneFilePath;
        // Pause menu is available in every scene EXCEPT the title screen
        // (which already shows its own Settings panel and has nothing to "resume" from).
        return !p.Contains("TitleScreen");
    }

    // Creates a full-rect CenterContainer → PanelContainer → MarginContainer.
    // Returns the root (CenterContainer) and sets contentHost to the MarginContainer.
    static Control MakeCard(out Control contentHost, float minW = 320)
    {
        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(minW, 0) };
        center.AddChild(panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left",   28);
        margin.AddThemeConstantOverride("margin_right",  28);
        margin.AddThemeConstantOverride("margin_top",    24);
        margin.AddThemeConstantOverride("margin_bottom", 24);
        panel.AddChild(margin);

        contentHost = margin;
        return center;
    }

    Button MakeBtn(string label, System.Action pressed)
    {
        var btn = new Button
        {
            Text              = label,
            CustomMinimumSize = new Vector2(0, 46),
        };
        if (_font != null) btn.AddThemeFontOverride("font", _font);
        btn.AddThemeFontSizeOverride("font_size", 19);
        btn.Pressed += pressed;
        return btn;
    }
}
