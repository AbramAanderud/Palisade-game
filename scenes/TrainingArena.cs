using System;
using Godot;

/// Endless survival training arena.
/// Player fights until dead; each killed dummy respawns at the arena edge after 1 s.
/// On player death: death camera zooms out over 2 s, then shows Restart / Exit.
public partial class TrainingArena : Node3D
{
    const int MaxBots = 10;

    PlayerController?  _player;
    int                _aliveCount = 0;
    int                _killCount  = 0;
    CanvasLayer?       _overlay;
    FontFile?          _font;
    AudioStreamPlayer? _killSfx;

    // Death camera state
    Camera3D? _deathCam;
    Vector3   _deathCamTarget;
    Vector3   _deathCamStart;
    Vector3   _deathCamEnd;
    float     _deathCamTimer  = -1f;
    bool      _deathBtnsShown = false;

    public override void _Ready()
    {
        _font = GD.Load<FontFile>("res://assets/fonts/Agmena Pro Book.ttf");

        _killSfx = new AudioStreamPlayer();
        var killStream = GD.Load<AudioStream>("res://assets/audio/sfx/combat/OnKillingDummy.wav");
        if (killStream != null) _killSfx.Stream = killStream;
        AddChild(_killSfx);

        // World environment
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

        // Arena geometry — both doors sealed
        var arena = new ArenaBuilder { Name = "Arena" };
        AddChild(arena);
        arena.Build(Vector3.Zero, openNorth: false, openSouth: false);

        // Solid physics floor — fixes CharacterBody3D launch bug caused by
        // ConcavePolygonShape3D (trimesh) floor detection unreliability.
        var floorBody  = new StaticBody3D { Name = "PhysicsFloor" };
        var floorShape = new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(70f, 0.4f, 70f) },
        };
        floorBody.Position = new Vector3(0f, -0.2f, 0f);
        floorBody.AddChild(floorShape);
        AddChild(floorBody);

        // Spawn player above the floor — PlayerController._Ready() auto-equips the weaker sword
        _player = PlayerController.Spawn(this, new Vector3(0f, 1.5f, 0f), 0f);

        var ph = _player.GetNodeOrNull<PlayerHealth>("PlayerHealth");
        if (ph != null)
            ph.OnDied += ShowLose;

        // Spawn initial ring of enemies
        int   count  = Mathf.Clamp(TrainingConfig.DummyCount, 1, MaxBots);
        float radius = Mathf.Min(8f + count * 0.6f, ArenaBuilder.Apothem - 4f);
        for (int i = 0; i < count; i++)
        {
            float angle = i * Mathf.Tau / count;
            var   pos   = new Vector3(Mathf.Cos(angle) * radius, 1.5f,
                                      Mathf.Sin(angle) * radius);
            SpawnEnemyAt(pos);
        }

        // HUD hint
        var canvas = new CanvasLayer();
        AddChild(canvas);
        var hint = new Label
        {
            Text     = "Survive endless waves!   ESC = exit",
            Position = new Vector2(10, 10),
        };
        if (_font != null) hint.AddThemeFontOverride("font", _font);
        hint.AddThemeFontSizeOverride("font_size", 14);
        hint.AddThemeColorOverride("font_color", new Color(0.65f, 0.65f, 0.65f));
        canvas.AddChild(hint);
    }

    public override void _Input(InputEvent ev)
    {
        if (ev.IsActionPressed("pause") && Input.MouseMode == Input.MouseModeEnum.Visible)
        {
            _player?.ReleaseMouse();
            GetTree().ChangeSceneToFile("res://scenes/TitleScreen.tscn");
        }
    }

    public override void _Process(double delta)
    {
        if (_deathCam == null) return;

        _deathCamTimer += (float)delta;
        float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp(_deathCamTimer / 2f, 0f, 1f));
        _deathCam.GlobalPosition = _deathCamStart.Lerp(_deathCamEnd, t);

        // Safe LookAt — skip if too close to vertical
        var lookDir = (_deathCamTarget - _deathCam.GlobalPosition).Normalized();
        if (lookDir.LengthSquared() > 0.001f && Mathf.Abs(lookDir.Dot(Vector3.Up)) < 0.98f)
            _deathCam.LookAt(_deathCamTarget, Vector3.Up);

        if (!_deathBtnsShown && _deathCamTimer >= 2f)
        {
            _deathBtnsShown = true;
            ShowDeathButtons();
        }
    }

    // ── Enemy management ──────────────────────────────────────────────────────
    void SpawnEnemyAt(Vector3 pos)
    {
        if (_player == null) return;
        var enemy = EnemyController.Spawn(this, pos, _player);
        enemy.Died += OnEnemyDied;
        _aliveCount++;
    }

    void OnEnemyDied()
    {
        _aliveCount--;
        _killCount++;
        _killSfx?.Play();
        // Respawn a fresh enemy at a random point on the arena edge after 1 s
        GetTree().CreateTimer(1.0).Timeout += () =>
        {
            if (!IsInstanceValid(this) || _player == null) return;
            if (_aliveCount >= MaxBots) return;
            float angle = GD.Randf() * Mathf.Tau;
            var   pos   = new Vector3(Mathf.Cos(angle) * 22f, 1.5f,
                                      Mathf.Sin(angle) * 22f);
            SpawnEnemyAt(pos);
        };
    }

    // ── Player death ──────────────────────────────────────────────────────────
    void ShowLose()
    {
        if (_deathCam != null) return;   // guard against double-fire

        // Stop 3D picking immediately — otherwise clicks during the 2 s death-cam
        // pan get eaten by physics (registered as swings on bot bodies / floor).
        GetViewport().PhysicsObjectPicking = false;

        if (_player != null)
        {
            _player.Frozen = true;        // belt-and-suspenders with _isDead
            _player.ReleaseMouse();
            _player.TriggerDeath();
        }
        Input.MouseMode = Input.MouseModeEnum.Visible;

        var playerPos   = _player?.GlobalPosition ?? Vector3.Zero;
        _deathCamStart  = playerPos + new Vector3(0f, 0.74f, 0.5f);   // just behind the eye
        _deathCamEnd    = playerPos + new Vector3(0f, 7.0f, 5.0f);     // overhead pull-back
        _deathCamTarget = playerPos + new Vector3(0f, 0.6f, 0f);       // look at mid-chest
        _deathCamTimer  = 0f;
        _deathBtnsShown = false;

        _deathCam = new Camera3D { Near = 0.1f };
        _deathCam.GlobalPosition = _deathCamStart;
        AddChild(_deathCam);
        _deathCam.MakeCurrent();
    }

    void ShowDeathButtons()
    {
        if (_overlay != null) return;
        _overlay = new CanvasLayer { Layer = 100 };  // sit above any other CanvasLayer
        AddChild(_overlay);

        // Belt-and-suspenders: ShowLose already disables this, but make sure.
        GetViewport().PhysicsObjectPicking = false;
        Input.MouseMode = Input.MouseModeEnum.Visible;

        // Fade the screen to dark — Ignore mouse so clicks fall through to buttons.
        var fade = new ColorRect
        {
            Color       = new Color(0f, 0f, 0f, 0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        fade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _overlay.AddChild(fade);
        fade.CreateTween().TweenProperty(fade, "color:a", 0.80f, 0.55f);

        // "You Died!" title
        var lbl = new Label
        {
            Text                = "You Died!",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
            AnchorLeft          = 0.5f, AnchorRight  = 0.5f,
            AnchorTop           = 0.5f, AnchorBottom = 0.5f,
            GrowHorizontal      = Control.GrowDirection.Both,
            GrowVertical        = Control.GrowDirection.Both,
        };
        lbl.OffsetLeft = -320; lbl.OffsetRight = 320;
        lbl.OffsetTop  = -160; lbl.OffsetBottom = -60;
        if (_font != null) lbl.AddThemeFontOverride("font", _font);
        lbl.AddThemeFontSizeOverride("font_size", 72);
        lbl.AddThemeColorOverride("font_color", new Color(0.95f, 0.18f, 0.10f));
        _overlay.AddChild(lbl);

        // Kill count
        string killWord = _killCount == 1 ? "1 dummy killed" : $"{_killCount} dummies killed";
        var kills = new Label
        {
            Text                = killWord,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
            AnchorLeft          = 0.5f, AnchorRight  = 0.5f,
            AnchorTop           = 0.5f, AnchorBottom = 0.5f,
            GrowHorizontal      = Control.GrowDirection.Both,
            GrowVertical        = Control.GrowDirection.Both,
        };
        kills.OffsetLeft = -320; kills.OffsetRight = 320;
        kills.OffsetTop  = -45;  kills.OffsetBottom = 5;
        if (_font != null) kills.AddThemeFontOverride("font", _font);
        kills.AddThemeFontSizeOverride("font_size", 28);
        kills.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.85f));
        _overlay.AddChild(kills);

        bool clicked = false;
        var restartBtn = AddOverlayBtn("Restart", -150, 150, 25, 85, () =>
        {
            if (clicked) return;
            clicked = true;
            GetTree().ChangeSceneToFile("res://scenes/TrainingArena.tscn");
        });
        AddOverlayBtn("Exit", -150, 150, 100, 160, () =>
        {
            if (clicked) return;
            clicked = true;
            GetTree().ChangeSceneToFile("res://scenes/TitleScreen.tscn");
        });

        // Auto-focus Restart so Enter/Space activates it (also helps if mouse fails).
        restartBtn.GrabFocus();
    }

    Button AddOverlayBtn(string text, float oL, float oR, float oT, float oB, Action cb)
    {
        var btn = new Button
        {
            Text              = text,
            CustomMinimumSize = new Vector2(300, 60),
            MouseFilter       = Control.MouseFilterEnum.Stop,
            FocusMode         = Control.FocusModeEnum.All,
        };
        btn.AnchorLeft     = 0.5f; btn.AnchorRight  = 0.5f;
        btn.AnchorTop      = 0.5f; btn.AnchorBottom = 0.5f;
        btn.GrowHorizontal = Control.GrowDirection.Both;
        btn.GrowVertical   = Control.GrowDirection.Both;
        btn.OffsetLeft = oL; btn.OffsetRight = oR;
        btn.OffsetTop  = oT; btn.OffsetBottom = oB;
        if (_font != null) btn.AddThemeFontOverride("font", _font);
        btn.AddThemeFontSizeOverride("font_size", 22);
        btn.AddThemeColorOverride("font_color",         Colors.White);
        btn.AddThemeColorOverride("font_hover_color",   new Color(1f, 0.9f, 0.5f));
        btn.AddThemeColorOverride("font_pressed_color", new Color(0.6f, 0.6f, 0.6f));
        btn.Pressed += cb;
        _overlay!.AddChild(btn);
        return btn;
    }
}
