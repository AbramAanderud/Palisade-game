using System;
using System.Linq;
using System.Text.Json;
using Godot;

/// Online 2-player arena: mirrors DungeonArena geometry but sources the maze from
/// OnlineGameState and syncs player state over the relay via NetworkManager.
///
/// Host  — loads its selected maze slot, sends JSON to client, spawns at MazeA start.
/// Client — waits for the maze JSON message, then builds and spawns at MazeB start.
///
/// Both peers keep a puppet (IsRemotePuppet=true PlayerController) and lerp it toward
/// received network positions at 20 Hz.
///
/// Game-end conditions:
///   • A player dies          → killer gets 100 gold, loser gets 0.
///   • 40s post-sword timer   → sword-holder gets 0 extra; non-holder gets 100 (survived).
///   Both award 100 gold to whoever picked up the sword first.
public partial class OnlineDungeonArena : Node3D
{
    // ── Layout constants (mirrors DungeonArena) ───────────────────────────────
    const float CellSize  = DungeonBuilder.CellSize;
    const float MazeCells = 10f;
    const float MazeDepth = MazeCells * CellSize;   // 100 m

    static float Apothem      => ArenaBuilder.Apothem;
    static float ArenaCentreZ => MazeDepth + Apothem;
    static float MazeBOffset  => MazeDepth + 2f * Apothem;

    const double CombatTimerSeconds = 40.0;

    // ── Nodes ─────────────────────────────────────────────────────────────────
    NetworkManager    _nm           = null!;
    PlayerController? _localPlayer;
    PlayerController? _puppet;
    CanvasLayer?      _loadingCanvas;
    FontFile?         _font;

    // Cached layout offsets (computed in BuildArena)
    float _exitAX   = 50f;
    float _offsetBX = 0f;
    float _offsetAY = 0f;
    float _offsetBY = 0f;

    // Host stores maze payload so it can re-send on client's ready_for_maze request
    string? _pendingMazePayload;

    // ── Game-end state ────────────────────────────────────────────────────────
    bool   _gameEnded    = false;
    bool   _swordPickedUp = false;
    bool   _localHasSword = false;
    int    _matchGold    = 0;
    Timer? _combatTimer;
    Label? _combatTimerLabel;

    // ── Ready ─────────────────────────────────────────────────────────────────
    public override void _Ready()
    {
        _nm = GetNode<NetworkManager>("/root/NetworkManager");
        _nm.PeerDisconnected += OnPeerDisconnected;
        _font = GD.Load<FontFile>("res://assets/fonts/Agmena Pro Book.ttf");

        BuildEnvironment();

        if (OnlineGameState.IsHost)
            SetupHost();
        else
            SetupClient();

        // Position broadcast at 20 Hz
        var timer = new Timer { WaitTime = 0.05f, Autostart = true, Name = "BroadcastTimer" };
        timer.Timeout += BroadcastPosition;
        AddChild(timer);

        // ESC hint overlay
        var canvas = new CanvasLayer();
        AddChild(canvas);
        var hint = new Label
        {
            Text     = "ESC = release mouse   ESC again = title",
            Position = new Vector2(10, 10),
        };
        if (_font != null) hint.AddThemeFontOverride("font", _font);
        hint.AddThemeFontSizeOverride("font_size", 13);
        hint.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
        canvas.AddChild(hint);
    }

    public override void _ExitTree()
    {
        _nm.PeerDisconnected -= OnPeerDisconnected;
        _nm.GameDataReceived -= OnGameData;
    }

    public override void _Process(double delta)
    {
        if (_combatTimer != null && !_combatTimer.IsStopped() && _combatTimerLabel != null)
        {
            int secs = (int)Math.Ceiling(_combatTimer.TimeLeft);
            _combatTimerLabel.Text = $"⚔  {secs}s";
        }
    }

    public override void _Input(InputEvent ev)
    {
        if (ev.IsActionPressed("pause") && _localPlayer == null)
        {
            _nm.GameDataReceived -= OnGameData;
            _nm.Disconnect();
            GetTree().ChangeSceneToFile("res://scenes/TitleScreen.tscn");
        }
    }

    // ── Host ──────────────────────────────────────────────────────────────────

    void SetupHost()
    {
        int slot = OnlineGameState.SelectedMazeSlot;
        var data = MazeSerializer.Load(slot);

        if (data == null || data.Pieces.Count == 0)
        {
            GD.PushError("[OnlineDungeonArena] Host: missing maze data");
            _nm.Disconnect();
            GetTree().ChangeSceneToFile("res://scenes/TitleScreen.tscn");
            return;
        }

        var dataFlipped = DungeonArena.FlipMazeZ(data);
        BuildArena(data, dataFlipped);

        var startA = data.Pieces.FirstOrDefault(p => p.Type == PieceType.Start) ?? data.Pieces[0];
        float ax = startA.X * CellSize + CellSize * 0.5f;
        float az = startA.Y * CellSize + CellSize * 0.5f;
        float ay = startA.Floor * DungeonBuilder.FloorHeight + 1f + _offsetAY;
        float spawnYawA = DungeonArena.DirToYaw(PieceDB.GetOpenings(PieceType.Start, startA.Rotation));
        _localPlayer = PlayerController.Spawn(this, new Vector3(ax, ay, az), spawnYawA);

        var startB = dataFlipped.Pieces.FirstOrDefault(p => p.Type == PieceType.Start) ?? dataFlipped.Pieces[0];
        float bx = startB.X * CellSize + CellSize * 0.5f + _offsetBX;
        float bz = startB.Y * CellSize + CellSize * 0.5f + MazeBOffset;
        float by = startB.Floor * DungeonBuilder.FloorHeight + 1f + _offsetBY;
        _puppet = PlayerController.Spawn(this, new Vector3(bx, by, bz), 0f);
        _puppet.IsRemotePuppet = true;
        _puppet.EnableWorldBars();

        _localPlayer.MakeActive();
        WireHitRelay();

        _nm.GameDataReceived += OnGameData;

        string mazeJson = JsonSerializer.Serialize(data);
        string escaped  = JsonSerializer.Serialize(mazeJson);
        _pendingMazePayload = $"{{\"t\":\"maze\",\"json\":{escaped}}}";
        _nm.SendGameData(_pendingMazePayload);

        GD.Print($"[OnlineDungeonArena] Host ready — slot {slot}");
    }

    // ── Client ────────────────────────────────────────────────────────────────

    void SetupClient()
    {
        _loadingCanvas = new CanvasLayer { Name = "LoadingCanvas" };

        var bg = new ColorRect { Color = new Color(0.04f, 0.04f, 0.06f) };
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _loadingCanvas.AddChild(bg);

        var lbl = new Label
        {
            Text                = "Waiting for maze data…",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
        };
        lbl.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        if (_font != null) lbl.AddThemeFontOverride("font", _font);
        lbl.AddThemeFontSizeOverride("font_size", 28);
        lbl.AddThemeColorOverride("font_color", Colors.White);
        _loadingCanvas.AddChild(lbl);

        AddChild(_loadingCanvas);

        _nm.GameDataReceived += OnGameData;
        _nm.SendGameData("{\"t\":\"ready_for_maze\"}");

        GD.Print("[OnlineDungeonArena] Client waiting for maze…");
    }

    void FinishClientSetup(MazeData hostMaze)
    {
        _loadingCanvas?.QueueFree();
        _loadingCanvas = null;

        var dataFlipped = DungeonArena.FlipMazeZ(hostMaze);
        BuildArena(hostMaze, dataFlipped);

        var startB = dataFlipped.Pieces.FirstOrDefault(p => p.Type == PieceType.Start) ?? dataFlipped.Pieces[0];
        float bx = startB.X * CellSize + CellSize * 0.5f + _offsetBX;
        float bz = startB.Y * CellSize + CellSize * 0.5f + MazeBOffset;
        float by = startB.Floor * DungeonBuilder.FloorHeight + 1f + _offsetBY;
        float spawnYawB = DungeonArena.DirToYaw(PieceDB.GetOpenings(PieceType.Start, startB.Rotation));
        _localPlayer = PlayerController.Spawn(this, new Vector3(bx, by, bz), spawnYawB);

        var startA = hostMaze.Pieces.FirstOrDefault(p => p.Type == PieceType.Start) ?? hostMaze.Pieces[0];
        float ax = startA.X * CellSize + CellSize * 0.5f;
        float az = startA.Y * CellSize + CellSize * 0.5f;
        float ay = startA.Floor * DungeonBuilder.FloorHeight + 1f + _offsetAY;
        _puppet = PlayerController.Spawn(this, new Vector3(ax, ay, az), 0f);
        _puppet.IsRemotePuppet = true;
        _puppet.EnableWorldBars();

        _localPlayer.MakeActive();
        WireHitRelay();

        GD.Print("[OnlineDungeonArena] Client arena built and spawned.");
    }

    // ── Geometry ──────────────────────────────────────────────────────────────

    void BuildEnvironment()
    {
        var worldEnv = new WorldEnvironment();
        var env      = new Godot.Environment();
        env.BackgroundMode     = Godot.Environment.BGMode.Color;
        env.BackgroundColor    = new Color(0f, 0f, 0f);
        env.AmbientLightSource = Godot.Environment.AmbientSource.Color;
        env.AmbientLightColor  = new Color(0.10f, 0.07f, 0.04f);
        env.AmbientLightEnergy = 0.25f;
        env.TonemapMode        = Godot.Environment.ToneMapper.Filmic;
        env.TonemapExposure    = 1.3f;
        worldEnv.Environment   = env;
        AddChild(worldEnv);
    }

    void BuildArena(MazeData dataA, MazeData dataFlipped)
    {
        var exitA = dataA.Pieces.FirstOrDefault(p => p.Type == PieceType.Exit);
        var exitB = dataFlipped.Pieces.FirstOrDefault(p => p.Type == PieceType.Exit);

        _exitAX = exitA != null ? exitA.X * CellSize + CellSize * 0.5f : MazeDepth * 0.5f;
        float exitBX = exitB != null ? exitB.X * CellSize + CellSize * 0.5f : _exitAX;
        _offsetBX = _exitAX - exitBX;
        _offsetAY = exitA != null ? -exitA.Floor * DungeonBuilder.FloorHeight : 0f;
        _offsetBY = exitB != null ? -exitB.Floor * DungeonBuilder.FloorHeight : 0f;

        var builderA = new DungeonBuilder { Name = "MazeA" };
        AddChild(builderA);
        builderA.Build(dataA, new Vector3(0f, _offsetAY, 0f), Dir.S);

        var builderB = new DungeonBuilder { Name = "MazeB" };
        AddChild(builderB);
        builderB.Build(dataFlipped, new Vector3(_offsetBX, _offsetBY, MazeBOffset), Dir.N);

        var torchA = new TorchSignalSystem { Name = "TorchSignalA" };
        AddChild(torchA);
        torchA.Init(dataA, builderA);

        var torchB = new TorchSignalSystem { Name = "TorchSignalB" };
        AddChild(torchB);
        torchB.Init(dataFlipped, builderB);

        var arena = new ArenaBuilder { Name = "Arena" };
        AddChild(arena);
        arena.Build(new Vector3(_exitAX, 0f, ArenaCentreZ), openNorth: true, openSouth: true);

        var pickup = WeaponPickup.Spawn(this, new Vector3(_exitAX, 1.5f, ArenaCentreZ));
        pickup.PickedUp += OnLocalSwordPickup;
    }

    // ── Network ───────────────────────────────────────────────────────────────

    void WireHitRelay()
    {
        var puppetHealth = _puppet?.GetNodeOrNull<PlayerHealth>("PlayerHealth");
        if (puppetHealth != null)
            puppetHealth.DamageDealt += dmg =>
                _nm.SendGameData($"{{\"t\":\"hit\",\"dmg\":{dmg:F1}}}");

        _localPlayer?.AddSwingListener(idx =>
            _nm.SendGameData($"{{\"t\":\"swing\",\"idx\":{idx}}}"));

        var localHealth = _localPlayer?.GetNodeOrNull<PlayerHealth>("PlayerHealth");
        if (localHealth != null)
            localHealth.OnDied += OnLocalPlayerDied;
    }

    void BroadcastPosition()
    {
        if (_localPlayer == null || !_nm.IsOpen) return;
        var pos = _localPlayer.GlobalPosition;
        _nm.SendGameData(
            $"{{\"t\":\"pos\",\"x\":{pos.X:F3},\"y\":{pos.Y:F3},\"z\":{pos.Z:F3}," +
            $"\"yaw\":{_localPlayer.Yaw:F4},\"pitch\":{_localPlayer.Pitch:F4}," +
            $"\"stam\":{_localPlayer.PlayerStamina:F1}}}");
    }

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
            case "maze":
                if (!OnlineGameState.IsHost && _localPlayer == null)
                {
                    try
                    {
                        string mazeJson = root.GetProperty("json").GetString()!;
                        var data = JsonSerializer.Deserialize<MazeData>(mazeJson)!;
                        FinishClientSetup(data);
                    }
                    catch (Exception ex)
                    {
                        GD.PushError($"[OnlineDungeonArena] Client: bad maze JSON — {ex.Message}");
                    }
                }
                break;

            case "pos":
                if (_puppet != null)
                {
                    _puppet.SetPuppetTarget(
                        new Vector3(
                            root.GetProperty("x").GetSingle(),
                            root.GetProperty("y").GetSingle(),
                            root.GetProperty("z").GetSingle()),
                        root.GetProperty("yaw").GetSingle(),
                        root.GetProperty("pitch").GetSingle());
                    if (root.TryGetProperty("stam", out var sp))
                        _puppet.PuppetStamina = sp.GetSingle();
                }
                break;

            case "hit":
                _localPlayer?.GetNodeOrNull<PlayerHealth>("PlayerHealth")
                             ?.TakeDamage(root.GetProperty("dmg").GetSingle(), Vector3.Zero);
                break;

            case "ready_for_maze":
                if (OnlineGameState.IsHost && _pendingMazePayload != null)
                {
                    GD.Print("[OnlineDungeonArena] Host: client ready, re-sending maze.");
                    _nm.SendGameData(_pendingMazePayload);
                }
                break;

            case "sword_taken":
                if (!_swordPickedUp && !_gameEnded)
                {
                    _swordPickedUp = true;
                    _localHasSword = false;
                    StartCombatTimer(peerHasSword: true);
                }
                break;

            case "swing":
                // Animate puppet sword swing (visual only)
                break;

            case "die":
                if (!_gameEnded)
                {
                    _matchGold += 100; // I killed the opponent
                    EndMatch("Victory!");
                }
                break;
        }
    }

    void OnPeerDisconnected()
    {
        if (_gameEnded) return;
        _localPlayer?.ReleaseMouse();

        var canvas = new CanvasLayer();
        AddChild(canvas);
        var lbl = new Label
        {
            Text                = "Opponent disconnected. Returning to lobby…",
            AnchorLeft          = 0.5f, AnchorRight = 0.5f,
            AnchorTop           = 0.1f, AnchorBottom = 0.1f,
            GrowHorizontal      = Control.GrowDirection.Both,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        lbl.OffsetLeft = -300; lbl.OffsetRight = 300;
        if (_font != null) lbl.AddThemeFontOverride("font", _font);
        lbl.AddThemeFontSizeOverride("font_size", 20);
        lbl.AddThemeColorOverride("font_color", new Color(1f, 0.4f, 0.4f));
        canvas.AddChild(lbl);

        _gameEnded = true;
        _nm.Disconnect();

        var t = new Timer { WaitTime = 3.0, OneShot = true };
        t.Timeout += () =>
        {
            if (IsInstanceValid(this))
                GetTree().ChangeSceneToFile("res://scenes/PlayGameScreen.tscn");
        };
        AddChild(t);
        t.Start();
    }

    // ── Game-end logic ────────────────────────────────────────────────────────

    void OnLocalSwordPickup()
    {
        if (_swordPickedUp || _gameEnded) return;
        _swordPickedUp = true;
        _localHasSword = true;
        _matchGold    += 100; // sword pickup gold
        _nm.SendGameData("{\"t\":\"sword_taken\"}");
        StartCombatTimer(peerHasSword: false);
    }

    void StartCombatTimer(bool peerHasSword)
    {
        _combatTimer = new Timer { WaitTime = CombatTimerSeconds, OneShot = true };
        _combatTimer.Timeout += OnCombatTimerExpired;
        AddChild(_combatTimer);
        _combatTimer.Start();
        ShowCombatHud(peerHasSword);
    }

    void ShowCombatHud(bool peerHasSword)
    {
        var canvas = new CanvasLayer { Name = "CombatHud" };
        AddChild(canvas);

        // Countdown label centred at top
        _combatTimerLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            AnchorLeft          = 0.5f, AnchorRight  = 0.5f,
            AnchorTop           = 0f,
            GrowHorizontal      = Control.GrowDirection.Both,
        };
        _combatTimerLabel.OffsetLeft  = -80f;
        _combatTimerLabel.OffsetRight =  80f;
        _combatTimerLabel.OffsetTop   =  40f;
        if (_font != null) _combatTimerLabel.AddThemeFontOverride("font", _font);
        _combatTimerLabel.AddThemeFontSizeOverride("font_size", 34);
        _combatTimerLabel.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.2f));
        canvas.AddChild(_combatTimerLabel);

        // Brief sword notification
        string note = peerHasSword ? "OPPONENT TOOK THE SWORD!" : "SWORD TAKEN! FIGHT!";
        var notif = new Label
        {
            Text                = note,
            HorizontalAlignment = HorizontalAlignment.Center,
            AnchorLeft          = 0.5f, AnchorRight  = 0.5f,
            AnchorTop           = 0.10f,
            GrowHorizontal      = Control.GrowDirection.Both,
        };
        notif.OffsetLeft  = -260f;
        notif.OffsetRight =  260f;
        if (_font != null) notif.AddThemeFontOverride("font", _font);
        notif.AddThemeFontSizeOverride("font_size", 26);
        notif.AddThemeColorOverride("font_color",
            peerHasSword ? new Color(1f, 0.4f, 0.3f) : new Color(0.3f, 1f, 0.5f));
        canvas.AddChild(notif);

        GetTree().CreateTimer(3.0).Timeout += () =>
        {
            if (IsInstanceValid(notif)) notif.Visible = false;
        };
    }

    void OnLocalPlayerDied()
    {
        if (_gameEnded) return;
        _nm.SendGameData("{\"t\":\"die\"}");
        EndMatch("Defeated!");
    }

    void OnCombatTimerExpired()
    {
        if (_gameEnded) return;
        // Survival gold: the player who didn't have the sword survived the timer
        if (!_localHasSword) _matchGold += 100;
        EndMatch("Time's Up!");
    }

    void EndMatch(string result)
    {
        if (_gameEnded) return;
        _gameEnded = true;
        _combatTimer?.Stop();
        _localPlayer?.ReleaseMouse();
        _nm.GameDataReceived -= OnGameData;
        _nm.Disconnect();
        OnlineGameState.Gold += _matchGold;
        PlayerProfile.Save();
        ShowEndOverlay(result);
    }

    void ShowEndOverlay(string result)
    {
        var canvas = new CanvasLayer { Name = "EndOverlay" };
        AddChild(canvas);

        var bg = new ColorRect { Color = new Color(0f, 0f, 0f, 0.82f) };
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        canvas.AddChild(bg);

        var vbox = new VBoxContainer();
        vbox.AnchorLeft    = 0.5f; vbox.AnchorRight  = 0.5f;
        vbox.AnchorTop     = 0.5f; vbox.AnchorBottom = 0.5f;
        vbox.GrowHorizontal = Control.GrowDirection.Both;
        vbox.GrowVertical   = Control.GrowDirection.Both;
        vbox.OffsetLeft    = -240f; vbox.OffsetRight  = 240f;
        vbox.OffsetTop     = -140f; vbox.OffsetBottom = 140f;
        vbox.AddThemeConstantOverride("separation", 18);
        canvas.AddChild(vbox);

        Color resultColor = result switch
        {
            "Defeated!"  => new Color(1f, 0.3f, 0.3f),
            "Victory!"   => new Color(0.3f, 1f, 0.5f),
            _            => new Color(1f, 0.9f, 0.3f),
        };

        var resultLbl = new Label
        {
            Text                = result,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        if (_font != null) resultLbl.AddThemeFontOverride("font", _font);
        resultLbl.AddThemeFontSizeOverride("font_size", 64);
        resultLbl.AddThemeColorOverride("font_color", resultColor);
        vbox.AddChild(resultLbl);

        var goldLbl = new Label
        {
            Text                = $"+{_matchGold} gold",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        if (_font != null) goldLbl.AddThemeFontOverride("font", _font);
        goldLbl.AddThemeFontSizeOverride("font_size", 30);
        goldLbl.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.3f));
        vbox.AddChild(goldLbl);

        var totalLbl = new Label
        {
            Text                = $"Total: {OnlineGameState.Gold} gold",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        if (_font != null) totalLbl.AddThemeFontOverride("font", _font);
        totalLbl.AddThemeFontSizeOverride("font_size", 20);
        totalLbl.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.5f));
        vbox.AddChild(totalLbl);

        var lobbyBtn = new Button
        {
            Text                = "Return to Lobby",
            CustomMinimumSize   = new Vector2(220, 56),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
        };
        if (_font != null) lobbyBtn.AddThemeFontOverride("font", _font);
        lobbyBtn.AddThemeFontSizeOverride("font_size", 20);
        lobbyBtn.Pressed += () =>
        {
            if (IsInstanceValid(this))
                GetTree().ChangeSceneToFile("res://scenes/PlayGameScreen.tscn");
        };
        vbox.AddChild(lobbyBtn);

        // Auto-return after 8 seconds
        GetTree().CreateTimer(8.0).Timeout += () =>
        {
            if (IsInstanceValid(this))
                GetTree().ChangeSceneToFile("res://scenes/PlayGameScreen.tscn");
        };
    }
}
