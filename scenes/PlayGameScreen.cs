using Godot;
using System;
using System.Text.Json;

/// Matchmaking lobby: shows players currently looking for a match.
/// Select your maze from the upward-opening dropdown at the bottom,
/// then click Challenge next to any player to invite them.
public partial class PlayGameScreen : Control
{
    NetworkManager     _nm           = null!;
    FontFile?          _font;
    AudioStreamPlayer? _clickSfx;
    AudioStreamPlayer? _playGameSfx;

    Label         _statusLabel  = null!;
    Label         _goldLabel    = null!;
    VBoxContainer _playerList   = null!;
    OptionButton  _mazeDropdown = null!;

    CanvasLayer?  _dialogLayer;
    string?       _outgoingTargetId;     // player we sent a challenge to
    string?       _incomingChallengerId; // player who challenged us

    const float BtnH = 48f;

    public override void _Ready()
    {
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _nm   = GetNode<NetworkManager>("/root/NetworkManager");
        _font = GD.Load<FontFile>("res://assets/fonts/Agmena Pro Book.ttf");

        _clickSfx = new AudioStreamPlayer();
        var snd = GD.Load<AudioStream>("res://assets/audio/ui/MenuButtonClick.wav");
        if (snd != null) _clickSfx.Stream = snd;
        AddChild(_clickSfx);

        _playGameSfx = new AudioStreamPlayer();
        var playSnd = GD.Load<AudioStream>("res://assets/audio/ui/PlayGameButtonNoise.wav");
        if (playSnd != null) _playGameSfx.Stream = playSnd;
        AddChild(_playGameSfx);

        PlayerProfile.Load();
        if (string.IsNullOrEmpty(OnlineGameState.PlayerName))
            OnlineGameState.PlayerName = "Player_" + (int)GD.RandRange(100, 999);

        SetAnchorsPreset(LayoutPreset.FullRect);
        BuildUI();

        _nm.LobbyJoined       += OnLobbyJoined;
        _nm.LobbyUpdated      += OnLobbyUpdated;
        _nm.IncomingChallenge += OnIncomingChallenge;
        _nm.ChallengeDeclined += OnChallengeDeclined;
        _nm.MatchMade         += OnMatchMade;
        _nm.ConnectionFailed  += OnConnectionFailed;

        _statusLabel.Text = "Connecting…";
        _nm.ConnectAndJoinLobby(OnlineGameState.PlayerName);
    }

    public override void _ExitTree()
    {
        _nm.LobbyJoined       -= OnLobbyJoined;
        _nm.LobbyUpdated      -= OnLobbyUpdated;
        _nm.IncomingChallenge -= OnIncomingChallenge;
        _nm.ChallengeDeclined -= OnChallengeDeclined;
        _nm.MatchMade         -= OnMatchMade;
        _nm.ConnectionFailed  -= OnConnectionFailed;
    }

    // ── UI construction ───────────────────────────────────────────────────────

    void BuildUI()
    {
        var bg = new ColorRect { Color = new Color(0.04f, 0.04f, 0.06f) };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        var outer = new VBoxContainer();
        outer.SetAnchorsPreset(LayoutPreset.FullRect);
        outer.OffsetLeft   = 60;  outer.OffsetRight  = -60;
        outer.OffsetTop    = 30;  outer.OffsetBottom = -30;
        outer.AddThemeConstantOverride("separation", 12);
        AddChild(outer);

        // ── Top row: back + title + gold ──────────────────────────────────────
        var topRow = new HBoxContainer();
        topRow.AddThemeConstantOverride("separation", 16);
        outer.AddChild(topRow);

        var backBtn = new Button { Text = "← Back", CustomMinimumSize = new Vector2(120, 42) };
        if (_font != null) backBtn.AddThemeFontOverride("font", _font);
        backBtn.AddThemeFontSizeOverride("font_size", 16);
        backBtn.Pressed += OnCancel;
        topRow.AddChild(backBtn);

        var titleLabel = new Label
        {
            Text                = "FIND MATCH",
            HorizontalAlignment = HorizontalAlignment.Center,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        if (_font != null) titleLabel.AddThemeFontOverride("font", _font);
        titleLabel.AddThemeFontSizeOverride("font_size", 36);
        titleLabel.AddThemeColorOverride("font_color", Colors.White);
        topRow.AddChild(titleLabel);

        _goldLabel = new Label
        {
            Text                = $"Gold: {OnlineGameState.Gold}",
            HorizontalAlignment = HorizontalAlignment.Right,
            CustomMinimumSize   = new Vector2(130, 0),
            SizeFlagsVertical   = SizeFlags.ShrinkCenter,
        };
        if (_font != null) _goldLabel.AddThemeFontOverride("font", _font);
        _goldLabel.AddThemeFontSizeOverride("font_size", 18);
        _goldLabel.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.3f));
        topRow.AddChild(_goldLabel);

        // ── Player name sub-heading ────────────────────────────────────────────
        var nameLabel = new Label
        {
            Text                = $"Playing as: {OnlineGameState.PlayerName}",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        if (_font != null) nameLabel.AddThemeFontOverride("font", _font);
        nameLabel.AddThemeFontSizeOverride("font_size", 16);
        nameLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
        outer.AddChild(nameLabel);

        // ── Status ────────────────────────────────────────────────────────────
        _statusLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        if (_font != null) _statusLabel.AddThemeFontOverride("font", _font);
        _statusLabel.AddThemeFontSizeOverride("font_size", 16);
        _statusLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.5f));
        outer.AddChild(_statusLabel);

        // ── Player list ───────────────────────────────────────────────────────
        var listHeader = new Label { Text = "Online Players:" };
        if (_font != null) listHeader.AddThemeFontOverride("font", _font);
        listHeader.AddThemeFontSizeOverride("font_size", 20);
        listHeader.AddThemeColorOverride("font_color", Colors.White);
        outer.AddChild(listHeader);

        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        outer.AddChild(scroll);

        _playerList = new VBoxContainer();
        _playerList.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _playerList.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(_playerList);

        // ── Bottom: maze selector (positioned at bottom so popup opens upward) ─
        var bottomRow = new HBoxContainer();
        bottomRow.AddThemeConstantOverride("separation", 12);
        outer.AddChild(bottomRow);

        var mazeLabel = new Label
        {
            Text              = "Your Maze:",
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        if (_font != null) mazeLabel.AddThemeFontOverride("font", _font);
        mazeLabel.AddThemeFontSizeOverride("font_size", 18);
        mazeLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.8f));
        bottomRow.AddChild(mazeLabel);

        _mazeDropdown = new OptionButton { CustomMinimumSize = new Vector2(280, BtnH) };
        if (_font != null) _mazeDropdown.AddThemeFontOverride("font", _font);
        _mazeDropdown.AddThemeFontSizeOverride("font_size", 16);
        PopulateMazeDropdown();
        _mazeDropdown.ItemSelected += idx =>
        {
            int slot = _mazeDropdown.GetItemId((int)idx);
            OnlineGameState.SelectedMazeSlot = slot;
            _clickSfx?.Play();
        };
        bottomRow.AddChild(_mazeDropdown);
    }

    void PopulateMazeDropdown()
    {
        _mazeDropdown.Clear();
        for (int i = 0; i < MazeSerializer.SlotCount; i++)
        {
            if (!MazeSerializer.Exists(i)) continue;
            var data = MazeSerializer.Load(i);
            if (data == null || !data.IsOnline) continue;
            if (!data.IsGameReady) continue;   // only game-ready mazes can be used online
            string name = data.Name ?? "Untitled";
            _mazeDropdown.AddItem($"Slot {i}: {name}", i);
        }

        if (_mazeDropdown.ItemCount == 0)
        {
            _mazeDropdown.AddItem("No game-ready mazes — finish one in the editor", -1);
            _mazeDropdown.Disabled = true;
            return;
        }

        // Re-select the previously chosen slot if still available
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

    // ── Network callbacks ──────────────────────────────────────────────────────

    void OnLobbyJoined(string myId)
    {
        _statusLabel.Text = "Connected — waiting for opponents…";
    }

    void OnLobbyUpdated(string raw)
    {
        foreach (var child in _playerList.GetChildren())
            child.QueueFree();

        try
        {
            using var doc    = JsonDocument.Parse(raw);
            var playersArr   = doc.RootElement.GetProperty("players");
            int count        = 0;

            foreach (var player in playersArr.EnumerateArray())
            {
                string id   = player.GetProperty("id").GetString()   ?? "";
                string name = player.GetProperty("name").GetString() ?? "Unknown";
                AddPlayerRow(id, name);
                count++;
            }

            if (count == 0)
            {
                var empty = new Label { Text = "No other players online yet. Hang tight…" };
                if (_font != null) empty.AddThemeFontOverride("font", _font);
                empty.AddThemeFontSizeOverride("font_size", 16);
                empty.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.5f));
                empty.HorizontalAlignment = HorizontalAlignment.Center;
                _playerList.AddChild(empty);
            }

            _statusLabel.Text = count == 1 ? "1 player online" : $"{count} players online";
        }
        catch { _statusLabel.Text = "Error loading player list"; }
    }

    void AddPlayerRow(string id, string name)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 16);

        var dot = new Label { Text = "●", SizeFlagsVertical = SizeFlags.ShrinkCenter };
        if (_font != null) dot.AddThemeFontOverride("font", _font);
        dot.AddThemeFontSizeOverride("font_size", 12);
        dot.AddThemeColorOverride("font_color", new Color(0.3f, 1f, 0.5f));
        row.AddChild(dot);

        var nameLabel = new Label
        {
            Text                = name,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical   = SizeFlags.ShrinkCenter,
        };
        if (_font != null) nameLabel.AddThemeFontOverride("font", _font);
        nameLabel.AddThemeFontSizeOverride("font_size", 20);
        nameLabel.AddThemeColorOverride("font_color", Colors.White);
        row.AddChild(nameLabel);

        var btn = new Button { Text = "Challenge", CustomMinimumSize = new Vector2(130, 40) };
        if (_font != null) btn.AddThemeFontOverride("font", _font);
        btn.AddThemeFontSizeOverride("font_size", 16);
        string capturedId   = id;
        string capturedName = name;
        btn.Pressed += () => { _clickSfx?.Play(); OnChallengeBtnPressed(capturedId, capturedName); };
        row.AddChild(btn);

        _playerList.AddChild(row);
    }

    void OnChallengeBtnPressed(string targetId, string targetName)
    {
        if (_outgoingTargetId != null) return;
        if (_mazeDropdown.Disabled || OnlineGameState.SelectedMazeSlot < 0)
        {
            _statusLabel.Text = "Build and select a maze first!";
            return;
        }
        _outgoingTargetId = targetId;
        _nm.SendChallenge(targetId);
        ShowOutgoingDialog(targetName);
    }

    // ── Challenge dialogs ──────────────────────────────────────────────────────

    void ShowOutgoingDialog(string targetName)
    {
        _dialogLayer = MakeDialogLayer();
        var vbox = MakeDialogVBox(_dialogLayer);

        var lbl = new Label { Text = $"Challenging\n{targetName}…\n\nWaiting for response…" };
        StyleDialogLabel(lbl, 22);
        vbox.AddChild(lbl);

        vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 16) });

        var cancelBtn = new Button { Text = "Cancel", CustomMinimumSize = new Vector2(140, 44) };
        StyleDialogBtn(cancelBtn, 16);
        cancelBtn.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        cancelBtn.Pressed += () => { _clickSfx?.Play(); CancelOutgoingChallenge(); };
        vbox.AddChild(cancelBtn);
    }

    void CancelOutgoingChallenge()
    {
        _outgoingTargetId = null;
        _dialogLayer?.QueueFree();
        _dialogLayer = null;
    }

    void OnIncomingChallenge(string fromId, string fromName)
    {
        _incomingChallengerId = fromId;
        _dialogLayer?.QueueFree();
        _dialogLayer = MakeDialogLayer();
        var vbox = MakeDialogVBox(_dialogLayer);

        var lbl = new Label { Text = $"{fromName}\nwants to play!" };
        StyleDialogLabel(lbl, 26);
        vbox.AddChild(lbl);

        vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 20) });

        var btnRow = new HBoxContainer();
        btnRow.AddThemeConstantOverride("separation", 24);
        btnRow.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        vbox.AddChild(btnRow);

        var acceptBtn = new Button { Text = "Accept", CustomMinimumSize = new Vector2(140, 52) };
        StyleDialogBtn(acceptBtn, 20);
        acceptBtn.AddThemeColorOverride("font_color", new Color(0.3f, 1f, 0.5f));
        acceptBtn.Pressed += () => { _clickSfx?.Play(); RespondToChallenge(true); };
        btnRow.AddChild(acceptBtn);

        var declineBtn = new Button { Text = "Decline", CustomMinimumSize = new Vector2(140, 52) };
        StyleDialogBtn(declineBtn, 20);
        declineBtn.AddThemeColorOverride("font_color", new Color(1f, 0.4f, 0.4f));
        declineBtn.Pressed += () => { _clickSfx?.Play(); RespondToChallenge(false); };
        btnRow.AddChild(declineBtn);
    }

    void RespondToChallenge(bool accepted)
    {
        if (_incomingChallengerId == null) return;
        _nm.RespondChallenge(_incomingChallengerId, accepted);
        _incomingChallengerId = null;
        _dialogLayer?.QueueFree();
        _dialogLayer = null;
        if (!accepted) _statusLabel.Text = "Challenge declined.";
    }

    void OnChallengeDeclined()
    {
        _outgoingTargetId = null;
        _dialogLayer?.QueueFree();
        _dialogLayer = null;
        _statusLabel.Text = "Challenge declined.";
    }

    void OnMatchMade(bool isHost)
    {
        _dialogLayer?.QueueFree();
        _clickSfx?.Play();
        GetTree().ChangeSceneToFile("res://scenes/PreGameLobby.tscn");
    }

    void OnConnectionFailed(string reason)
    {
        _statusLabel.Text = $"Connection failed: {reason}";
    }

    void OnCancel()
    {
        _clickSfx?.Play();
        _nm.Disconnect();
        GetTree().ChangeSceneToFile("res://scenes/TitleScreen.tscn");
    }

    // ── Dialog helpers ─────────────────────────────────────────────────────────

    CanvasLayer MakeDialogLayer()
    {
        var layer = new CanvasLayer();
        AddChild(layer);

        var backdrop = new ColorRect { Color = new Color(0f, 0f, 0f, 0.65f) };
        backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
        layer.AddChild(backdrop);

        return layer;
    }

    VBoxContainer MakeDialogVBox(CanvasLayer layer)
    {
        var panel = new PanelContainer();
        panel.AnchorLeft    = 0.5f;  panel.AnchorRight  = 0.5f;
        panel.AnchorTop     = 0.5f;  panel.AnchorBottom = 0.5f;
        panel.GrowHorizontal = GrowDirection.Both;
        panel.GrowVertical   = GrowDirection.Both;
        panel.OffsetLeft    = -220f; panel.OffsetRight  = 220f;
        panel.OffsetTop     = -130f; panel.OffsetBottom = 130f;
        layer.AddChild(panel);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 10);
        panel.AddChild(vbox);
        return vbox;
    }

    void StyleDialogLabel(Label lbl, int size)
    {
        if (_font != null) lbl.AddThemeFontOverride("font", _font);
        lbl.AddThemeFontSizeOverride("font_size", size);
        lbl.AddThemeColorOverride("font_color", Colors.White);
        lbl.HorizontalAlignment = HorizontalAlignment.Center;
        lbl.AutowrapMode        = TextServer.AutowrapMode.Word;
    }

    void StyleDialogBtn(Button btn, int size)
    {
        if (_font != null) btn.AddThemeFontOverride("font", _font);
        btn.AddThemeFontSizeOverride("font_size", size);
    }
}
