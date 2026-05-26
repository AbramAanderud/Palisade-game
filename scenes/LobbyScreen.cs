using Godot;
using System.Text.Json;

/// Dual-mode lobby screen:
///   Host   — shows the 3-digit room code, waits for opponent to join.
///   Client — has a LineEdit to enter the code, then waits for host to start.
public partial class LobbyScreen : Control
{
    NetworkManager     _nm          = null!;
    Label              _statusLabel = null!;
    FontFile?          _font;
    AudioStreamPlayer? _clickSfx;
    AudioStreamPlayer? _playGameSfx;

    public override void _Ready()
    {
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _nm = GetNode<NetworkManager>("/root/NetworkManager");
        _font = GD.Load<FontFile>("res://assets/fonts/Agmena Pro Book.ttf");

        _clickSfx = new AudioStreamPlayer();
        var clickStream = GD.Load<AudioStream>("res://assets/audio/ui/MenuButtonClick.wav");
        if (clickStream != null) _clickSfx.Stream = clickStream;
        AddChild(_clickSfx);

        _playGameSfx = new AudioStreamPlayer();
        var playStream = GD.Load<AudioStream>("res://assets/audio/ui/PlayGameButtonNoise.wav");
        if (playStream != null) _playGameSfx.Stream = playStream;
        AddChild(_playGameSfx);

        SetAnchorsPreset(LayoutPreset.FullRect);

        var bg = new ColorRect { Color = new Color(0.04f, 0.04f, 0.06f) };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        var vbox = new VBoxContainer();
        vbox.AnchorLeft   = 0.5f; vbox.AnchorRight  = 0.5f;
        vbox.AnchorTop    = 0.3f; vbox.AnchorBottom = 0.3f;
        vbox.OffsetLeft   = -220f; vbox.OffsetRight  = 220f;
        vbox.GrowHorizontal = GrowDirection.Both;
        vbox.AddThemeConstantOverride("separation", 20);
        AddChild(vbox);

        if (OnlineGameState.IsHost)
            BuildHostUI(vbox);
        else
            BuildClientUI(vbox);

        _statusLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        if (_font != null) _statusLabel.AddThemeFontOverride("font", _font);
        _statusLabel.AddThemeFontSizeOverride("font_size", 18);
        _statusLabel.AddThemeColorOverride("font_color", new Color(0.75f, 0.75f, 0.5f));
        vbox.AddChild(_statusLabel);

        var backBtn = new Button { Text = "← Cancel", CustomMinimumSize = new Vector2(160, 44) };
        if (_font != null) backBtn.AddThemeFontOverride("font", _font);
        backBtn.AddThemeFontSizeOverride("font_size", 16);
        backBtn.Pressed += OnCancel;
        backBtn.Pressed += () => _clickSfx?.Play();
        vbox.AddChild(backBtn);
    }

    public override void _ExitTree()
    {
        _nm.PeerConnected    -= OnPeerConnected;
        _nm.PeerDisconnected -= OnPeerDisconnected;
        _nm.JoinedRoom       -= OnJoinedRoom;
        _nm.ConnectionFailed -= OnConnectionFailed;
        _nm.GameDataReceived -= OnGameData;
    }

    // ── Host UI ───────────────────────────────────────────────────────────────

    void BuildHostUI(VBoxContainer parent)
    {
        var heading = new Label { Text = "Room Created", HorizontalAlignment = HorizontalAlignment.Center };
        if (_font != null) heading.AddThemeFontOverride("font", _font);
        heading.AddThemeFontSizeOverride("font_size", 28);
        heading.AddThemeColorOverride("font_color", Colors.White);
        parent.AddChild(heading);

        var codeLabel = new Label
        {
            Text                = OnlineGameState.RoomCode,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        if (_font != null) codeLabel.AddThemeFontOverride("font", _font);
        codeLabel.AddThemeFontSizeOverride("font_size", 96);
        codeLabel.AddThemeColorOverride("font_color", new Color(0.3f, 1f, 0.5f));
        parent.AddChild(codeLabel);

        var hint = new Label
        {
            Text                = "Give this code to your opponent.",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        if (_font != null) hint.AddThemeFontOverride("font", _font);
        hint.AddThemeFontSizeOverride("font_size", 16);
        hint.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
        parent.AddChild(hint);

        _nm.PeerConnected    += OnPeerConnected;
        _nm.PeerDisconnected += OnPeerDisconnected;
    }

    void OnPeerConnected()
    {
        _statusLabel.Text = "Opponent connected — starting…";
        _nm.SendGameData("{\"t\":\"start\"}");
        _playGameSfx?.Play();
        GetTree().CreateTimer(0.5).Timeout += () =>
            GetTree().ChangeSceneToFile("res://scenes/OnlineDungeonArena.tscn");
    }

    void OnPeerDisconnected()
    {
        _statusLabel.Text = "Opponent disconnected.";
    }

    // ── Client UI ─────────────────────────────────────────────────────────────

    LineEdit? _codeEdit;
    Button?   _joinBtn;

    void BuildClientUI(VBoxContainer parent)
    {
        var heading = new Label { Text = "Join Game", HorizontalAlignment = HorizontalAlignment.Center };
        if (_font != null) heading.AddThemeFontOverride("font", _font);
        heading.AddThemeFontSizeOverride("font_size", 28);
        heading.AddThemeColorOverride("font_color", Colors.White);
        parent.AddChild(heading);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        parent.AddChild(row);

        _codeEdit = new LineEdit
        {
            PlaceholderText   = "Room code…",
            MaxLength         = 3,
            CustomMinimumSize = new Vector2(180, 48),
        };
        if (_font != null) _codeEdit.AddThemeFontOverride("font", _font);
        _codeEdit.AddThemeFontSizeOverride("font_size", 28);
        row.AddChild(_codeEdit);

        _joinBtn = new Button { Text = "Join", CustomMinimumSize = new Vector2(100, 48) };
        if (_font != null) _joinBtn.AddThemeFontOverride("font", _font);
        _joinBtn.AddThemeFontSizeOverride("font_size", 20);
        _joinBtn.Pressed += OnJoin;
        _joinBtn.Pressed += () => _clickSfx?.Play();
        row.AddChild(_joinBtn);
    }

    void OnJoin()
    {
        var code = _codeEdit?.Text.Trim() ?? "";
        if (code.Length != 3) { _statusLabel.Text = "Enter a 3-digit code."; return; }

        if (_joinBtn != null) _joinBtn.Disabled = true;
        _statusLabel.Text = "Connecting…";

        _nm.JoinedRoom       += OnJoinedRoom;
        _nm.ConnectionFailed += OnConnectionFailed;
        _nm.GameDataReceived += OnGameData;
        _nm.ConnectAndJoin(code);
    }

    void OnJoinedRoom(string code)
    {
        _statusLabel.Text = "Waiting for host to start…";
    }

    void OnConnectionFailed(string reason)
    {
        _statusLabel.Text = $"Failed: {reason}";
        if (_joinBtn != null) _joinBtn.Disabled = false;
    }

    void OnGameData(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("t", out var t) && t.GetString() == "start")
            {
                _playGameSfx?.Play();
                GetTree().CreateTimer(0.5).Timeout += () =>
                    GetTree().ChangeSceneToFile("res://scenes/OnlineDungeonArena.tscn");
            }
        }
        catch { }
    }

    // ── Shared ────────────────────────────────────────────────────────────────

    void OnCancel()
    {
        _nm.Disconnect();
        GetTree().ChangeSceneToFile("res://scenes/TitleScreen.tscn");
    }
}
