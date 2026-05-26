using Godot;

/// Host-only screen: pick a saved maze slot to use for the online match.
/// Builds a 6-column grid of up to 30 slots. Empty slots are disabled.
public partial class MazeSelectScreen : Control
{
    const int TotalSlots = 30;
    const int Cols       = 6;

    NetworkManager     _nm          = null!;
    Label              _statusLabel = null!;
    Button             _confirmBtn  = null!;
    int                _selected    = -1;
    Button[]           _slotBtns    = null!;
    AudioStreamPlayer? _clickSfx;

    public override void _Ready()
    {
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _nm = GetNode<NetworkManager>("/root/NetworkManager");

        _clickSfx = new AudioStreamPlayer();
        var clickStream = GD.Load<AudioStream>("res://assets/audio/ui/MenuButtonClick.wav");
        if (clickStream != null) _clickSfx.Stream = clickStream;
        AddChild(_clickSfx);

        SetAnchorsPreset(LayoutPreset.FullRect);

        var font = GD.Load<FontFile>("res://assets/fonts/Agmena Pro Book.ttf");

        var bg = new ColorRect { Color = new Color(0.04f, 0.04f, 0.06f) };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        // Outer VBox
        var outer = new VBoxContainer();
        outer.SetAnchorsPreset(LayoutPreset.FullRect);
        outer.AddThemeConstantOverride("separation", 20);
        outer.OffsetLeft   = 60; outer.OffsetRight  = -60;
        outer.OffsetTop    = 40; outer.OffsetBottom = -40;
        AddChild(outer);

        // Top row: back arrow + title
        var topRow = new HBoxContainer();
        topRow.AddThemeConstantOverride("separation", 16);
        outer.AddChild(topRow);

        var backTop = new Button { Text = "← Back", CustomMinimumSize = new Vector2(110, 40) };
        if (font != null) backTop.AddThemeFontOverride("font", font);
        backTop.AddThemeFontSizeOverride("font_size", 16);
        backTop.Pressed += () => GetTree().ChangeSceneToFile("res://scenes/TitleScreen.tscn");
        backTop.Pressed += () => _clickSfx?.Play();
        topRow.AddChild(backTop);

        var heading = new Label { Text = "Maze", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        if (font != null) heading.AddThemeFontOverride("font", font);
        heading.AddThemeFontSizeOverride("font_size", 36);
        heading.AddThemeColorOverride("font_color", Colors.White);
        heading.HorizontalAlignment = HorizontalAlignment.Center;
        topRow.AddChild(heading);

        // Invisible spacer to balance the back button so the title stays centred
        var spacer = new Control { CustomMinimumSize = new Vector2(110, 0) };
        topRow.AddChild(spacer);

        // Slot grid
        var grid = new GridContainer { Columns = Cols };
        grid.AddThemeConstantOverride("h_separation", 10);
        grid.AddThemeConstantOverride("v_separation", 10);
        outer.AddChild(grid);

        _slotBtns = new Button[TotalSlots];
        for (int i = 0; i < TotalSlots; i++)
        {
            int slot = i;
            bool exists = MazeSerializer.Exists(slot);
            var btn = new Button
            {
                Text              = $"Slot {slot}",
                Disabled          = !exists,
                CustomMinimumSize = new Vector2(140, 52),
                ToggleMode        = true,
            };
            if (font != null) btn.AddThemeFontOverride("font", font);
            btn.AddThemeFontSizeOverride("font_size", 16);
            if (!exists)
                btn.AddThemeColorOverride("font_color", new Color(0.35f, 0.35f, 0.35f));
            btn.Pressed += () => OnSlotPressed(slot, btn);
            btn.Pressed += () => _clickSfx?.Play();
            grid.AddChild(btn);
            _slotBtns[i] = btn;
        }

        // Bottom row: status + buttons
        var bottomRow = new HBoxContainer();
        bottomRow.AddThemeConstantOverride("separation", 16);
        outer.AddChild(bottomRow);

        _statusLabel = new Label { Text = "" };
        if (font != null) _statusLabel.AddThemeFontOverride("font", font);
        _statusLabel.AddThemeFontSizeOverride("font_size", 16);
        _statusLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.5f));
        _statusLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        bottomRow.AddChild(_statusLabel);

        _confirmBtn = new Button
        {
            Text     = "Create Game →",
            Disabled = true,
            CustomMinimumSize = new Vector2(180, 44),
        };
        if (font != null) _confirmBtn.AddThemeFontOverride("font", font);
        _confirmBtn.AddThemeFontSizeOverride("font_size", 16);
        _confirmBtn.Pressed += OnConfirm;
        _confirmBtn.Pressed += () => _clickSfx?.Play();
        bottomRow.AddChild(_confirmBtn);
    }

    public override void _ExitTree()
    {
        _nm.RoomCreated       -= OnRoomCreated;
        _nm.ConnectionFailed  -= OnConnectionFailed;
    }

    void OnSlotPressed(int slot, Button btn)
    {
        // Deselect previously selected
        if (_selected >= 0 && _selected < _slotBtns.Length)
            _slotBtns[_selected].ButtonPressed = false;

        _selected = slot;
        btn.ButtonPressed = true;
        _confirmBtn.Disabled = false;
        _statusLabel.Text = $"Slot {slot} selected";
    }

    void OnConfirm()
    {
        if (_selected < 0) return;

        OnlineGameState.SelectedMazeSlot = _selected;
        _confirmBtn.Disabled = true;
        _statusLabel.Text = "Connecting to relay…";

        _nm.RoomCreated      += OnRoomCreated;
        _nm.ConnectionFailed += OnConnectionFailed;
        _nm.ConnectAndCreate();
    }

    void OnRoomCreated(string code)
    {
        OnlineGameState.RoomCode = code;
        GetTree().ChangeSceneToFile("res://scenes/LobbyScreen.tscn");
    }

    void OnConnectionFailed(string reason)
    {
        _statusLabel.Text    = $"Connection failed: {reason}";
        _confirmBtn.Disabled = false;
    }
}
