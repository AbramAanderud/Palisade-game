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

    bool _changingScene = false;
    bool _connectedOnce = false;

    Timer? _connectWaitTimer;        // shows "Waking server…" after 5 s
    Timer? _connectFailTimer;        // shows Retry/Back after 35 s
    Timer? _outgoingChallengeTimer;  // 30 s timeout on outgoing challenge
    Timer? _statusClearTimer;        // clears status label after 5 s

    Button? _retryBtn;
    Button? _backToMenuBtn;

    const float BtnH       = 48f;
    const int   MaxNameLen = 24;

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
        bool hasSavedName = !string.IsNullOrEmpty(OnlineGameState.PlayerName);
        if (!hasSavedName)
            OnlineGameState.PlayerName = "Player_" + (int)GD.RandRange(100, 999);

        SetAnchorsPreset(LayoutPreset.FullRect);
        BuildUI();

        _nm.LobbyJoined        += OnLobbyJoined;
        _nm.LobbyUpdated       += OnLobbyUpdated;
        _nm.IncomingChallenge  += OnIncomingChallenge;
        _nm.ChallengeDeclined  += OnChallengeDeclined;
        _nm.ChallengeCancelled += OnChallengeCancelled;
        _nm.MatchMade          += OnMatchMade;
        _nm.ConnectionFailed   += OnConnectionFailed;

        if (hasSavedName)
            BeginConnect();
        else
            PromptForName();
    }

    public override void _ExitTree()
    {
        _nm.LobbyJoined        -= OnLobbyJoined;
        _nm.LobbyUpdated       -= OnLobbyUpdated;
        _nm.IncomingChallenge  -= OnIncomingChallenge;
        _nm.ChallengeDeclined  -= OnChallengeDeclined;
        _nm.ChallengeCancelled -= OnChallengeCancelled;
        _nm.MatchMade          -= OnMatchMade;
        _nm.ConnectionFailed   -= OnConnectionFailed;
        PlayerProfile.Save();
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
            Text                = $"Fame: {OnlineGameState.Gold}",
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

        // ── Retry / Back-to-menu buttons (hidden until connection fails) ───────
        var failRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        failRow.AddThemeConstantOverride("separation", 16);
        outer.AddChild(failRow);

        _retryBtn = new Button { Text = "Retry", CustomMinimumSize = new Vector2(140, 40), Visible = false };
        if (_font != null) _retryBtn.AddThemeFontOverride("font", _font);
        _retryBtn.AddThemeFontSizeOverride("font_size", 16);
        _retryBtn.Pressed += OnRetryConnect;
        failRow.AddChild(_retryBtn);

        _backToMenuBtn = new Button { Text = "Back to menu", CustomMinimumSize = new Vector2(160, 40), Visible = false };
        if (_font != null) _backToMenuBtn.AddThemeFontOverride("font", _font);
        _backToMenuBtn.AddThemeFontSizeOverride("font_size", 16);
        _backToMenuBtn.Pressed += OnCancel;
        failRow.AddChild(_backToMenuBtn);

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

    // ── Status / connection state machine ─────────────────────────────────────

    void SetStatus(string text, bool autoClear = true)
    {
        _statusLabel.Text = text;
        _statusClearTimer?.Stop();
        if (!autoClear) return;
        _statusClearTimer ??= NewOneShotTimer(5.0, () => { if (_statusLabel != null) _statusLabel.Text = ""; });
        _statusClearTimer.Start(5.0);
    }

    Timer NewOneShotTimer(double seconds, Action onTimeout)
    {
        var t = new Timer { WaitTime = seconds, OneShot = true };
        t.Timeout += onTimeout;
        AddChild(t);
        return t;
    }

    void BeginConnect()
    {
        _connectedOnce = false;
        SetStatus("Connecting…", autoClear: false);
        GD.Print($"[LOBBY] Connecting as '{OnlineGameState.PlayerName}'");
        _nm.ConnectAndJoinLobby(OnlineGameState.PlayerName);

        _connectWaitTimer?.Stop();
        _connectFailTimer?.Stop();
        _connectWaitTimer ??= NewOneShotTimer(5.0,  OnConnectWaitElapsed);
        _connectFailTimer ??= NewOneShotTimer(35.0, OnConnectFailElapsed);
        _connectWaitTimer.Start(5.0);
        _connectFailTimer.Start(35.0);
    }

    void OnConnectWaitElapsed()
    {
        if (_connectedOnce) return;
        SetStatus("Waking server — this can take up to 30 s…", autoClear: false);
    }

    void OnConnectFailElapsed()
    {
        if (_connectedOnce) return;
        ShowConnectionFailureUI("Could not reach server.");
    }

    void ShowConnectionFailureUI(string reason)
    {
        SetStatus(reason, autoClear: false);
        if (_retryBtn != null) { _retryBtn.Visible = true; }
        if (_backToMenuBtn != null) { _backToMenuBtn.Visible = true; }
    }

    void HideConnectionFailureUI()
    {
        if (_retryBtn != null) _retryBtn.Visible = false;
        if (_backToMenuBtn != null) _backToMenuBtn.Visible = false;
    }

    void OnRetryConnect()
    {
        if (_changingScene) return;
        _clickSfx?.Play();
        HideConnectionFailureUI();
        _nm.Disconnect();
        BeginConnect();
    }

    // ── Name prompt ───────────────────────────────────────────────────────────

    void PromptForName()
    {
        _dialogLayer = MakeDialogLayer();
        var vbox = MakeDialogVBox(_dialogLayer);

        var lbl = new Label { Text = "Choose your name" };
        StyleDialogLabel(lbl, 24);
        vbox.AddChild(lbl);

        var edit = new LineEdit
        {
            Text              = OnlineGameState.PlayerName,
            MaxLength         = MaxNameLen,
            CustomMinimumSize = new Vector2(280, 44),
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
        };
        if (_font != null) edit.AddThemeFontOverride("font", _font);
        edit.AddThemeFontSizeOverride("font_size", 18);
        vbox.AddChild(edit);

        var okBtn = new Button { Text = "Continue", CustomMinimumSize = new Vector2(160, 44) };
        StyleDialogBtn(okBtn, 18);
        okBtn.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        Action submit = () =>
        {
            string raw = edit.Text ?? "";
            string clean = SanitizeName(raw);
            OnlineGameState.PlayerName = clean;
            PlayerProfile.Save();
            _clickSfx?.Play();
            _dialogLayer?.QueueFree();
            _dialogLayer = null;
            BeginConnect();
        };
        okBtn.Pressed += submit;
        edit.TextSubmitted += _ => submit();
        vbox.AddChild(okBtn);

        edit.GrabFocus();
    }

    static string SanitizeName(string raw)
    {
        string s = (raw ?? "").Trim();
        var sb = new System.Text.StringBuilder();
        foreach (char c in s)
        {
            if (c >= 32 && c != 127) sb.Append(c);
            if (sb.Length >= MaxNameLen) break;
        }
        s = sb.ToString().Trim();
        if (string.IsNullOrEmpty(s)) s = "Player_" + (int)GD.RandRange(100, 999);
        return s;
    }

    // ── Network callbacks ──────────────────────────────────────────────────────

    void OnLobbyJoined(string myId)
    {
        _connectedOnce = true;
        _connectWaitTimer?.Stop();
        _connectFailTimer?.Stop();
        HideConnectionFailureUI();
        GD.Print($"[LOBBY] Joined lobby as id {myId}");

        // Refresh dropdown + gold every time we (re-)enter the lobby so a maze
        // saved during this session, or gold earned in a prior match, appears.
        PopulateMazeDropdown();
        _goldLabel.Text = $"Fame: {OnlineGameState.Gold}";

        SetStatus("Connected — waiting for opponents…", autoClear: false);
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

            // Persistent — don't auto-clear since this updates with each lobby_update.
            SetStatus(count == 1 ? "1 player online" : $"{count} players online", autoClear: false);
        }
        catch { SetStatus("Error loading player list"); }
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
        if (_incomingChallengerId != null)
        {
            SetStatus("Respond to the incoming challenge first.");
            return;
        }
        if (_mazeDropdown.Disabled || OnlineGameState.SelectedMazeSlot < 0)
        {
            SetStatus("Build and select a maze first!");
            return;
        }
        _outgoingTargetId = targetId;
        _nm.SendChallenge(targetId);
        GD.Print($"[LOBBY] Sent challenge to {targetName} ({targetId})");
        ShowOutgoingDialog(targetName);

        _outgoingChallengeTimer?.Stop();
        _outgoingChallengeTimer ??= NewOneShotTimer(30.0, OnOutgoingChallengeTimeout);
        _outgoingChallengeTimer.Start(30.0);
    }

    void OnOutgoingChallengeTimeout()
    {
        if (_outgoingTargetId == null) return;
        GD.Print("[LOBBY] Outgoing challenge timed out.");
        CancelOutgoingChallenge();
        SetStatus("Opponent didn't respond.");
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
        _outgoingChallengeTimer?.Stop();
        _dialogLayer?.QueueFree();
        _dialogLayer = null;
    }

    void OnIncomingChallenge(string fromId, string fromName)
    {
        // Re-entry guard: if a dialog is already up (incoming or outgoing), ignore.
        if (_incomingChallengerId != null || _outgoingTargetId != null)
        {
            GD.Print($"[LOBBY] Ignoring incoming challenge from {fromName} — already busy.");
            _nm.RespondChallenge(fromId, false); // auto-decline so they don't hang
            return;
        }
        _incomingChallengerId = fromId;
        _dialogLayer?.QueueFree();
        _dialogLayer = MakeDialogLayer();
        var vbox = MakeDialogVBox(_dialogLayer);
        GD.Print($"[LOBBY] Incoming challenge from {fromName} ({fromId})");

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
        _outgoingChallengeTimer?.Stop();
        _dialogLayer?.QueueFree();
        _dialogLayer = null;
        SetStatus("Challenge declined.");
    }

    void OnChallengeCancelled(string fromId)
    {
        // The challenger left the lobby — dismiss any incoming dialog from them.
        if (_incomingChallengerId == fromId)
        {
            _incomingChallengerId = null;
            _dialogLayer?.QueueFree();
            _dialogLayer = null;
            SetStatus("Challenger disconnected.");
        }
    }

    void OnMatchMade(bool isHost)
    {
        if (_changingScene) return;
        _changingScene = true;
        _outgoingChallengeTimer?.Stop();
        _dialogLayer?.QueueFree();
        _clickSfx?.Play();
        GD.Print($"[LOBBY] MatchMade isHost={isHost} — entering PreGameLobby");
        GetTree().ChangeSceneToFile("res://scenes/PreGameLobby.tscn");
    }

    void OnConnectionFailed(string reason)
    {
        GD.Print($"[LOBBY] ConnectionFailed: {reason}");
        if (!_connectedOnce)
            ShowConnectionFailureUI($"Could not reach server ({reason}).");
        else
            SetStatus($"Disconnected: {reason}", autoClear: false);
    }

    void OnCancel()
    {
        if (_changingScene) return;
        _changingScene = true;
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
