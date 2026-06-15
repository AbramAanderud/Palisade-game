using Godot;
using System.Text.Json;

/// Pre-game lobby: both matched players see each other, pick their maze, then click Ready.
/// Host sends "start" once both are ready; client transitions when it receives that message.
public partial class PreGameLobby : Control
{
    NetworkManager     _nm           = null!;
    FontFile?          _font;
    AudioStreamPlayer? _clickSfx;
    AudioStreamPlayer? _playGameSfx;

    Label        _remoteNameLabel   = null!;
    Label        _remoteMazeLabel   = null!;
    Label        _remoteStatusLabel = null!;
    OptionButton _mazeDropdown      = null!;
    Button       _readyBtn          = null!;
    Label        _statusLabel       = null!;

    bool _localReady   = false;
    bool _remoteReady  = false;
    bool _gameStarting = false;
    bool _returning    = false;

    Timer? _readyTimeoutTimer;
    const double ReadyTimeoutSeconds = 60.0;

    const float BtnH = 52f;

    public override void _Ready()
    {
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _nm   = GetNode<NetworkManager>("/root/NetworkManager");
        _font = GD.Load<FontFile>("res://assets/fonts/Agmena Pro Book.ttf");

        _clickSfx = new AudioStreamPlayer();
        var clickSnd = GD.Load<AudioStream>("res://assets/audio/ui/MenuButtonClick.wav");
        if (clickSnd != null) _clickSfx.Stream = clickSnd;
        AddChild(_clickSfx);

        _playGameSfx = new AudioStreamPlayer();
        var playSnd = GD.Load<AudioStream>("res://assets/audio/ui/PlayGameButtonNoise.wav");
        if (playSnd != null) _playGameSfx.Stream = playSnd;
        AddChild(_playGameSfx);

        SetAnchorsPreset(LayoutPreset.FullRect);
        BuildUI();

        _nm.GameDataReceived += OnGameData;
        _nm.PeerDisconnected += OnDisconnected;

        // Exchange identity and initial maze selection with the opponent
        string safeName = JsonSerializer.Serialize(OnlineGameState.PlayerName);
        _nm.SendGameData($"{{\"t\":\"intro\",\"name\":{safeName}}}");
        if (OnlineGameState.SelectedMazeSlot >= 0)
            _nm.SendGameData($"{{\"t\":\"maze\",\"slot\":{OnlineGameState.SelectedMazeSlot}}}");

        // 60 s timeout — if both players don't ready up by then, bail back to lobby.
        _readyTimeoutTimer = new Timer { WaitTime = ReadyTimeoutSeconds, OneShot = true };
        _readyTimeoutTimer.Timeout += OnReadyTimeout;
        AddChild(_readyTimeoutTimer);
        _readyTimeoutTimer.Start();
    }

    public override void _ExitTree()
    {
        _nm.GameDataReceived -= OnGameData;
        _nm.PeerDisconnected -= OnDisconnected;
    }

    // ── UI ────────────────────────────────────────────────────────────────────

    void BuildUI()
    {
        var bg = new ColorRect { Color = new Color(0.04f, 0.04f, 0.06f) };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        var outer = new VBoxContainer();
        outer.SetAnchorsPreset(LayoutPreset.FullRect);
        outer.OffsetLeft = 60; outer.OffsetRight  = -60;
        outer.OffsetTop  = 30; outer.OffsetBottom = -30;
        outer.AddThemeConstantOverride("separation", 20);
        AddChild(outer);

        // Title
        var title = new Label { Text = "LOBBY", HorizontalAlignment = HorizontalAlignment.Center };
        if (_font != null) title.AddThemeFontOverride("font", _font);
        title.AddThemeFontSizeOverride("font_size", 52);
        title.AddThemeColorOverride("font_color", Colors.White);
        outer.AddChild(title);

        // ── Two-column player panels ──────────────────────────────────────────
        var panelRow = new HBoxContainer();
        panelRow.AddThemeConstantOverride("separation", 32);
        panelRow.SizeFlagsVertical = SizeFlags.ExpandFill;
        outer.AddChild(panelRow);

        // Local player panel
        var localPanel = new VBoxContainer();
        localPanel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        localPanel.AddThemeConstantOverride("separation", 14);
        panelRow.AddChild(localPanel);

        AddSectionLabel(localPanel, "YOU", new Color(0.4f, 0.8f, 1f));

        var localName = new Label { Text = OnlineGameState.PlayerName };
        StyleNameLabel(localName);
        localPanel.AddChild(localName);

        // Maze selector
        var mazeRow = new HBoxContainer();
        mazeRow.AddThemeConstantOverride("separation", 8);
        localPanel.AddChild(mazeRow);

        var mazeHint = new Label { Text = "Maze:", SizeFlagsVertical = SizeFlags.ShrinkCenter };
        if (_font != null) mazeHint.AddThemeFontOverride("font", _font);
        mazeHint.AddThemeFontSizeOverride("font_size", 15);
        mazeHint.AddThemeColorOverride("font_color", new Color(0.65f, 0.65f, 0.65f));
        mazeRow.AddChild(mazeHint);

        _mazeDropdown = new OptionButton { CustomMinimumSize = new Vector2(0, 42) };
        _mazeDropdown.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        if (_font != null) _mazeDropdown.AddThemeFontOverride("font", _font);
        _mazeDropdown.AddThemeFontSizeOverride("font_size", 15);
        PopulateMazeDropdown();
        _mazeDropdown.ItemSelected += idx => OnLocalMazeSelected((int)idx);
        mazeRow.AddChild(_mazeDropdown);

        localPanel.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });

        _readyBtn = new Button
        {
            Text              = "READY  ▶",
            CustomMinimumSize = new Vector2(0, BtnH),
            Disabled          = _mazeDropdown.Disabled,
        };
        if (_font != null) _readyBtn.AddThemeFontOverride("font", _font);
        _readyBtn.AddThemeFontSizeOverride("font_size", 24);
        _readyBtn.AddThemeColorOverride("font_color", new Color(0.3f, 1f, 0.5f));
        _readyBtn.Pressed += OnLocalReady;
        localPanel.AddChild(_readyBtn);

        // Remote player panel
        var remotePanel = new VBoxContainer();
        remotePanel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        remotePanel.AddThemeConstantOverride("separation", 14);
        panelRow.AddChild(remotePanel);

        AddSectionLabel(remotePanel, "OPPONENT", new Color(1f, 0.5f, 0.5f));

        _remoteNameLabel = new Label { Text = "Connecting…" };
        StyleNameLabel(_remoteNameLabel);
        remotePanel.AddChild(_remoteNameLabel);

        var remoteMazeHint = new Label { Text = "Maze:" };
        if (_font != null) remoteMazeHint.AddThemeFontOverride("font", _font);
        remoteMazeHint.AddThemeFontSizeOverride("font_size", 15);
        remoteMazeHint.AddThemeColorOverride("font_color", new Color(0.65f, 0.65f, 0.65f));
        remotePanel.AddChild(remoteMazeHint);

        _remoteMazeLabel = new Label { Text = "Selecting…" };
        if (_font != null) _remoteMazeLabel.AddThemeFontOverride("font", _font);
        _remoteMazeLabel.AddThemeFontSizeOverride("font_size", 18);
        _remoteMazeLabel.AddThemeColorOverride("font_color", Colors.White);
        remotePanel.AddChild(_remoteMazeLabel);

        remotePanel.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });

        _remoteStatusLabel = new Label { Text = "Not ready" };
        if (_font != null) _remoteStatusLabel.AddThemeFontOverride("font", _font);
        _remoteStatusLabel.AddThemeFontSizeOverride("font_size", 24);
        _remoteStatusLabel.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.5f));
        remotePanel.AddChild(_remoteStatusLabel);

        // Status line
        _statusLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        if (_font != null) _statusLabel.AddThemeFontOverride("font", _font);
        _statusLabel.AddThemeFontSizeOverride("font_size", 16);
        _statusLabel.AddThemeColorOverride("font_color", new Color(0.75f, 0.75f, 0.5f));
        outer.AddChild(_statusLabel);

        // Leave button
        var leaveBtn = new Button { Text = "← Leave", CustomMinimumSize = new Vector2(120, 40) };
        if (_font != null) leaveBtn.AddThemeFontOverride("font", _font);
        leaveBtn.AddThemeFontSizeOverride("font_size", 16);
        leaveBtn.Pressed += OnLeave;
        outer.AddChild(leaveBtn);
    }

    void PopulateMazeDropdown()
    {
        _mazeDropdown.Clear();
        for (int i = 0; i < MazeSerializer.SlotCount; i++)
        {
            if (!MazeSerializer.Exists(i)) continue;
            var data = MazeSerializer.Load(i);
            if (data == null || !data.IsOnline) continue;
            _mazeDropdown.AddItem($"Slot {i}: {data.Name ?? "Untitled"}", i);
        }

        if (_mazeDropdown.ItemCount == 0)
        {
            _mazeDropdown.AddItem("No mazes saved — build one first", -1);
            _mazeDropdown.Disabled = true;
            return;
        }

        for (int i = 0; i < _mazeDropdown.ItemCount; i++)
        {
            if (_mazeDropdown.GetItemId(i) == OnlineGameState.SelectedMazeSlot)
            {
                _mazeDropdown.Select(i);
                return;
            }
        }
        _mazeDropdown.Select(0);
        OnlineGameState.SelectedMazeSlot = _mazeDropdown.GetItemId(0);
    }

    // ── Interactions ──────────────────────────────────────────────────────────

    void OnLocalMazeSelected(int idx)
    {
        _clickSfx?.Play();
        int slot = _mazeDropdown.GetItemId(idx);
        OnlineGameState.SelectedMazeSlot = slot;
        _readyBtn.Disabled = (slot < 0);
        if (slot >= 0)
            _nm.SendGameData($"{{\"t\":\"maze\",\"slot\":{slot}}}");
    }

    void OnLocalReady()
    {
        if (_localReady || _gameStarting) return;
        _localReady         = true;
        _playGameSfx?.Play();
        _readyBtn.Text      = "READY  ✓";
        _readyBtn.Disabled  = true;
        _mazeDropdown.Disabled = true;
        _nm.SendGameData("{\"t\":\"ready\"}");
        _statusLabel.Text   = "Waiting for opponent…";
        CheckBothReady();
    }

    void CheckBothReady()
    {
        if (!_localReady || !_remoteReady || _gameStarting) return;
        _gameStarting     = true;
        _readyTimeoutTimer?.Stop();
        _statusLabel.Text = "Starting…";
        if (OnlineGameState.IsHost)
            _nm.SendGameData("{\"t\":\"start\"}");
        GetTree().CreateTimer(0.6).Timeout += () =>
            GetTree().ChangeSceneToFile("res://scenes/OnlineDungeonArena.tscn");
    }

    void OnReadyTimeout()
    {
        if (_gameStarting || _returning) return;
        GD.Print("[LOBBY] Ready timeout — both players didn't ready in 60 s.");
        ReturnToFindMatch("Took too long to start. Returning to lobby…");
    }

    void ReturnToFindMatch(string message)
    {
        if (_returning) return;
        _returning = true;
        _readyTimeoutTimer?.Stop();
        _readyBtn.Disabled = true;
        _mazeDropdown.Disabled = true;
        _statusLabel.Text = message;
        GetTree().CreateTimer(2.0).Timeout += () =>
        {
            if (!IsInstanceValid(this)) return;
            _nm.Disconnect();
            GetTree().ChangeSceneToFile("res://scenes/PlayGameScreen.tscn");
        };
    }

    // ── Network ───────────────────────────────────────────────────────────────

    void OnGameData(string json)
    {
        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(json);
            root = doc.RootElement.Clone();
        }
        catch { return; }

        if (!root.TryGetProperty("t", out var tProp)) return;

        switch (tProp.GetString())
        {
            case "intro":
                string name = root.TryGetProperty("name", out var np) ? np.GetString() ?? "Opponent" : "Opponent";
                _remoteNameLabel.Text = name;
                // Send our intro back so they get our name too
                string safeName = JsonSerializer.Serialize(OnlineGameState.PlayerName);
                _nm.SendGameData($"{{\"t\":\"intro_ack\",\"name\":{safeName}}}");
                break;

            case "intro_ack":
                string ackName = root.TryGetProperty("name", out var an) ? an.GetString() ?? "Opponent" : "Opponent";
                _remoteNameLabel.Text = ackName;
                break;

            case "maze":
                if (root.TryGetProperty("slot", out var sp) && sp.TryGetInt32(out int slot))
                {
                    var data = MazeSerializer.Exists(slot) ? MazeSerializer.Load(slot) : null;
                    _remoteMazeLabel.Text = data?.Name != null ? $"{data.Name} (Slot {slot})" : $"Slot {slot}";
                }
                break;

            case "ready":
                _remoteReady = true;
                _remoteStatusLabel.Text = "READY  ✓";
                _remoteStatusLabel.AddThemeColorOverride("font_color", new Color(0.3f, 1f, 0.5f));
                if (!_localReady) _statusLabel.Text = "Opponent is ready!";
                CheckBothReady();
                break;

            case "start":
                if (!_gameStarting)
                {
                    _gameStarting = true;
                    GetTree().ChangeSceneToFile("res://scenes/OnlineDungeonArena.tscn");
                }
                break;
        }
    }

    void OnDisconnected()
    {
        ReturnToFindMatch("Opponent disconnected. Returning to lobby…");
    }

    void OnLeave()
    {
        _clickSfx?.Play();
        _nm.Disconnect();
        GetTree().ChangeSceneToFile("res://scenes/TitleScreen.tscn");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    void AddSectionLabel(VBoxContainer parent, string text, Color color)
    {
        var lbl = new Label { Text = text };
        if (_font != null) lbl.AddThemeFontOverride("font", _font);
        lbl.AddThemeFontSizeOverride("font_size", 13);
        lbl.AddThemeColorOverride("font_color", color);
        parent.AddChild(lbl);
    }

    void StyleNameLabel(Label lbl)
    {
        if (_font != null) lbl.AddThemeFontOverride("font", _font);
        lbl.AddThemeFontSizeOverride("font_size", 28);
        lbl.AddThemeColorOverride("font_color", Colors.White);
    }
}
