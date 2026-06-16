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

    // Maze exchange: both players send their own maze and wait for the opponent's.
    // Arena builds once both are in hand.
    MazeData? _ownMaze;
    MazeData? _opponentMaze;
    string?   _ownMazePayload;      // serialized own maze, kept for re-send on request
    bool      _arenaBuilt = false;

    // ── Game-end state ────────────────────────────────────────────────────────
    bool   _gameEnded      = false;
    bool   _localPlayerDead = false;
    bool   _swordPickedUp  = false;
    bool   _localHasSword  = false;
    int    _matchGold      = 0;
    Timer? _combatTimer;
    Label? _combatTimerLabel;

    // ── Client maze handshake ─────────────────────────────────────────────────
    Timer? _clientMazeRetryTimer;   // re-sends ready_for_maze every 2 s on client
    int    _clientMazeRetries = 0;  // gives up at 15 retries (~30 s)

    bool _softLightToastShown = false;

    CanvasLayer? _surrenderDialog;

    // ── Ready ─────────────────────────────────────────────────────────────────
    public override void _Ready()
    {
        _nm = GetNode<NetworkManager>("/root/NetworkManager");
        _nm.PeerDisconnected += OnPeerDisconnected;
        _nm.GameDataReceived += OnGameData;
        _font = GD.Load<FontFile>("res://assets/fonts/Agmena Pro Book.ttf");

        BuildEnvironment();
        SetupShared();

        // Position broadcast at 30 Hz (was 20 Hz). 33 ms tick; ~3 KB/s/player.
        var timer = new Timer { WaitTime = 0.033f, Autostart = true, Name = "BroadcastTimer" };
        timer.Timeout += BroadcastPosition;
        AddChild(timer);

        // ESC hint overlay
        var canvas = new CanvasLayer();
        AddChild(canvas);
        var hint = new Label
        {
            Text     = "ESC = surrender",
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
        // Safety net: never leave the cursor captured when the arena unloads.
        Input.MouseMode = Input.MouseModeEnum.Visible;
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
        if (!ev.IsActionPressed("pause")) return;

        // Loading screen — quick bail.
        if (_localPlayer == null)
        {
            _nm.GameDataReceived -= OnGameData;
            _nm.Disconnect();
            GetTree().ChangeSceneToFile("res://scenes/TitleScreen.tscn");
            GetViewport().SetInputAsHandled();
            return;
        }

        if (_gameEnded) return;

        // In-match: toggle the surrender dialog.
        if (_surrenderDialog != null)
            DismissSurrenderDialog();
        else
            ShowSurrenderDialog();
        GetViewport().SetInputAsHandled();
    }

    // ── Shared maze-exchange setup ────────────────────────────────────────────
    // Each player sends their OWN maze and waits for the opponent's. The arena
    // builds once both have arrived. Both screens then show identical geometry:
    //   • MazeA (north / low z) = host's design
    //   • MazeB (south / high z, flipped) = client's design
    // Host spawns in MazeB and navigates north toward the center.
    // Client spawns in MazeA and navigates south toward the center.

    void SetupShared()
    {
        int slot = OnlineGameState.SelectedMazeSlot;
        var data = MazeSerializer.Load(slot);

        if (data == null || data.Pieces.Count == 0)
        {
            GD.PushError("[ARENA] Missing own maze data — returning to lobby.");
            _nm.Disconnect();
            GetTree().ChangeSceneToFile("res://scenes/PlayGameScreen.tscn");
            return;
        }

        _ownMaze = data;

        // Serialize own maze and stash for retry; immediately send to opponent.
        string mazeJson = JsonSerializer.Serialize(data);
        string escaped  = JsonSerializer.Serialize(mazeJson);
        _ownMazePayload = $"{{\"t\":\"maze\",\"json\":{escaped}}}";
        _nm.SendGameData(_ownMazePayload);

        ShowLoadingScreen();

        // Retry: re-send our maze (in case it was dropped) and re-request opponent's.
        _clientMazeRetryTimer = new Timer { WaitTime = 2.0, Autostart = true, Name = "MazeRetry" };
        _clientMazeRetryTimer.Timeout += OnOpponentMazeRetry;
        AddChild(_clientMazeRetryTimer);

        GD.Print($"[ARENA] {(OnlineGameState.IsHost ? "Host" : "Client")} sent own maze (slot {slot}, " +
                 $"{_ownMazePayload.Length} chars). Waiting for opponent's maze…");
    }

    void ShowLoadingScreen()
    {
        _loadingCanvas = new CanvasLayer { Name = "LoadingCanvas" };

        var bg = new ColorRect { Color = new Color(0.04f, 0.04f, 0.06f) };
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _loadingCanvas.AddChild(bg);

        var lbl = new Label
        {
            Text                = "Exchanging mazes…",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
        };
        lbl.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        if (_font != null) lbl.AddThemeFontOverride("font", _font);
        lbl.AddThemeFontSizeOverride("font_size", 28);
        lbl.AddThemeColorOverride("font_color", Colors.White);
        _loadingCanvas.AddChild(lbl);

        AddChild(_loadingCanvas);
    }

    void OnOpponentMazeRetry()
    {
        if (_arenaBuilt) return;
        _clientMazeRetries++;
        if (_clientMazeRetries > 15)            // ~30 s
        {
            GD.PushError("[ARENA] Gave up waiting for opponent's maze after 30 s.");
            _clientMazeRetryTimer?.Stop();
            if (_loadingCanvas != null)
            {
                foreach (var child in _loadingCanvas.GetChildren())
                    if (child is Label l) l.Text = "Could not receive opponent's maze.\nReturning to lobby…";
            }
            GetTree().CreateTimer(3.0).Timeout += () =>
            {
                if (IsInstanceValid(this))
                {
                    _nm.Disconnect();
                    GetTree().ChangeSceneToFile("res://scenes/PlayGameScreen.tscn");
                }
            };
            return;
        }
        GD.Print($"[ARENA] Re-requesting opponent's maze (attempt {_clientMazeRetries})");
        // Resend our own maze too — opponent may have missed it on the first send.
        if (_ownMazePayload != null) _nm.SendGameData(_ownMazePayload);
        _nm.SendGameData("{\"t\":\"ready_for_maze\"}");
    }

    void TryBuildArenaAndSpawn()
    {
        if (_arenaBuilt) return;
        if (_ownMaze == null || _opponentMaze == null) return;
        _arenaBuilt = true;

        _clientMazeRetryTimer?.Stop();
        _loadingCanvas?.QueueFree();
        _loadingCanvas = null;

        // North side = host's design, south side = client's design (flipped to face center).
        var hostMaze   = OnlineGameState.IsHost ? _ownMaze     : _opponentMaze;
        var clientMaze = OnlineGameState.IsHost ? _opponentMaze : _ownMaze;
        var mazeBFlipped = DungeonArena.FlipMazeZ(clientMaze);

        BuildArena(hostMaze, mazeBFlipped);

        // Spawn positions for both mazes (computed here so we don't recompute later).
        var startA = hostMaze.Pieces.FirstOrDefault(p => p.Type == PieceType.Start) ?? hostMaze.Pieces[0];
        float ax = startA.X * CellSize + CellSize * 0.5f;
        float az = startA.Y * CellSize + CellSize * 0.5f;
        float ay = startA.Floor * DungeonBuilder.FloorHeight + 1f + _offsetAY;
        float yawA = DungeonArena.DirToYaw(PieceDB.GetOpenings(PieceType.Start, startA.Rotation));

        var startB = mazeBFlipped.Pieces.FirstOrDefault(p => p.Type == PieceType.Start) ?? mazeBFlipped.Pieces[0];
        float bx = startB.X * CellSize + CellSize * 0.5f + _offsetBX;
        float bz = startB.Y * CellSize + CellSize * 0.5f + MazeBOffset;
        float by = startB.Floor * DungeonBuilder.FloorHeight + 1f + _offsetBY;
        float yawB = DungeonArena.DirToYaw(PieceDB.GetOpenings(PieceType.Start, startB.Rotation));

        // Host plays in MazeB (client's maze flipped); client plays in MazeA (host's maze).
        if (OnlineGameState.IsHost)
        {
            _localPlayer = PlayerController.Spawn(this, new Vector3(bx, by, bz), yawB);
            _puppet      = PlayerController.Spawn(this, new Vector3(ax, ay, az), 0f);
        }
        else
        {
            _localPlayer = PlayerController.Spawn(this, new Vector3(ax, ay, az), yawA);
            _puppet      = PlayerController.Spawn(this, new Vector3(bx, by, bz), 0f);
        }
        _puppet.IsRemotePuppet = true;
        _puppet.EnableWorldBars();

        _localPlayer.MakeActive();
        WireHitRelay();
        ShowToast("Find the sword.", 4.0, new Color(1f, 0.9f, 0.6f));

        GD.Print($"[ARENA] Arena built. {(OnlineGameState.IsHost ? "Host" : "Client")} " +
                 "navigating opponent's maze.");
    }

    /// Center-screen toast: fades in 0.3 s, holds `hold` seconds, fades out 0.7 s.
    void ShowToast(string text, double hold = 4.0, Color? color = null)
    {
        var canvas = new CanvasLayer { Name = "Toast" };
        AddChild(canvas);

        var label = new Label
        {
            Text                = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
            AnchorLeft          = 0f,   AnchorRight  = 1f,
            AnchorTop           = 0.5f, AnchorBottom = 0.5f,
            GrowHorizontal      = Control.GrowDirection.Both,
            GrowVertical        = Control.GrowDirection.Both,
            Modulate            = new Color(1, 1, 1, 0),
        };
        label.OffsetTop    = -40;
        label.OffsetBottom =  40;
        if (_font != null) label.AddThemeFontOverride("font", _font);
        label.AddThemeFontSizeOverride("font_size", 46);
        label.AddThemeColorOverride("font_color",         color ?? Colors.White);
        label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.85f));
        label.AddThemeConstantOverride("outline_size", 6);
        canvas.AddChild(label);

        var tw = CreateTween();
        tw.TweenProperty(label, "modulate:a", 1f, 0.3);
        tw.TweenInterval(hold);
        tw.TweenProperty(label, "modulate:a", 0f, 0.7);
        tw.TweenCallback(Callable.From(() =>
        {
            if (IsInstanceValid(canvas)) canvas.QueueFree();
        }));
    }

    void OnSoftLightTriggered()
    {
        if (_softLightToastShown || _gameEnded) return;
        _softLightToastShown = true;
        ShowToast("The soft light guides you.", 4.0, new Color(0.55f, 0.75f, 1f));
        GD.Print("[ARENA] Soft light toast shown.");
    }

    // ── Surrender (ESC during match) ──────────────────────────────────────────

    void ShowSurrenderDialog()
    {
        if (_gameEnded || _surrenderDialog != null) return;

        // Freeze local player so they can't keep moving while deciding.
        if (_localPlayer != null && IsInstanceValid(_localPlayer))
        {
            _localPlayer.Frozen = true;
            _localPlayer.ReleaseMouse();
        }
        Input.MouseMode = Input.MouseModeEnum.Visible;

        _surrenderDialog = new CanvasLayer { Layer = 90 };
        AddChild(_surrenderDialog);

        var bg = new ColorRect
        {
            Color       = new Color(0f, 0f, 0f, 0.72f),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _surrenderDialog.AddChild(bg);

        var vbox = new VBoxContainer();
        vbox.AnchorLeft    = 0.5f; vbox.AnchorRight  = 0.5f;
        vbox.AnchorTop     = 0.5f; vbox.AnchorBottom = 0.5f;
        vbox.GrowHorizontal = Control.GrowDirection.Both;
        vbox.GrowVertical   = Control.GrowDirection.Both;
        vbox.OffsetLeft    = -240; vbox.OffsetRight  = 240;
        vbox.OffsetTop     = -120; vbox.OffsetBottom = 120;
        vbox.AddThemeConstantOverride("separation", 16);
        _surrenderDialog.AddChild(vbox);

        var title = new Label
        {
            Text                = "Surrender?",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        if (_font != null) title.AddThemeFontOverride("font", _font);
        title.AddThemeFontSizeOverride("font_size", 44);
        title.AddThemeColorOverride("font_color", new Color(1f, 0.4f, 0.4f));
        vbox.AddChild(title);

        var sub = new Label
        {
            Text                = "Leaving now counts as a loss.",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        if (_font != null) sub.AddThemeFontOverride("font", _font);
        sub.AddThemeFontSizeOverride("font_size", 18);
        sub.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.85f));
        vbox.AddChild(sub);

        var btnRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        btnRow.AddThemeConstantOverride("separation", 24);
        vbox.AddChild(btnRow);

        var cancelBtn = new Button
        {
            Text              = "Keep Fighting",
            CustomMinimumSize = new Vector2(200, 52),
            FocusMode         = Control.FocusModeEnum.All,
        };
        if (_font != null) cancelBtn.AddThemeFontOverride("font", _font);
        cancelBtn.AddThemeFontSizeOverride("font_size", 18);
        cancelBtn.AddThemeColorOverride("font_color", new Color(0.6f, 0.95f, 0.6f));
        cancelBtn.Pressed += DismissSurrenderDialog;
        btnRow.AddChild(cancelBtn);

        var confirmBtn = new Button
        {
            Text              = "Surrender",
            CustomMinimumSize = new Vector2(200, 52),
            FocusMode         = Control.FocusModeEnum.All,
        };
        if (_font != null) confirmBtn.AddThemeFontOverride("font", _font);
        confirmBtn.AddThemeFontSizeOverride("font_size", 18);
        confirmBtn.AddThemeColorOverride("font_color", new Color(1f, 0.45f, 0.45f));
        bool confirmedClick = false;
        confirmBtn.Pressed += () =>
        {
            if (confirmedClick) return;
            confirmedClick = true;
            ConfirmSurrender();
        };
        btnRow.AddChild(confirmBtn);

        cancelBtn.GrabFocus();
        GD.Print("[ARENA] Surrender dialog opened.");
    }

    void DismissSurrenderDialog()
    {
        if (_surrenderDialog == null) return;
        _surrenderDialog.QueueFree();
        _surrenderDialog = null;
        if (_gameEnded) return;
        // Resume play
        if (_localPlayer != null && IsInstanceValid(_localPlayer))
        {
            _localPlayer.Frozen = false;
            _localPlayer.MakeActive();   // re-capture mouse + re-assert camera
        }
        GD.Print("[ARENA] Surrender dialog dismissed.");
    }

    void ConfirmSurrender()
    {
        if (_gameEnded || _localPlayerDead) return;
        _localPlayerDead = true;
        _nm.SendGameData("{\"t\":\"die\"}");
        GD.Print("[ARENA] Local surrendered — counts as a loss.");
        EndMatch("Surrendered.");
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

        // The local player navigates the opponent's maze:
        //   • Host plays in MazeB (client's design, flipped) → listen on torchB
        //   • Client plays in MazeA (host's design)          → listen on torchA
        var localTorch = OnlineGameState.IsHost ? torchB : torchA;
        localTorch.FirstCellLit += OnSoftLightTriggered;

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
        var vel = _localPlayer.Velocity;
        _nm.SendGameData(
            $"{{\"t\":\"pos\",\"x\":{pos.X:F3},\"y\":{pos.Y:F3},\"z\":{pos.Z:F3}," +
            $"\"vx\":{vel.X:F2},\"vy\":{vel.Y:F2},\"vz\":{vel.Z:F2}," +
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
                if (_opponentMaze != null) break;   // already have it
                try
                {
                    string mazeJson = root.GetProperty("json").GetString()!;
                    var data = JsonSerializer.Deserialize<MazeData>(mazeJson)!;
                    _opponentMaze = data;
                    GD.Print($"[ARENA] Received opponent's maze ({mazeJson.Length} chars).");
                    TryBuildArenaAndSpawn();
                }
                catch (Exception ex)
                {
                    GD.PushError($"[ARENA] Bad opponent maze JSON — {ex.Message}");
                }
                break;

            case "pos":
                if (_puppet != null)
                {
                    Vector3 ppos = new(
                        root.GetProperty("x").GetSingle(),
                        root.GetProperty("y").GetSingle(),
                        root.GetProperty("z").GetSingle());
                    Vector3 pvel = Vector3.Zero;
                    if (root.TryGetProperty("vx", out var vx))
                        pvel = new Vector3(
                            vx.GetSingle(),
                            root.GetProperty("vy").GetSingle(),
                            root.GetProperty("vz").GetSingle());
                    _puppet.SetPuppetTarget(
                        ppos, pvel,
                        root.GetProperty("yaw").GetSingle(),
                        root.GetProperty("pitch").GetSingle());
                    if (root.TryGetProperty("stam", out var sp))
                        _puppet.PuppetStamina = sp.GetSingle();
                }
                break;

            case "hit":
                if (_localPlayer != null && !_gameEnded && !_localPlayerDead)
                {
                    var hp = _localPlayer.GetNodeOrNull<PlayerHealth>("PlayerHealth");
                    hp?.TakeDamage(root.GetProperty("dmg").GetSingle(), Vector3.Zero);
                }
                break;

            case "ready_for_maze":
                if (_ownMazePayload != null)
                {
                    GD.Print("[ARENA] Re-sending own maze on opponent's request.");
                    _nm.SendGameData(_ownMazePayload);
                }
                break;

            case "sword_taken":
                if (!_swordPickedUp && !_gameEnded)
                {
                    _swordPickedUp = true;
                    _localHasSword = false;
                    ShowToast("You are hunted.", 4.0, new Color(1f, 0.4f, 0.3f));
                    StartCombatTimer(peerHasSword: true);
                    GD.Print("[ARENA] Sword taken by opponent.");
                }
                break;

            case "swing":
                // Animate puppet sword swing (visual only)
                break;

            case "die":
                if (!_gameEnded)
                {
                    _matchGold += 200; // I killed the opponent
                    EndMatch("Victory!");
                }
                break;
        }
    }

    void OnPeerDisconnected()
    {
        if (_gameEnded) return;
        _gameEnded = true;
        _combatTimer?.Stop();
        FreezePlayers();
        _nm.Disconnect();
        // Whatever gold they earned up to this point still counts.
        OnlineGameState.Gold += _matchGold;
        PlayerProfile.Save();
        GD.Print($"[ARENA] Peer disconnected (+{_matchGold} gold)");
        ShowEndOverlay("Opponent left.");
    }

    // ── Game-end logic ────────────────────────────────────────────────────────

    void OnLocalSwordPickup()
    {
        if (_swordPickedUp || _gameEnded) return;
        _swordPickedUp = true;
        _localHasSword = true;
        _matchGold    += 100; // sword pickup gold
        _nm.SendGameData("{\"t\":\"sword_taken\"}");
        ShowToast("Sword claimed.", 4.0, new Color(0.3f, 1f, 0.5f));
        StartCombatTimer(peerHasSword: false);
        GD.Print("[ARENA] Local sword pickup — combat timer started.");
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

        // Countdown label centred at top — the sword toast is handled separately by ShowToast.
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
    }

    void OnLocalPlayerDied()
    {
        if (_gameEnded || _localPlayerDead) return;
        _localPlayerDead = true;
        _nm.SendGameData("{\"t\":\"die\"}");
        GD.Print("[ARENA] Local player died.");
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
        FreezePlayers();
        // If the surrender confirmation was open, replace it with the end overlay.
        if (_surrenderDialog != null)
        {
            _surrenderDialog.QueueFree();
            _surrenderDialog = null;
        }
        _nm.GameDataReceived -= OnGameData;
        _nm.Disconnect();
        OnlineGameState.Gold += _matchGold;
        PlayerProfile.Save();
        GD.Print($"[ARENA] EndMatch: {result} (+{_matchGold} fame, total {OnlineGameState.Gold})");
        ShowEndOverlay(result);
    }

    void FreezePlayers()
    {
        if (_localPlayer != null && IsInstanceValid(_localPlayer))
        {
            _localPlayer.Frozen = true;
            _localPlayer.ReleaseMouse();
        }
        if (_puppet != null && IsInstanceValid(_puppet))
            _puppet.Frozen = true;
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
            "Defeated!"      => new Color(1f, 0.3f, 0.3f),
            "Surrendered."   => new Color(1f, 0.4f, 0.4f),
            "Victory!"       => new Color(0.3f, 1f, 0.5f),
            "Opponent left." => new Color(0.85f, 0.85f, 0.85f),
            _                => new Color(1f, 0.9f, 0.3f),
        };

        // Make sure cursor stays usable while the overlay is up.
        Input.MouseMode = Input.MouseModeEnum.Visible;

        var resultLbl = new Label
        {
            Text                = result,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        if (_font != null) resultLbl.AddThemeFontOverride("font", _font);
        resultLbl.AddThemeFontSizeOverride("font_size", 64);
        resultLbl.AddThemeColorOverride("font_color", resultColor);
        vbox.AddChild(resultLbl);

        var gainedLbl = new Label
        {
            Text                = $"+{_matchGold} fame",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        if (_font != null) gainedLbl.AddThemeFontOverride("font", _font);
        gainedLbl.AddThemeFontSizeOverride("font_size", 30);
        gainedLbl.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.3f));
        vbox.AddChild(gainedLbl);

        var totalLbl = new Label
        {
            Text                = $"Total fame: {OnlineGameState.Gold}",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        if (_font != null) totalLbl.AddThemeFontOverride("font", _font);
        totalLbl.AddThemeFontSizeOverride("font_size", 20);
        totalLbl.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.5f));
        vbox.AddChild(totalLbl);

        var exitBtn = new Button
        {
            Text                = "Exit to Menu",
            CustomMinimumSize   = new Vector2(240, 56),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
        };
        if (_font != null) exitBtn.AddThemeFontOverride("font", _font);
        exitBtn.AddThemeFontSizeOverride("font_size", 20);
        bool clicked = false;
        exitBtn.Pressed += () =>
        {
            if (clicked) return;
            clicked = true;
            if (IsInstanceValid(this))
                GetTree().ChangeSceneToFile("res://scenes/TitleScreen.tscn");
        };
        vbox.AddChild(exitBtn);
    }
}
