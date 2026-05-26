using System;
using Godot;

/// Training mode setup screen: pick dummy count (1-10), then start.
public partial class TrainingSetupScreen : Control
{
    const float BtnH      = 52f;
    const float CornerPad = 36f;

    int                _dummyCount  = 3;
    Label              _countLabel  = null!;
    FontFile?          _font;
    AudioStreamPlayer? _clickSfx;
    AudioStreamPlayer? _playGameSfx;

    public override void _Ready()
    {
        Input.MouseMode = Input.MouseModeEnum.Visible;
        SetAnchorsPreset(LayoutPreset.FullRect);
        GrowHorizontal = GrowDirection.Both;
        GrowVertical   = GrowDirection.Both;

        _font = GD.Load<FontFile>("res://assets/fonts/Agmena Pro Book.ttf");

        _clickSfx = new AudioStreamPlayer();
        var clickStream = GD.Load<AudioStream>("res://assets/audio/ui/MenuButtonClick.wav");
        if (clickStream != null) _clickSfx.Stream = clickStream;
        AddChild(_clickSfx);

        _playGameSfx = new AudioStreamPlayer();
        var playStream = GD.Load<AudioStream>("res://assets/audio/ui/PlayGameButtonNoise.wav");
        if (playStream != null) _playGameSfx.Stream = playStream;
        AddChild(_playGameSfx);

        var bg = new ColorRect { Color = Colors.Black };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        // Centre column
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
        vbox.CustomMinimumSize = new Vector2(480, 0);
        vbox.AddThemeConstantOverride("separation", 0);
        hrow.AddChild(vbox);
        hrow.AddChild(HSpacer());

        for (int i = 0; i < 3; i++) outer.AddChild(VSpacer());

        // Title
        var title = new Label { Text = "TRAINING", HorizontalAlignment = HorizontalAlignment.Center };
        if (_font != null) title.AddThemeFontOverride("font", _font);
        title.AddThemeFontSizeOverride("font_size", 96);
        title.AddThemeColorOverride("font_color", Colors.White);
        vbox.AddChild(title);

        vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 40) });

        // ── Dummy count row ───────────────────────────────────────────────────
        var countRow = new HBoxContainer();
        countRow.AddThemeConstantOverride("separation", 0);
        countRow.CustomMinimumSize = new Vector2(0, BtnH);
        vbox.AddChild(countRow);

        var countLabelLeft = new Label
        {
            Text                = "Dummies",
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment   = VerticalAlignment.Center,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        if (_font != null) countLabelLeft.AddThemeFontOverride("font", _font);
        countLabelLeft.AddThemeFontSizeOverride("font_size", 26);
        countLabelLeft.AddThemeColorOverride("font_color", Colors.White);
        countRow.AddChild(countLabelLeft);

        countRow.AddChild(new Control { CustomMinimumSize = new Vector2(20, 0) });

        var minusBtn = MakeBtn("−", _font, 30, () => SetCount(_dummyCount - 1));
        minusBtn.CustomMinimumSize = new Vector2(BtnH, BtnH);
        minusBtn.Pressed += () => _clickSfx?.Play();
        countRow.AddChild(minusBtn);

        _countLabel = new Label
        {
            Text                = $"{_dummyCount}",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
            CustomMinimumSize   = new Vector2(64, BtnH),
        };
        if (_font != null) _countLabel.AddThemeFontOverride("font", _font);
        _countLabel.AddThemeFontSizeOverride("font_size", 30);
        _countLabel.AddThemeColorOverride("font_color", Colors.White);
        countRow.AddChild(_countLabel);

        var plusBtn = MakeBtn("+", _font, 30, () => SetCount(_dummyCount + 1));
        plusBtn.CustomMinimumSize = new Vector2(BtnH, BtnH);
        plusBtn.Pressed += () => _clickSfx?.Play();
        countRow.AddChild(plusBtn);

        vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 32) });

        // ── Bottom-left: Start Training ───────────────────────────────────────
        var startBtn = MakeBtn("Start Training", _font, 26, OnStart);
        startBtn.CustomMinimumSize = new Vector2(220, BtnH);
        startBtn.AnchorLeft     = 0f;  startBtn.AnchorRight  = 0f;
        startBtn.AnchorTop      = 1f;  startBtn.AnchorBottom = 1f;
        startBtn.GrowHorizontal = GrowDirection.End;
        startBtn.GrowVertical   = GrowDirection.Begin;
        startBtn.OffsetLeft     =  CornerPad;
        startBtn.OffsetRight    =  CornerPad + 220f;
        startBtn.OffsetTop      = -(CornerPad + BtnH);
        startBtn.OffsetBottom   = -CornerPad;
        AddChild(startBtn);

        // ── Bottom-right: Back ────────────────────────────────────────────────
        var backBtn = MakeBtn("Back", _font, 24, OnBack);
        backBtn.Pressed += () => _clickSfx?.Play();
        backBtn.CustomMinimumSize = new Vector2(160, BtnH);
        backBtn.AnchorLeft     = 1f;  backBtn.AnchorRight  = 1f;
        backBtn.AnchorTop      = 1f;  backBtn.AnchorBottom = 1f;
        backBtn.GrowHorizontal = GrowDirection.Begin;
        backBtn.GrowVertical   = GrowDirection.Begin;
        backBtn.OffsetLeft     = -(CornerPad + 160f);
        backBtn.OffsetRight    = -CornerPad;
        backBtn.OffsetTop      = -(CornerPad + BtnH);
        backBtn.OffsetBottom   = -CornerPad;
        backBtn.AddThemeColorOverride("font_color",       new Color(0.45f, 0.45f, 0.45f));
        backBtn.AddThemeColorOverride("font_hover_color", new Color(0.75f, 0.75f, 0.75f));
        AddChild(backBtn);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    void SetCount(int v)
    {
        _dummyCount      = Mathf.Clamp(v, 1, 10);
        _countLabel.Text = $"{_dummyCount}";
    }

    void OnStart()
    {
        TrainingConfig.DummyCount = _dummyCount;
        _playGameSfx?.Play();
        GetTree().CreateTimer(0.5).Timeout += () =>
            GetTree().ChangeSceneToFile("res://scenes/TrainingArena.tscn");
    }

    void OnBack() =>
        GetTree().ChangeSceneToFile("res://scenes/TitleScreen.tscn");

    static Control VSpacer() =>
        new Control { SizeFlagsVertical = SizeFlags.ExpandFill };

    static Control HSpacer() =>
        new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };

    static Button MakeBtn(string label, FontFile? font, int fontSize, Action pressed)
    {
        var btn = new Button
        {
            Text              = label,
            Flat              = true,
            Alignment         = HorizontalAlignment.Center,
            CustomMinimumSize = new Vector2(0, 52f),
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
}
