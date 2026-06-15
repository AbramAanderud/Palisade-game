using System;
using Godot;

/// res://scripts/PlayerController.cs
/// Movement system:
///   Walk          — WASD, 6 m/s
///   Sprint        — Shift, 11 m/s
///   Crouch walk   — Ctrl while walking (not sprinting), 2.5 m/s, camera lowers
///   Slide         — Ctrl while sprinting OR landing fast from air; camera lowest
///   Slide-jump    — Space during slide; big horizontal + vertical launch
///   Wall-run      — lean into a wall while airborne, gravity pauses 2 s
///   Wall-jump     — Space during wall-run; kick off with directional boost
public partial class PlayerController : CharacterBody3D
{
    // ── Speed constants ────────────────────────────────────────────────────────
    const float WalkSpeed      = 5.5f;
    const float SprintSpeed    = 8.0f;
    const float CrouchSpeed    = 2.5f;

    const float GroundAccel    = 55.0f;   // m/s² — how fast you reach target walk/sprint speed
    const float SprintAccel    = 45.0f;   // m/s² — slightly slower buildup while sprinting
    const float GroundFriction = 70.0f;   // m/s² — very snappy stop when no input
    const float AirAccel       = 4f;
    const float Gravity        = 22f;
    const float JumpSpeed      = 10.5f;
    const float JumpFwdBoost   = 0.7f;      // base forward nudge — scales up with momentum
    const float CoyoteTime     = 0.12f;

    // ── Slide constants ────────────────────────────────────────────────────────
    const float SlideBoostFactor = 1.8f;    // entry = current_speed * factor
    const float SlideMinEntry    = 12.0f;   // minimum entry speed (burst even from slow walk)
    const float SlideMaxEntry    = 32.0f;   // cap to prevent absurd speeds
    const float SlideGrace       = 0.30f;   // no-friction window at slide start
    const float SlideFriction    = 7.0f;    // deceleration after grace
    const float SlideMinSpeed    = 4.0f;    // slide ends below this
    const float SlideJumpV       = 12.0f;   // vertical launch on slide-jump
    const float LandSlideMin     = 8.0f;    // fall speed to trigger auto-slide on land

    // ── Momentum system ────────────────────────────────────────────────────────
    const float MomentumDecayRate    = 2.0f;    // fast decay when braking or stopped
    const float MomentumSprintDecay  = 0.10f;   // slow drain while actively sprinting
    const float MomentumAirDecayRate = 0.15f;   // slow air drain — preserves bhop momentum
    const float MomentumSlideBonus   = 0.20f;
    const float MomentumJumpBonus    = 0.20f;   // meaningful per-hop build for bhopping
    const float MomentumWallBonus    = 0.15f;
    const float MomentumSpeedBonus   = 0.50f;   // sprint * 1.5 at full momentum
    float _momentum = 0f;

    // ── Sustained sprint ramp ──────────────────────────────────────────────────
    // After SprintRampTime seconds of continuous sprinting you get a flat speed bonus.
    const float SprintRampTime  = 3.0f;
    const float SprintRampBonus = 0.15f;  // +15% sprint speed after 3 s
    float _sprintTime = 0f;

    // ── Camera heights ─────────────────────────────────────────────────────────
    // Model root is 0.9 m below origin; UAL1 eye socket ≈ 0.74 m above origin.
    // Body is on visual layer 2 — FP camera excludes that layer, so no inside-mesh hacks needed.
    const float EyeHeight   = 0.74f;
    const float CrouchEyeH = 0.38f;
    const float SlideEyeH  = 0.26f;

    // ── Wall-run constants ─────────────────────────────────────────────────────
    const float WallRunSpeed   = 15.0f;
    const float WallRunAccel   = 4.0f;     // m/s² — gradual, not snappy
    const float WallRunTime    = 6.0f;
    const float WallRunGravity = 3.0f;
    const float WallJumpOff    = 8.0f;
    const float WallJumpUp     = 11.0f;
    const float WallRunCooldown= 0.4f;

    const float MouseSens      = 0.002f;
    const float PitchClamp     = 85f * Mathf.Pi / 180f;

    // ── Nodes ─────────────────────────────────────────────────────────────────
    Camera3D        _cam   = null!;
    Node3D          _mount = null!;
    Node3D?         _heldWeapon;   // Node3D wrapper; inner mesh is flipped 180° Y so blade points forward

    // ── Look ──────────────────────────────────────────────────────────────────
    float _yaw   = 0f;
    float _pitch = 0f;

    // ── Slide state ───────────────────────────────────────────────────────────
    bool    _sliding        = false;
    Vector3 _slideDir       = Vector3.Zero;
    float   _slideSp        = 0f;
    float   _slideGraceLeft = 0f;   // no-friction burst window at slide start
    float   _slideMinTimer  = 0f;   // player is locked into the slide for this many seconds

    // ── Crouch state ──────────────────────────────────────────────────────────
    bool _crouching = false;   // prone crouch walk (Ctrl while walking)

    // ── Wall-run state ────────────────────────────────────────────────────────
    bool    _wallRunning  = false;
    float   _wallRunTimer = 0f;
    float   _wallCooldown = 0f;
    Vector3 _wallNormal   = Vector3.Zero;
    float   _wallRollSign = 0f;

    // ── Coyote time ───────────────────────────────────────────────────────────
    float _coyote     = 0f;
    bool  _wasOnFloor = false;

    // ── Landing-slide tracking ─────────────────────────────────────────────────
    float _airFallSpeed = 0f;

    // ── Sprint grace — slide window after releasing sprint ────────────────────
    // Does NOT decay in the air so sprint→jump→land→slide still works.
    const float SprintGrace    = 0.8f;
    float _sprintGraceTimer    = 0f;

    // ── Stair climbing ────────────────────────────────────────────────────────
    const float StairDownBoost  = 10f;   // m/s² gravity assist going down stairs
    const float StairStepHeight = 0.62f; // max riser height to auto-step over
    float _prevFloorY = 0f;              // floor height last frame, used to detect descent

    // ── Camera smoothing ──────────────────────────────────────────────────────
    float _camH    = EyeHeight;
    float _camRoll = 0f;

    // ── Third-person camera ────────────────────────────────────────────────────
    const float TpDistance      = 6.0f;
    const float TpDistanceBlock = 5.0f;
    const float TpElevation     = 2.2f;
    const float TpElevationSlide= 1.0f;
    bool        _tpMode         = false;
    Camera3D    _tpCam          = null!;
    Node3D      _playerBody     = null!;
    float       _tpDistCurrent  = 5.0f;
    float       _tpElevCurrent  = 2.5f;

    // ── Weapon node references ─────────────────────────────────────────────────
    MeshInstance3D? _tpWeapon;    // bone-attached TP sword
    Node3D?         _fpArmsModel; // camera-space arm model (layer 3, FP only)


    // ── Camera bob (FP only — moves the camera, not the model) ───────────────
    float   _camBobPhase  = 0f;
    Vector3 _camBobOffset = Vector3.Zero;

    // ── Weapon sway (spring simulation driven by mouse input) ─────────────────
    Vector2 _frameMouseDelta = Vector2.Zero;
    Vector2 _swayVelocity    = Vector2.Zero;
    Vector2 _swayOffset      = Vector2.Zero;
    const float SwayScale  = 0.0025f;
    const float SwaySpring = 9.0f;
    const float SwayDamp   = 5.5f;
    const float SwayLimit  = 0.035f;

    // ── Hold-R reset ──────────────────────────────────────────────────────────
    float _resetHoldTime = 0f;
    const float ResetHoldThreshold = 0.5f;

    // ── Death state ───────────────────────────────────────────────────────────
    bool _isDead = false;

    // ── World-space health+stamina bars (puppet / online opponent) ────────────
    Node3D? _worldBarRoot;

    // ── Audio ─────────────────────────────────────────────────────────────────
    AudioStreamPlayer _walkSfx = null!;
    AudioStreamPlayer _runSfx  = null!;
    AudioStreamPlayer _jumpSfx = null!;
    AudioStreamPlayer _landSfx = null!;
    AudioStreamPlayer _hitSfx  = null!;

    // ── Animator ──────────────────────────────────────────────────────────────
    PlayerAnimator _animator = null!;

    // ── Combat ────────────────────────────────────────────────────────────────
    SwordCombat _combat          = null!;
    Vector3     _lungeDir        = Vector3.Zero;
    float       _lungeSpeed      = 0f;
    // 0.5 = starter sword (half momentum), 1.0 = middle sword (full momentum)
    float       _aerialLungePow  = 0.5f;

    // ── Public ────────────────────────────────────────────────────────────────
    public bool HasWeapon { get; private set; } = false;

    /// Player's current facing direction in world space (yaw only, Y=0).
    public Vector3 ForwardVector => new Vector3(-Mathf.Sin(_yaw), 0f, -Mathf.Cos(_yaw));

    /// Current combat phase — Idle when the player has no weapon.
    public SwordCombat.Phase PlayerCombatPhase =>
        HasWeapon ? _combat.CombatPhase : SwordCombat.Phase.Idle;

    /// Which combo step is active (0=Swing1, 1=Swing2, 2=Swing3).
    public int PlayerComboStep => HasWeapon ? _combat.ComboStep : 0;

    /// Local player's current stamina (sent over network for opponent's bar).
    public float PlayerStamina => _combat != null ? _combat.Stamina : 150f;

    /// Stamina value received from network for this puppet; drives the world stamina bar.
    public float PuppetStamina { get; set; } = 150f;

    /// Creates a world-space HP+stamina bar above this player (for online opponent display).
    public void EnableWorldBars()
    {
        if (_worldBarRoot != null) return;
        _worldBarRoot = EnemyController.BuildBarGroup(2.2f);
        AddChild(_worldBarRoot);
    }

    public override void _Process(double delta)
    {
        if (_worldBarRoot == null) return;

        const float BarW  = 0.9f;
        const float HpH   = 0.12f;
        const float StamH = 0.03f;
        const float Gap   = 0.005f;
        float stamY = -(HpH / 2f + Gap + StamH / 2f);

        var health  = GetNodeOrNull<PlayerHealth>("PlayerHealth");
        float hpFrac = health != null
            ? Mathf.Clamp(health.CurrentHealth / PlayerHealth.MaxHealth, 0f, 1f) : 1f;
        var hpFill = _worldBarRoot.GetNodeOrNull<MeshInstance3D>("HpFill");
        if (hpFill != null)
        {
            hpFill.Scale    = new Vector3(Mathf.Max(hpFrac, 0.001f), 1f, 1f);
            hpFill.Position = new Vector3(BarW * (hpFrac - 1f) / 2f, 0f, 0f);
        }

        float rawStam  = IsRemotePuppet ? PuppetStamina : PlayerStamina;
        float maxStam  = _combat?.MaxStam ?? 150f;
        float sFrac    = Mathf.Clamp(rawStam / maxStam, 0f, 1f);
        var   stamFill = _worldBarRoot.GetNodeOrNull<MeshInstance3D>("StamFill");
        if (stamFill != null)
        {
            stamFill.Scale    = new Vector3(Mathf.Max(sFrac, 0.001f), 1f, 1f);
            stamFill.Position = new Vector3(BarW * (sFrac - 1f) / 2f, stamY, 0f);
        }

        var cam = GetViewport().GetCamera3D();
        if (cam != null)
        {
            var toCamera = cam.GlobalPosition - _worldBarRoot.GlobalPosition;
            toCamera.Y   = 0f;
            if (toCamera.LengthSquared() > 0.001f)
                _worldBarRoot.LookAt(_worldBarRoot.GlobalPosition + toCamera, Vector3.Up);
        }
    }

    void ApplyAerialLungeDash()
    {
        var   horiz    = new Vector3(Velocity.X, 0f, Velocity.Z);
        float horizSpd = horiz.Length();
        float momentumScale = 1f + _momentum * 0.5f;   // up to 1.5× at full momentum
        // Base: 9 (starter) → 18 (middle). Scale: 1.0× (starter) → 2.0× (middle).
        float dashSpd  = Mathf.Clamp(horizSpd * 2.0f * _aerialLungePow * momentumScale,
                                     18f * _aerialLungePow, 42f * _aerialLungePow);
        _combat.AerialLungeSpeed = horizSpd;
        var fwd  = new Vector3(-Mathf.Sin(_yaw), 0f, -Mathf.Cos(_yaw));
        _lungeDir   = fwd;
        _lungeSpeed = dashSpd;
        Velocity = new Vector3(fwd.X * dashSpd, Mathf.Max(Velocity.Y, 0f), fwd.Z * dashSpd);
    }

    void ApplyAimAssist(float maxCorrectionDeg = 15f)
    {
        float maxCorrection = maxCorrectionDeg * Mathf.Pi / 180f;
        const float ScanRadius  = 8f;
        const float ConeHalfCos = 0.5f;   // cos(60°) — search ±60° in front

        var    fwd     = ForwardVector;
        Node3D? best   = null;
        float   bestDot = ConeHalfCos;

        foreach (var node in GetTree().GetNodesInGroup("enemies"))
        {
            if (node is not Node3D target) continue;
            var   toTarget = target.GlobalPosition - GlobalPosition;
            toTarget.Y     = 0f;
            float dist     = toTarget.Length();
            if (dist > ScanRadius || dist < 0.001f) continue;
            float dot = fwd.Dot(toTarget / dist);
            if (dot > bestDot) { bestDot = dot; best = target; }
        }

        if (best == null) return;

        var   toEnemy   = best.GlobalPosition - GlobalPosition;
        toEnemy.Y       = 0f;
        toEnemy         = toEnemy.Normalized();
        float targetYaw = Mathf.Atan2(-toEnemy.X, -toEnemy.Z);
        float delta     = Mathf.Atan2(Mathf.Sin(targetYaw - _yaw), Mathf.Cos(targetYaw - _yaw));
        _yaw += Mathf.Clamp(delta, -maxCorrection, maxCorrection);
        ApplyLook();
    }

    // ── Remote puppet support ─────────────────────────────────────────────────
    public bool  IsRemotePuppet { get; set; } = false;
    public float Yaw   => _yaw;
    public float Pitch => _pitch;

    Vector3 _puppetTargetPos   = Vector3.Zero;
    Vector3 _puppetTargetVel   = Vector3.Zero;
    float   _puppetTargetYaw   = 0f;
    float   _puppetTargetPitch = 0f;
    ulong   _puppetReceiveMs   = 0;   // local Time.GetTicksMsec at receive
    bool    _puppetHasTarget   = false;

    const float PuppetExtrapolateCapSec = 0.2f;   // cap forward prediction at 200 ms
    const float PuppetSnapDistance      = 2.0f;   // snap if extrapolated target drifts > 2 m

    /// Set puppet target with velocity for extrapolation. Velocity is in world units/sec.
    public void SetPuppetTarget(Vector3 pos, Vector3 velocity, float yaw, float pitch)
    {
        _puppetTargetPos   = pos;
        _puppetTargetVel   = velocity;
        _puppetTargetYaw   = yaw;
        _puppetTargetPitch = pitch;
        _puppetReceiveMs   = Time.GetTicksMsec();
        _puppetHasTarget   = true;
    }

    /// Re-assert this player's camera as current and re-capture the mouse.
    public void MakeActive()
    {
        (_tpMode ? _tpCam : _cam).MakeCurrent();
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    /// Register a callback that fires when a swing starts (index 0-3).
    public void AddSwingListener(Action<int> cb) => _combat.OnSwingStart += cb;

    // ── Spawn ─────────────────────────────────────────────────────────────────
    public static PlayerController Spawn(Node parent, Vector3 pos, float yawDeg = 180f)
    {
        var p = new PlayerController { Name = "Player" };
        parent.AddChild(p);
        p.Position = pos;
        p._yaw     = yawDeg * Mathf.Pi / 180f;
        p.ResetPhysicsInterpolation();
        return p;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    public override void _Ready()
    {
        AddToGroup("players");

        var shape = new CapsuleShape3D { Radius = 0.35f, Height = 1.6f };
        AddChild(new CollisionShape3D { Shape = shape, Name = "Body" });
        CollisionMask  = 0xFFFFFFFF;
        MotionMode     = MotionModeEnum.Grounded;
        UpDirection    = Vector3.Up;
        FloorMaxAngle  = 68f * Mathf.Pi / 180f;  // allow climbing stair ramps (current ramp ~61°)
        FloorSnapLength = 1.5f;                   // snap down to next stair tread (~0.52m drop)

        _mount = new Node3D { Name = "CamMount", Position = new(0, EyeHeight, 0) };
        AddChild(_mount);
        // FP camera: sits exactly at the eye socket (no forehead hack needed — body is on layer 2).
        // CullMask excludes layer 2 (player body) so the camera never sees its own mesh.
        _cam = new Camera3D { Name = "PlayerCam", Fov = GameSettings.Instance?.FOV ?? 90f, Near = 0.04f, Far = 500f };
        _cam.CullMask = ~2u;           // all layers except layer 2 (player body)
        _mount.AddChild(_cam);

        // FP sword — camera-space, layer 3 (bitmask 4); only FP cam can see it
        // (Layers is set on the inner MeshInstance3D inside BuildFpSword.)
        _heldWeapon = BuildFpSword();
        _cam.AddChild(_heldWeapon);

        _playerBody = BuildPlayerBody();
        _playerBody.Visible = true;
        AddChild(_playerBody);
        // Put every mesh in the player body on visual layer 2 — invisible to FP cam
        SetLayersRecursive(_playerBody, 2u);

        _animator = new PlayerAnimator();
        AddChild(_animator);
        var modelRoot = _playerBody.GetNodeOrNull<Node3D>("CharacterModel");
        if (modelRoot != null) _animator.Init(modelRoot);

        // FP arms — camera-space, layer 3; only exists once UAL1_FpArms.glb is exported
        _fpArmsModel = BuildFpArmsModel();
        if (_fpArmsModel != null)
        {
            _cam.AddChild(_fpArmsModel);
            _animator.InitFpArms(_fpArmsModel);
        }

        // TP camera: sees world (1) + player body (2), NOT the FP-only weapon (4)
        _tpCam = new Camera3D { Name = "TpCam", Fov = 75f, Near = 0.1f, Far = 500f };
        _tpCam.CullMask = ~4u;         // all layers except layer 3 (FP weapon)
        AddChild(_tpCam);

        _combat = new SwordCombat();
        AddChild(_combat);

        // Wire animator callbacks so combat state drives animation
        _combat.OnSwingStart  = step => _animator.PlayAttack(step);
        _combat.OnSwingStart += _    => { if (!IsRemotePuppet) ApplyAimAssist(); };
        _combat.OnBlockChange       = b    => _animator.PlayBlock(b);
        _combat.OnAerialLungeStart = () =>
        {
            if (!IsRemotePuppet) ApplyAimAssist(20f);
            ApplyAerialLungeDash();
            _animator.PlayAerialLunge();
        };

        // Add health component + HUD
        var health = new PlayerHealth { Name = "PlayerHealth" };
        AddChild(health);
        health.DamageDealt += _ => { if (!IsRemotePuppet) _hitSfx.Play(); };
        AddChild(new PlayerHUD());

        _walkSfx = MakeSfx("res://assets/audio/sfx/movement/Walk.wav");
        _runSfx  = MakeSfx("res://assets/audio/sfx/movement/Run.wav");
        _jumpSfx = MakeSfx("res://assets/audio/sfx/movement/Jump.wav");
        _landSfx = MakeSfx("res://assets/audio/sfx/movement/Land.wav");
        _hitSfx  = MakeSfx("res://assets/audio/sfx/combat/swordfleshimpact.wav");

        _animator.StepTaken += spd =>
        {
            if (!IsRemotePuppet)
                (spd > 7f ? _runSfx : _walkSfx).Play();
        };

        ApplyLook();
        _cam.MakeCurrent();
        Input.MouseMode = Input.MouseModeEnum.Captured;

        // Players spawn already armed with the weaker sword
        EquipWeakerSword();
    }

    AudioStreamPlayer MakeSfx(string path)
    {
        var sfx    = new AudioStreamPlayer();
        var stream = GD.Load<AudioStream>(path);
        if (stream != null) sfx.Stream = stream;
        AddChild(sfx);
        return sfx;
    }

    /// When true, the player ignores all input and stops physics — used to lock
    /// movement / mouse / swings while the end-of-match overlay is up.
    public bool Frozen { get; set; } = false;

    public override void _Input(InputEvent ev)
    {
        if (IsRemotePuppet || _isDead || Frozen) return;
        if (ev is InputEventMouseMotion mm && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            _frameMouseDelta += mm.Relative;   // accumulate for weapon sway
            _yaw   -= mm.Relative.X * MouseSens;
            _pitch  = Mathf.Clamp(_pitch - mm.Relative.Y * MouseSens, -PitchClamp, PitchClamp);
            ApplyLook();
        }
        if (ev is InputEventKey pauseKey && pauseKey.Keycode == Key.Escape && pauseKey.Pressed && !pauseKey.Echo)
            Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Captured
                ? Input.MouseModeEnum.Visible
                : Input.MouseModeEnum.Captured;
        if (ev.IsActionPressed("toggle_camera") && !(ev is InputEventKey ek && ek.Echo))
            ToggleCameraMode();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_isDead || Frozen) return;

        if (IsRemotePuppet)
        {
            float dt2 = (float)delta;
            if (_puppetHasTarget)
            {
                // Extrapolate forward by time since packet arrived, capped to 200 ms.
                float elapsedSec = (Time.GetTicksMsec() - _puppetReceiveMs) / 1000f;
                if (elapsedSec > PuppetExtrapolateCapSec) elapsedSec = PuppetExtrapolateCapSec;
                Vector3 predicted = _puppetTargetPos + _puppetTargetVel * elapsedSec;

                // Snap on big jumps (respawn / teleport / packet loss recovery).
                if (GlobalPosition.DistanceTo(predicted) > PuppetSnapDistance)
                    GlobalPosition = predicted;
                else
                    GlobalPosition = GlobalPosition.Lerp(predicted, 20f * dt2);
            }
            _yaw   = Mathf.LerpAngle(_yaw,   _puppetTargetYaw,   20f * dt2);
            _pitch = Mathf.Lerp(_pitch, _puppetTargetPitch, 20f * dt2);
            ApplyLook();
            _playerBody.Rotation = new Vector3(0f, _yaw, 0f);
            return;
        }

        float dt         = (float)delta;
        bool  onFloor    = IsOnFloor();
        bool  justLanded = onFloor && !_wasOnFloor;  // capture before coyote overwrites

        // ── Coyote time ───────────────────────────────────────────────────────
        if (onFloor) _coyote = CoyoteTime;
        else         _coyote = Mathf.MoveToward(_coyote, 0f, dt);

        // ── Track fall speed for landing-slide ────────────────────────────────
        if (!onFloor && Velocity.Y < 0f)
            _airFallSpeed = Mathf.Abs(Velocity.Y);

        // ── Wall-run cooldown ─────────────────────────────────────────────────
        if (_wallCooldown > 0) _wallCooldown -= dt;

        // ── Landing-slide trigger — only fires when Ctrl is held on landing ─────
        if (justLanded && !_sliding && Input.IsActionPressed("slide"))
        {
            var hv       = new Vector3(Velocity.X, 0, Velocity.Z);
            var slideDir = hv.Length() > 0.5f ? hv
                         : new Vector3(-Mathf.Sin(_yaw), 0f, -Mathf.Cos(_yaw));
            float entrySpeed = Mathf.Clamp(hv.Length() * SlideBoostFactor, SlideMinEntry, SlideMaxEntry);
            _momentum += MomentumSlideBonus + 0.10f;
            StartSlide(slideDir, entrySpeed);
        }
        if (onFloor) _airFallSpeed = 0f;

        // ── Land cancels wall-run ─────────────────────────────────────────────
        if (onFloor && _wallRunning) EndWallRun();

        Vector3 vel = Velocity;

        // ── Gravity ───────────────────────────────────────────────────────────
        if (!onFloor && !_wallRunning)
            vel.Y -= Gravity * dt;
        else if (onFloor && !_sliding)
            vel.Y = -0.5f;

        // ── State dispatch ────────────────────────────────────────────────────
        if (_wallRunning)
            WallRunTick(ref vel, dt);
        else if (_sliding)
            SlideTick(ref vel, dt, onFloor);
        else
            NormalTick(ref vel, dt, onFloor);

        _wasOnFloor = onFloor;

        // ── Hold R to teleport back to maze start ─────────────────────────────
        if (Input.IsKeyPressed(Key.R))
        {
            _resetHoldTime += dt;
            if (_resetHoldTime >= ResetHoldThreshold)
            {
                _resetHoldTime = 0f;
                var startPos = DungeonBuilder.MazeStartPosition;
                if (startPos != Vector3.Zero)
                {
                    Position = startPos;
                    vel      = Vector3.Zero;
                    _sliding        = false;
                    _wallRunning    = false;
                    _airFallSpeed   = 0f;
                    _coyote         = 0f;
                }
            }
        }
        else
        {
            _resetHoldTime = 0f;
        }

        AdjustStairVelocity(ref vel, dt);
        TryStepUp(vel, dt);

        // Hold lunge velocity constant throughout the aerial lunge (overrides air friction)
        if (HasWeapon && _combat.IsLunging)
        {
            vel.X = _lungeDir.X * _lungeSpeed;
            vel.Z = _lungeDir.Z * _lungeSpeed;
        }

        Velocity = vel;
        MoveAndSlide();

        if (IsOnFloor()) _prevFloorY = GlobalPosition.Y - 0.8f;

        if (!IsOnFloor() && !_wallRunning && _wallCooldown <= 0)
            TryStartWallRun();

        // ── Sword combat ──────────────────────────────────────────────────────
        if (HasWeapon)
            _combat.Tick(dt, !onFloor, _wallRunning, _yaw);

        TickWallhack(dt);

        // ── Smooth camera height ───────────────────────────────────────────────
        float targetH = _sliding ? SlideEyeH : _crouching ? CrouchEyeH : EyeHeight;
        _camH = Mathf.MoveToward(_camH, targetH, 6f * dt);
        _mount.Position = new(0f, _camH, 0f);

        // ── Camera roll during wall-run ────────────────────────────────────────
        float targetRoll = _wallRunning ? _wallRollSign * 12f * Mathf.Pi / 180f : 0f;
        _camRoll = Mathf.Lerp(_camRoll, targetRoll, 5f * dt);
        _cam.Rotation = new(_pitch, 0f, _camRoll);

        // ── Momentum update ────────────────────────────────────────────────────
        // Momentum is built only by discrete events (bhop, slide, wall-run).
        // Sprint alone drains it slowly; stopping drains it fast.
        var  hSpd     = new Vector2(Velocity.X, Velocity.Z).Length();
        bool inFreefall  = !IsOnFloor() && !_sliding;   // includes wall-run
        bool hasInput    = Input.GetVector("move_left", "move_right", "move_forward", "move_back") != Vector2.Zero;
        bool isBraking   = IsOnFloor() && !_sliding && (!hasInput || hSpd < SprintSpeed * 0.4f);
        if (inFreefall)
            _momentum = Mathf.Max(_momentum - MomentumAirDecayRate * dt, 0f);
        else if (isBraking)
            _momentum = Mathf.Max(_momentum - MomentumDecayRate * dt, 0f);
        else if (!_sliding)
            _momentum = Mathf.Max(_momentum - MomentumSprintDecay * dt, 0f);

        // ── Animate player body ────────────────────────────────────────────────
        _animator.Tick(dt, IsOnFloor(), _sliding, _wallRunning, _wallRollSign, HasWeapon, hSpd, Velocity.Y);

        // ── Land sound (local player only) ───────────────────────────────────
        if (!IsRemotePuppet && justLanded && _airFallSpeed > 1.0f)
            _landSfx.Play();

        // ── Camera bob + weapon sway ──────────────────────────────────────────
        if (!_tpMode)
        {
            UpdateCameraBob(dt, hSpd, IsOnFloor());
            UpdateWeaponSway(dt);
            AnimateFpSword(dt);
        }

        // ── FP FOV swell — expands with speed + momentum for visceral feedback ────
        bool isSpr = Input.IsActionPressed("sprint") && hSpd > WalkSpeed;
        float baseFov   = GameSettings.Instance?.FOV ?? 90f;
        float fovMom    = Mathf.Min(_momentum, 3f);   // visual cap — FOV readable up to 3× momentum
        float targetFov = _sliding ? (baseFov + 5f  + fovMom * 15f)
                        : isSpr    ? (baseFov + 2f  + fovMom * 12f)
                        : baseFov;
        _cam.Fov = Mathf.Lerp(_cam.Fov, targetFov, 5f * dt);

        // ── Player body yaw tracks look direction ──────────────────────────────
        _playerBody.Rotation = new(0f, _yaw, 0f);

        // ── TP camera follows behind player ────────────────────────────────────
        if (_tpMode)
        {
            bool blocking = HasWeapon && _combat.IsBlocking;
            float targetDist = blocking ? TpDistanceBlock : TpDistance;
            float targetElev = _sliding  ? TpElevationSlide : TpElevation;
            _tpDistCurrent = Mathf.MoveToward(_tpDistCurrent, targetDist, 8f * dt);
            _tpElevCurrent = Mathf.MoveToward(_tpElevCurrent, targetElev, 4f * dt);

            var pivot = GlobalPosition + Vector3.Up * (EyeHeight * 0.7f);
            var back  = new Vector3(Mathf.Sin(_yaw), 0f, Mathf.Cos(_yaw));
            _tpCam.GlobalPosition = pivot + back * _tpDistCurrent + Vector3.Up * _tpElevCurrent;
            _tpCam.LookAt(pivot, Vector3.Up);
        }
    }

    // ── Normal movement ───────────────────────────────────────────────────────
    void NormalTick(ref Vector3 vel, float dt, bool onFloor)
    {
        Vector2 input    = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
        bool isSprinting = Input.IsActionPressed("sprint") && input != Vector2.Zero;
        bool slideHeld   = Input.IsActionPressed("slide");

        // Sprint grace — feed while sprinting, decay only on ground (not in air)
        if (isSprinting)
            _sprintGraceTimer = SprintGrace;
        else if (onFloor && !_sliding)
            _sprintGraceTimer = Mathf.MoveToward(_sprintGraceTimer, 0f, dt);

        // Track sustained sprint time — bonus kicks in after SprintRampTime seconds
        if (isSprinting && onFloor)
            _sprintTime += dt;
        else if (!isSprinting)
            _sprintTime = 0f;
        float ramp = _sprintTime >= SprintRampTime ? SprintRampBonus : 0f;

        // Sprint speed: momentum bonus + sustained-sprint ramp (constant once unlocked)
        float momentumSpeed = SprintSpeed * (1f + _momentum * MomentumSpeedBonus + ramp);
        float speed = isSprinting ? momentumSpeed
            : slideHeld ? CrouchSpeed
            : (HasWeapon && _combat.IsBlocking) ? CrouchSpeed
            : WalkSpeed;

        var hVelNow = new Vector3(vel.X, 0, vel.Z);
        // Only crouch-walk when slide is held with no significant movement (not sprinting)
        _crouching = slideHeld && !isSprinting && hVelNow.Length() < 2f && !_sliding;

        var fwd   = new Vector3(-Mathf.Sin(_yaw), 0f, -Mathf.Cos(_yaw));
        var right = new Vector3( Mathf.Cos(_yaw), 0f, -Mathf.Sin(_yaw));
        var wish  = (fwd * (-input.Y) + right * input.X).Normalized();

        var hVel = new Vector3(vel.X, 0, vel.Z);
        if (onFloor)
        {
            if (wish.LengthSquared() > 0.01f)
            {
                // Accelerate toward target — sprint uses a slightly lower accel for the
                // "momentum buildup" feel; walk/crouch snap to speed more responsively.
                float accel = isSprinting ? SprintAccel : GroundAccel;
                hVel = hVel.MoveToward(wish * speed, accel * dt);
            }
            else
            {
                // No input — apply friction so momentum carries briefly then stops
                hVel = hVel.MoveToward(Vector3.Zero, GroundFriction * dt);
            }
        }
        else
        {
            // Air control — momentum-scaled cap so bhop speed is preserved in air
            hVel += wish * AirAccel * dt;
            float cap = SprintSpeed * (1f + _momentum * MomentumSpeedBonus);
            if (hVel.Length() > cap) hVel = hVel.Normalized() * cap;
        }
        vel.X = hVel.X;
        vel.Z = hVel.Z;

        // Jump — halve horizontal carry so you don't travel a full hallway forward
        if (Input.IsActionJustPressed("jump") && _coyote > 0f)
        {
            vel.Y   = JumpSpeed;
            _coyote = 0f;
            _jumpSfx.Play();
            _momentum += MomentumJumpBonus;
            var hv = new Vector3(vel.X, 0f, vel.Z);
            if (hv.LengthSquared() > 0.01f)
            {
                hv += hv.Normalized() * JumpFwdBoost;
                vel.X = hv.X;
                vel.Z = hv.Z;
            }
        }

        // Slide — only available during/after sprint (grace window).
        // Instant if held while sprinting; fresh press works within grace period.
        bool canSlide            = isSprinting || _sprintGraceTimer > 0f;
        bool slideJustPressed    = Input.IsActionJustPressed("slide");
        bool slideWhileSprinting = slideHeld && isSprinting && !_sliding;
        if (canSlide && (slideJustPressed || slideWhileSprinting) && onFloor && !(HasWeapon && _combat.IsBlocking))
        {
            var slideDir = hVel.Length() > 1f ? hVel
                         : new Vector3(-Mathf.Sin(_yaw), 0f, -Mathf.Cos(_yaw));
            float entrySpeed = Mathf.Clamp(hVel.Length() * SlideBoostFactor, SlideMinEntry, SlideMaxEntry);
            _momentum += MomentumSlideBonus;
            StartSlide(slideDir, entrySpeed);
        }
    }

    // ── Slide ─────────────────────────────────────────────────────────────────
    void StartSlide(Vector3 hVel, float speed)
    {
        _sliding        = true;
        _crouching      = false;
        _slideDir       = hVel.Normalized();
        _slideSp        = speed;
        _slideGraceLeft = SlideGrace;
        _slideMinTimer  = 1.0f;   // locked in for 1 second
    }

    void SlideTick(ref Vector3 vel, float dt, bool onFloor)
    {
        _slideMinTimer = Mathf.MoveToward(_slideMinTimer, 0f, dt);

        if (!onFloor) { EndSlide(ref vel); return; }  // falling off a ledge always ends it

        // Key release and speed-floor only end the slide after the lock-in window
        if (!Input.IsActionPressed("slide") && _slideMinTimer <= 0f) { EndSlide(ref vel); return; }

        if (_slideGraceLeft > 0f)
            _slideGraceLeft -= dt;
        else
            _slideSp = Mathf.MoveToward(_slideSp, 0f, SlideFriction * dt);

        if (_slideSp < SlideMinSpeed && _slideMinTimer <= 0f) { EndSlide(ref vel); return; }

        vel.X = _slideDir.X * _slideSp;
        vel.Z = _slideDir.Z * _slideSp;
        vel.Y = -0.5f;

        if (Input.IsActionJustPressed("jump"))
        {
            vel.X     = _slideDir.X * _slideSp;
            vel.Z     = _slideDir.Z * _slideSp;
            vel.Y     = SlideJumpV;
            _momentum += 0.35f;   // slide-jump: big immediate momentum burst
            _sliding  = false;
            _jumpSfx.Play();
        }
    }

    void EndSlide(ref Vector3 vel) => _sliding = false;

    // ── Wall-run ──────────────────────────────────────────────────────────────
    void TryStartWallRun()
    {
        var hVel = new Vector3(Velocity.X, 0, Velocity.Z);
        if (hVel.Length() < 3f) return;

        // Primary: slide collisions from MoveAndSlide (player ran directly into wall)
        for (int i = 0; i < GetSlideCollisionCount(); i++)
        {
            var col = GetSlideCollision(i);
            Vector3 n = col.GetNormal();
            if (Mathf.Abs(n.Y) > 0.3f) continue;
            float alongWall = hVel.Dot(new Vector3(-n.Z, 0f, n.X));
            if (Mathf.Abs(alongWall) < 1f) continue;
            BeginWallRun(n);
            return;
        }

        // Fallback: TestMove toward each side using the body's own capsule shape.
        // IMPORTANT: IntersectRay does NOT detect ConcavePolygonShape3D (dungeon walls
        // are concave trimesh). TestMove uses MoveAndSlide's detection path which does.
        var rightVec = new Vector3(Mathf.Cos(_yaw), 0f, -Mathf.Sin(_yaw));
        foreach (var side in new[] { rightVec, -rightVec })
        {
            var col = new KinematicCollision3D();
            if (!TestMove(GlobalTransform, side * 0.5f, col)) continue;
            var n = col.GetNormal();
            if (Mathf.Abs(n.Y) > 0.3f) continue;
            float alongWall = hVel.Dot(new Vector3(-n.Z, 0f, n.X));
            if (Mathf.Abs(alongWall) < 1f) continue;
            BeginWallRun(n);
            return;
        }
    }

    void BeginWallRun(Vector3 wallNormal)
    {
        _wallRunning  = true;
        _wallRunTimer = WallRunTime;
        _wallNormal   = wallNormal;
        // No momentum bonus — wall run duration is driven by existing momentum
        var rightVec  = new Vector3(Mathf.Cos(_yaw), 0f, -Mathf.Sin(_yaw));
        _wallRollSign = -Mathf.Sign(wallNormal.Dot(rightVec));
    }

    void WallRunTick(ref Vector3 vel, float dt)
    {
        _wallRunTimer -= dt;
        if (_wallRunTimer <= 0f) { EndWallRun(); return; }

        // End immediately if no wall is physically present — no floating in gaps
        if (!HasWallContact()) { EndWallRun(); return; }

        // Wall gravity scales with momentum loss: full momentum = weightless on wall,
        // zero momentum = full WallRunGravity pull. Momentum drains in air ~0.4/s.
        float wallGrav = WallRunGravity * Mathf.Max(0f, 1f - _momentum);
        if (vel.Y > 0f) vel.Y = 0f;
        vel.Y = Mathf.MoveToward(vel.Y, -wallGrav, WallRunGravity * dt);

        // Gradually accelerate along the wall (not instant snap)
        var hVel = new Vector3(vel.X, 0, vel.Z);
        hVel -= _wallNormal * hVel.Dot(_wallNormal);
        float currentSpd = hVel.Length();
        float targetSpd  = Mathf.MoveToward(currentSpd, WallRunSpeed, WallRunAccel * dt);
        if (currentSpd > 0.01f)
            hVel = hVel.Normalized() * Mathf.Max(currentSpd, targetSpd);
        vel.X = hVel.X;
        vel.Z = hVel.Z;

        if (Input.IsActionJustPressed("jump"))
        {
            // 70% look direction, 30% wall normal — goes where you're pointing,
            // with a slight peel-off so you can't clip back into the same wall.
            var lookFwd  = new Vector3(-Mathf.Sin(_yaw), 0f, -Mathf.Cos(_yaw));
            var launchDir = (lookFwd * 0.7f + _wallNormal * 0.3f).Normalized();
            float launchSpd = WallJumpOff * (1f + _momentum * 0.6f);  // momentum-scaled
            vel.X = launchDir.X * launchSpd;
            vel.Z = launchDir.Z * launchSpd;
            vel.Y = WallJumpUp;
            EndWallRun();
            _jumpSfx.Play();
        }
    }

    bool HasWallContact()
    {
        // TestMove asks: "would this body collide if nudged 0.5m toward the wall?"
        // Returns true only when something physical is there — reliable, no ray quirks.
        return TestMove(GlobalTransform, -_wallNormal * 0.5f);
    }

    void EndWallRun()
    {
        _wallRunning  = false;
        _wallCooldown = WallRunCooldown;
        _wallRollSign = 0f;
    }

    // ── Stair step-up ────────────────────────────────────────────────────────
    // When the player walks into a stair riser, teleport the capsule above it
    // so MoveAndSlide can proceed onto the tread. FloorSnapLength then snaps
    // the player back down to the tread surface each frame.
    void TryStepUp(Vector3 vel, float dt)
    {
        if (!IsOnFloor() || _sliding || _wallRunning) return;
        var hv = new Vector3(vel.X, 0f, vel.Z);
        if (hv.LengthSquared() < 0.01f) return;

        var dir  = hv.Normalized();
        float dist = Mathf.Max(hv.Length() * dt + 0.1f, 0.15f);

        // Only act when there's an obstacle in the movement direction
        if (!TestMove(GlobalTransform, dir * dist)) return;

        // Confirm the floor ahead is HIGHER than current floor (ascending step, not descending).
        // ProbeReach must be > capsuleRadius (0.35) so the probe lands inside the stair cell,
        // past the riser face, rather than in the corridor behind the player.
        const float HalfCapsule = 0.8f;
        const float ProbeReach  = 0.55f;
        float currentFloorY = GlobalPosition.Y - HalfCapsule;
        // Skip if the player's floor is lower than last frame — they are descending stairs.
        if (currentFloorY < _prevFloorY - 0.05f) return;
        var spaceState = GetWorld3D().DirectSpaceState;
        var probeFrom  = GlobalPosition + dir * ProbeReach + Vector3.Up  * StairStepHeight;
        var probeTo    = GlobalPosition + dir * ProbeReach - Vector3.Up  * StairStepHeight;
        var probeQ     = PhysicsRayQueryParameters3D.Create(probeFrom, probeTo, CollisionMask);
        probeQ.Exclude = new Godot.Collections.Array<Rid> { GetRid() };
        var probeHit   = spaceState.IntersectRay(probeQ);
        if (probeHit.Count == 0) return; // no floor ahead — genuine wall or void
        float floorAheadY = ((Vector3)probeHit["position"]).Y;
        if (floorAheadY <= currentFloorY + 0.05f) return; // flat or descending — don't step up

        // Skip if no headroom for the step-up
        if (TestMove(GlobalTransform, Vector3.Up * StairStepHeight)) return;
        // Skip if the raised path is still blocked (solid wall, not a step)
        var upXf = GlobalTransform;
        upXf.Origin.Y += StairStepHeight;
        if (TestMove(upXf, dir * dist)) return;

        // Valid ascending step — pop the capsule above the riser
        GlobalPosition = upXf.Origin;
    }

    // ── Stair speed regulation ────────────────────────────────────────────────
    // Going uphill: no speed penalty — momentum carries through at full speed.
    // Adds gravity boost when sliding downhill.
    // Only fires on steep floors (stairs ~51°, so floorNormal.Y ≈ 0.63).
    void AdjustStairVelocity(ref Vector3 vel, float dt)
    {
        if (!IsOnFloor()) return;
        var fn = GetFloorNormal();
        if (fn.Y > 0.85f) return;   // flat floor, skip

        // XZ component of floor normal points downhill
        var downhillXZ = new Vector3(fn.X, 0f, fn.Z);
        if (downhillXZ.LengthSquared() < 0.01f) return;
        downhillXZ = downhillXZ.Normalized();

        var velXZ = new Vector3(vel.X, 0f, vel.Z);
        float slopeSign = velXZ.Dot(downhillXZ); // +ve = going downhill, -ve = going uphill

        if (slopeSign < 0f)
        {
            // Going uphill: no penalty — allow momentum-driven stair climbing at any speed.
            // (No cap applied; let the player carry full sprint momentum up stairs.)
        }
        else if (_sliding)
        {
            // Sliding downhill: gravity assist accelerates the slide
            velXZ += downhillXZ * StairDownBoost * dt;
            vel.X  = velXZ.X;
            vel.Z  = velXZ.Z;
            _slideSp = velXZ.Length();   // sync slide speed so friction tracks correctly
        }
    }

    // ── FP sword procedural animation ─────────────────────────────────────────
    // Axes: X=right, Y=up, Z=back (negative Z = into screen / forward).
    // Idle / Block / Spin are combo-step-independent.
    static readonly Vector3 FpSwordPosIdle  = new( 0.20f, -0.22f, -0.40f);
    static readonly Vector3 FpSwordPosBlock = new(-0.02f, -0.06f, -0.36f);
    static readonly Vector3 FpSwordRotIdle  = new( 0.00f,  0.00f,  0.087f);
    static readonly Vector3 FpSwordRotBlock = new( 1.40f,  0.00f,  0.00f);

    // ── Per-swing FP positions ─────────────────────────────────────────────────
    // Swing 0: forehand right→left   (Sword_Regular_A)
    // Swing 1: backhand left→right   (Sword_Regular_B)
    // Swing 2: overhead arc sweep    (Sword_Regular_C) — wide up-and-around arc
    static readonly Vector3[] FpWindupPos = {
        new( 0.38f,  0.05f, -0.30f),   // 0: chambered right-shoulder
        new(-0.24f,  0.03f, -0.30f),   // 1: chambered left-shoulder
        new( 0.24f,  0.40f, -0.22f),   // 2: raised high-right, loading the arc
    };
    static readonly Vector3[] FpWindupRot = {
        new( 0.12f, -0.62f,  0.42f),   // 0: blade cocked right, edge forward
        new(-0.08f,  0.58f, -0.40f),   // 1: blade cocked left
        new(-0.85f, -0.28f,  0.40f),   // 2: blade up-right, edge leading the sweep
    };

    // Swing 0/1: near-instant snap (lerpRate 50). Swing 2: slower sweep so the arc reads.
    static readonly float[] FpHitLerpRate = { 50f, 50f, 26f };

    static readonly Vector3[] FpHitPos = {
        new(-0.14f, -0.18f, -0.58f),   // 0: swept full left, lunged forward
        new( 0.30f, -0.14f, -0.56f),   // 1: swept full right
        new(-0.22f, -0.08f, -0.70f),   // 2: arc landed left-forward (wide sweep)
    };
    static readonly Vector3[] FpHitRot = {
        new( 0.00f,  0.46f, -0.24f),   // 0: follow-through left
        new( 0.00f, -0.42f,  0.26f),   // 1: follow-through right
        new( 0.18f,  0.55f, -0.30f),   // 2: blade swept through, tip angled left
    };

    // Recovery: slow hang so the momentum bleeds off naturally before returning to idle
    static readonly Vector3[] FpRecoveryPos = {
        new(-0.18f, -0.26f, -0.46f),   // 0: over-extended left
        new( 0.32f, -0.24f, -0.44f),   // 1: over-extended right
        new(-0.26f, -0.28f, -0.52f),   // 2: follow-through left, sword hanging wide
    };
    static readonly Vector3[] FpRecoveryRot = {
        new( 0.00f,  0.20f, -0.08f),
        new( 0.00f, -0.18f,  0.08f),
        new( 0.08f,  0.28f, -0.14f),   // 2: arc completed, blade still swept left
    };

    // Aerial lunge: pulled-back thrust → full forward extension → hang
    static readonly Vector3 FpLungeWindupPos  = new( 0.30f,  0.10f, -0.18f);
    static readonly Vector3 FpLungeWindupRot  = new(-0.18f, -0.08f,  0.14f);
    static readonly Vector3 FpLungeHitPos     = new( 0.00f, -0.08f, -0.92f);
    static readonly Vector3 FpLungeHitRot     = new( 0.10f,  0.04f, -0.04f);
    static readonly Vector3 FpLungeRecovPos   = new( 0.04f, -0.14f, -0.70f);
    static readonly Vector3 FpLungeRecovRot   = new( 0.06f,  0.06f, -0.02f);

    void AnimateFpSword(float dt)
    {
        if (_heldWeapon == null || !HasWeapon) return;

        // Aerial lunge uses its own thrust animation track
        if (_combat.IsAerialLunge)
        {
            (Vector3 pos, Vector3 rot, float rate) = _combat.CombatPhase switch
            {
                SwordCombat.Phase.Windup    => (FpLungeWindupPos, FpLungeWindupRot, 26f),
                SwordCombat.Phase.HitWindow => (FpLungeHitPos,    FpLungeHitRot,    60f),
                _                           => (FpLungeRecovPos,   FpLungeRecovRot,   9f),
            };
            float t2 = Mathf.Clamp(rate * dt, 0f, 1f);
            var sway2 = new Vector3(_swayOffset.X, _swayOffset.Y, 0f);
            _heldWeapon.Position = _heldWeapon.Position.Lerp(pos + sway2, t2);
            _heldWeapon.Rotation = _heldWeapon.Rotation.Lerp(rot, t2);
            return;
        }

        int     step     = Mathf.Clamp(_combat.ComboStep, 0, 2);
        Vector3 targetPos, targetRot;
        float   lerpRate;

        switch (_combat.CombatPhase)
        {
            case SwordCombat.Phase.Windup:
                targetPos = FpWindupPos[step];
                targetRot = FpWindupRot[step];
                lerpRate  = 22f;   // quick chamber
                break;
            case SwordCombat.Phase.HitWindow:
                targetPos = FpHitPos[step];
                targetRot = FpHitRot[step];
                lerpRate  = FpHitLerpRate[step];   // swing 2 slower so the arc reads
                break;
            case SwordCombat.Phase.Recovery:
                targetPos = FpRecoveryPos[step];
                targetRot = FpRecoveryRot[step];
                lerpRate  = 9f;    // slow hang — natural follow-through
                break;
            case SwordCombat.Phase.Block:
                targetPos = FpSwordPosBlock;
                targetRot = FpSwordRotBlock;
                lerpRate  = 20f;
                break;
            default:   // Idle, Cooldown
                targetPos = FpSwordPosIdle;
                targetRot = FpSwordRotIdle;
                lerpRate  = 11f;
                break;
        }

        float t = Mathf.Clamp(lerpRate * dt, 0f, 1f);
        var swayDelta = new Vector3(_swayOffset.X, _swayOffset.Y, 0f);
        _heldWeapon.Position = _heldWeapon.Position.Lerp(targetPos + swayDelta, t);
        _heldWeapon.Rotation = _heldWeapon.Rotation.Lerp(targetRot, t);
    }

    // ── Camera head bob ────────────────────────────────────────────────────────
    void UpdateCameraBob(float dt, float speed, bool onFloor)
    {
        if (onFloor && speed > 0.5f)
        {
            float freq = speed > 7f ? 14f : 9f;
            float ampY = speed > 7f ? 0.030f : 0.016f;
            float ampX = ampY * 0.45f;
            _camBobPhase += dt * freq;
            // Y: heel-strike pattern (always downward — Abs keeps it below the baseline)
            float bobY = -Mathf.Abs(Mathf.Sin(_camBobPhase)) * ampY;
            float bobX =  Mathf.Sin(_camBobPhase * 0.5f) * ampX;
            _camBobOffset = _camBobOffset.Lerp(new Vector3(bobX, bobY, 0f), 16f * dt);
        }
        else
        {
            _camBobOffset = _camBobOffset.Lerp(Vector3.Zero, 8f * dt);
        }
        // Camera sits at eye socket; bob is the only positional offset
        _cam.Position = _camBobOffset;
    }

    // ── Weapon inertia (spring simulation) ────────────────────────────────────
    void UpdateWeaponSway(float dt)
    {
        // Spring force pulls sway back to zero; damping prevents oscillation
        _swayVelocity += (-_swayOffset * SwaySpring - _swayVelocity * SwayDamp) * dt;
        // Mouse input adds an impulse (X=yaw, Y=pitch → invert X so sway opposes motion)
        _swayVelocity += new Vector2(-_frameMouseDelta.X, _frameMouseDelta.Y) * SwayScale;
        _swayOffset   += _swayVelocity * dt;
        _swayOffset    = _swayOffset.Clamp(Vector2.One * -SwayLimit, Vector2.One * SwayLimit);
        _frameMouseDelta = Vector2.Zero;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    void ApplyLook()
    {
        _mount.Rotation = new(0f, _yaw, 0f);
        _cam.Rotation   = new(_pitch, 0f, _camRoll);
    }

    public void PickupWeapon()
    {
        HasWeapon = true;
        if (_tpMode)
        {
            if (_tpWeapon   != null) _tpWeapon.Visible   = true;
        }
        else
        {
            if (_heldWeapon != null) _heldWeapon.Visible = true;
        }
    }

    // ── Sword tiers ────────────────────────────────────────────────────────────
    // Players spawn with the weaker sword (half damage, no lifesteal).
    // Grabbing the arena pickup upgrades them to the middle sword (full damage + 10% lifesteal)
    // and briefly grants a wallhack outline of the opponent.

    const float WallhackDuration = 10f;

    MeshInstance3D? _wallhackOutline;
    float           _wallhackTimer = 0f;

    public void EquipWeakerSword()
    {
        PickupWeapon();
        _combat.DamageMultiplier = 0.5f;
        _combat.Lifesteal        = 0f;
        _aerialLungePow          = 0.5f;
    }

    public void EquipMiddleSword()
    {
        PickupWeapon();
        _combat.DamageMultiplier = 1.0f;
        _combat.Lifesteal        = 0.10f;
        _aerialLungePow          = 1.0f;
        SwapHeldSwordMesh(ZweihanderPath);
        TriggerOpponentWallhack();
    }

    void TriggerOpponentWallhack()
    {
        // Find the opposing player in the scene (the only PlayerController that isn't us)
        var scene = GetTree().CurrentScene;
        if (scene == null) return;
        foreach (var node in GetTree().GetNodesInGroup("players"))
        {
            if (node is PlayerController pc && pc != this)
                pc.ShowWallhackOutline(WallhackDuration);
        }
    }

    /// Makes this player's body visible through walls for `seconds`.
    /// Called on the OPPONENT when the local player grabs the middle sword.
    public void ShowWallhackOutline(float seconds)
    {
        if (_wallhackOutline == null)
        {
            var mat = new StandardMaterial3D
            {
                AlbedoColor  = new Color(1.0f, 0.25f, 0.25f, 0.85f),
                ShadingMode  = BaseMaterial3D.ShadingModeEnum.Unshaded,
                NoDepthTest  = true,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            };
            _wallhackOutline = new MeshInstance3D
            {
                Name             = "WallhackOutline",
                Mesh             = new CapsuleMesh { Radius = 0.45f, Height = 1.9f },
                MaterialOverride = mat,
                Position         = new Vector3(0f, 0.95f, 0f),
                Layers           = 1u,
            };
            AddChild(_wallhackOutline);
        }
        _wallhackOutline.Visible = true;
        _wallhackTimer = seconds;
    }

    void TickWallhack(float dt)
    {
        if (_wallhackOutline == null || !_wallhackOutline.Visible) return;
        _wallhackTimer -= dt;
        if (_wallhackTimer <= 0f) _wallhackOutline.Visible = false;
    }

    public void ToggleCameraMode()
    {
        _tpMode = !_tpMode;
        // _playerBody is always visible — real model provides FP body awareness
        if (_tpMode)
        {
            _tpCam.MakeCurrent();
            if (_heldWeapon  != null) _heldWeapon.Visible  = false;
            if (_tpWeapon    != null) _tpWeapon.Visible    = HasWeapon;
            if (_fpArmsModel != null) _fpArmsModel.Visible = false;
        }
        else
        {
            _cam.MakeCurrent();
            if (_heldWeapon  != null) _heldWeapon.Visible  = HasWeapon;
            if (_tpWeapon    != null) _tpWeapon.Visible    = false;
            if (_fpArmsModel != null) _fpArmsModel.Visible = true;
        }
    }

    // ── Model config ──────────────────────────────────────────────────────────
    // UAL1 mannequin is used as the character — its skeleton perfectly matches its own
    // animations, so no retargeting mismatch (armored_guard had rest-pose orientation issues).
    const string ModelPath    = "res://playerModels/animations/Universal Animation Library[Standard]/Unreal-Godot/UAL1_Standard.glb";
    const string FpArmsPath   = "res://playerModels/UAL1_FpArms.glb";
    const string SwordGlbPath    = "res://assets/all_dmc2_swords_remade.glb";
    const string ArenaSword      = "plane_001_merciless";   // starting (weaker) sword
    const string ZweihanderPath  = "res://assets/zweihander.glb";  // middle sword
    const float  ModelScale   = 1.0f;
    const float  ModelYOff    = -0.9f;

    // ── FP arms model (camera-space, layer 3) ─────────────────────────────────
    // Requires playerModels/UAL1_FpArms.glb — exported from Blender by deleting
    // all non-arm geometry from UAL1_Standard.glb (keep forearms + hands only).
    //
    // Y offset math: eye socket = ModelYOff + EyeHeight above model root.
    // To put eye at camera origin: model Y = -(EyeHeight + |ModelYOff|) = -1.64m.
    Node3D? BuildFpArmsModel()
    {
        if (!ResourceLoader.Exists(FpArmsPath)) return null;
        var scene = GD.Load<PackedScene>(FpArmsPath);
        if (scene == null) return null;
        var model = scene.Instantiate<Node3D>();
        model.Name            = "FpArmsModel";
        model.Scale           = Vector3.One * ModelScale;
        model.Position        = new Vector3(0f, ModelYOff - EyeHeight, 0f);   // -1.64m
        model.RotationDegrees = new Vector3(0f, 180f, 0f);
        SetLayersRecursive(model, 4u);   // layer 3 — FP camera only
        GD.Print("[Player] FpArms model loaded");
        return model;
    }

    Node3D BuildPlayerBody()
    {
        var root = new Node3D { Name = "PlayerBody", Visible = false };

        var scene = GD.Load<PackedScene>(ModelPath);
        if (scene != null)
        {
            var model = scene.Instantiate<Node3D>();
            model.Name              = "CharacterModel";
            model.Scale             = Vector3.One * ModelScale;
            model.Position          = new Vector3(0f, ModelYOff, 0f);
            model.RotationDegrees   = new Vector3(0f, 180f, 0f);
            root.AddChild(model);

            // Attach sword to the right-hand bone
            var skel = FindFirst<Skeleton3D>(model);
            GD.Print($"[Player] Skeleton found: {skel != null}");
            if (skel != null)
            {
                // Try several common hand bone name variants
                string handBone = "";
                foreach (var candidate in new[] { "RightHand", "hand_r", "Hand_R", "R_Hand", "mixamorig:RightHand" })
                {
                    if (skel.FindBone(candidate) >= 0) { handBone = candidate; break; }
                }
                // If still not found, pick any bone whose name contains "hand" (case-insensitive)
                if (handBone.Length == 0)
                {
                    for (int i = 0; i < skel.GetBoneCount(); i++)
                    {
                        var bn = skel.GetBoneName(i);
                        if (bn.ToLowerInvariant().Contains("hand") && !bn.ToLowerInvariant().Contains("left"))
                        { handBone = bn; break; }
                    }
                }
                GD.Print($"[Player] Hand bone: '{handBone}' (of {skel.GetBoneCount()} bones)");

                if (handBone.Length > 0)
                {
                    var attach = new BoneAttachment3D { Name = "SwordAttach", BoneName = handBone };
                    skel.AddChild(attach);

                    _tpWeapon = LoadSwordMesh(SwordGlbPath, ArenaSword);
                    if (_tpWeapon == null)
                        _tpWeapon = MakePlaceholderSword();
                    _tpWeapon.Visible = false;
                    // Auto-scale the sword to ~0.9m in its longest axis
                    if (_tpWeapon.Mesh != null)
                    {
                        var aabb   = _tpWeapon.Mesh.GetAabb();
                        float maxD = Mathf.Max(aabb.Size.X, Mathf.Max(aabb.Size.Y, aabb.Size.Z));
                        _tpWeapon.Scale = maxD > 0.01f ? Vector3.One * (0.9f / maxD) : Vector3.One * 0.01f;
                    }
                    else
                    {
                        _tpWeapon.Scale = Vector3.One;
                    }
                    _tpWeapon.RotationDegrees = new Vector3(0f, 180f, -90f);
                    _tpWeapon.Position        = Vector3.Zero;
                    attach.AddChild(_tpWeapon);
                    GD.Print($"[Player] Sword attached to bone '{handBone}', scale={_tpWeapon.Scale}");
                }
                else
                {
                    GD.PrintErr("[Player] No hand bone found — using floating placeholder");
                    _tpWeapon = MakePlaceholderSword();
                    _tpWeapon.Position = new(0.32f, 0.80f, -0.15f);
                    _tpWeapon.Visible  = false;
                    root.AddChild(_tpWeapon);
                }
            }
            else
            {
                GD.PrintErr("[Player] No Skeleton3D found in model");
                _tpWeapon = MakePlaceholderSword();
                _tpWeapon.Position = new(0.32f, 0.80f, -0.15f);
                _tpWeapon.Visible  = false;
                root.AddChild(_tpWeapon);
            }
        }
        else
        {
            // Fallback capsule body when GLB is missing
            var mat = new StandardMaterial3D { AlbedoColor = new(0.25f, 0.30f, 0.45f), Roughness = 0.8f };
            root.AddChild(new MeshInstance3D
            {
                Mesh     = new CapsuleMesh { Radius = 0.28f, Height = 0.80f },
                Position = new(0f, 0.75f, 0f)
            });
            root.GetChild<MeshInstance3D>(0).SetSurfaceOverrideMaterial(0, mat);
            _tpWeapon = MakePlaceholderSword();
            _tpWeapon.Position = new(0.32f, 0.80f, -0.15f);
            _tpWeapon.Visible  = false;
            root.AddChild(_tpWeapon);
        }

        return root;
    }

    // ── Asset helpers ─────────────────────────────────────────────────────────
    static T? FindFirst<T>(Node node) where T : Node
    {
        if (node is T t) return t;
        foreach (Node child in node.GetChildren())
        {
            var found = FindFirst<T>(child);
            if (found != null) return found;
        }
        return null;
    }

    static T? FindFirst<T>(Node node, string name) where T : Node
    {
        if (node is T t && node.Name == name) return t;
        foreach (Node child in node.GetChildren())
        {
            var found = FindFirst<T>(child, name);
            if (found != null) return found;
        }
        return null;
    }

    static MeshInstance3D? LoadSwordMesh(string glbPath, string meshName)
    {
        var packed = GD.Load<PackedScene>(glbPath);
        if (packed == null) return null;
        var tempRoot = packed.Instantiate<Node3D>();
        var src = FindFirst<MeshInstance3D>(tempRoot, meshName)
               ?? FindFirst<MeshInstance3D>(tempRoot);
        if (src == null) { tempRoot.Free(); return null; }
        var inst = new MeshInstance3D { Name = "TpWeapon", Mesh = src.Mesh };
        tempRoot.Free();
        return inst;
    }

    static void SetLayersRecursive(Node node, uint layer)
    {
        if (node is VisualInstance3D vi) vi.Layers = layer;
        foreach (Node child in node.GetChildren())
            SetLayersRecursive(child, layer);
    }

    static MeshInstance3D MakePlaceholderSword()
    {
        var m = new MeshInstance3D
        {
            Name = "TpWeapon",
            Mesh = new BoxMesh { Size = new(0.05f, 0.70f, 0.05f) },
        };
        m.SetSurfaceOverrideMaterial(0,
            new StandardMaterial3D { AlbedoColor = new(0.6f, 0.6f, 0.65f), Metallic = 0.9f, Roughness = 0.25f });
        return m;
    }

    // Swap both the FP inner mesh and the TP bone-attached mesh to a new GLB.
    // Called by EquipMiddleSword() to make the held sword visually change on pickup.
    void SwapHeldSwordMesh(string glbPath)
    {
        // Extract just the Mesh resource from the GLB (first MeshInstance3D found)
        Mesh? newMesh = null;
        var packed = GD.Load<PackedScene>(glbPath);
        if (packed != null)
        {
            var tempRoot = packed.Instantiate<Node3D>();
            var src      = FindFirst<MeshInstance3D>(tempRoot);
            newMesh      = src?.Mesh;
            tempRoot.Free();
        }
        if (newMesh == null)
        {
            GD.PrintErr($"[Player] SwapHeldSwordMesh: no mesh found in {glbPath}");
            return;
        }

        // Zweihander blade runs along X; rotate 90° Y so it faces forward (-Z).
        // Starter/DMC swords run along Z; existing 180° Y flip was correct for those.
        bool isZwei = glbPath == ZweihanderPath;

        // ── FP sword — replace inner "HeldWeaponMesh" child ──────────────────
        if (_heldWeapon != null)
        {
            var fpInner = _heldWeapon.GetNodeOrNull<MeshInstance3D>("HeldWeaponMesh");
            if (fpInner != null)
            {
                fpInner.Mesh = newMesh;
                var aabb  = newMesh.GetAabb();
                float maxD = Mathf.Max(aabb.Size.X, Mathf.Max(aabb.Size.Y, aabb.Size.Z));
                fpInner.Scale           = maxD > 0.01f ? Vector3.One * (0.75f / maxD) : Vector3.One;
                fpInner.RotationDegrees = isZwei ? new(0f, 90f, 0f) : new(0f, 180f, 0f);
            }
        }

        // ── TP sword — swap Mesh on the bone-attached MeshInstance3D ─────────
        if (_tpWeapon != null)
        {
            _tpWeapon.Mesh = newMesh;
            var aabb  = newMesh.GetAabb();
            float maxD = Mathf.Max(aabb.Size.X, Mathf.Max(aabb.Size.Y, aabb.Size.Z));
            _tpWeapon.Scale           = maxD > 0.01f ? Vector3.One * (0.9f / maxD) : Vector3.One;
            _tpWeapon.RotationDegrees = isZwei ? new(0f, 90f, -90f) : new(0f, 180f, -90f);
        }
    }

    // Wrapper Node3D: animation lerps Position/Rotation on the wrapper, while the inner
    // MeshInstance3D carries a 180° Y rotation so the blade points forward (away from
    // the camera) instead of backward toward the player.
    Node3D BuildFpSword()
    {
        var holder = new Node3D
        {
            Name            = "HeldWeapon",
            Position        = new(0.20f, -0.22f, -0.40f),
            Visible         = false,
        };
        holder.RotationDegrees = new(0f, 0f, 5f);

        var inst = LoadSwordMesh(SwordGlbPath, ArenaSword);
        if (inst != null)
        {
            inst.Name = "HeldWeaponMesh";
            if (inst.Mesh != null)
            {
                var aabb   = inst.Mesh.GetAabb();
                float maxD = Mathf.Max(aabb.Size.X, Mathf.Max(aabb.Size.Y, aabb.Size.Z));
                inst.Scale = maxD > 0.01f ? Vector3.One * (0.75f / maxD) : Vector3.One * 0.01f;
            }
            inst.RotationDegrees = new(0f, 180f, 0f);   // flip: blade points forward
            inst.Layers          = 4u;
            holder.AddChild(inst);
            return holder;
        }

        var box = new MeshInstance3D
        {
            Name            = "HeldWeaponMesh",
            Mesh            = new BoxMesh { Size = new(0.06f, 0.72f, 0.06f) },
            RotationDegrees = new(0f, 180f, 0f),
        };
        box.SetSurfaceOverrideMaterial(0,
            new StandardMaterial3D { AlbedoColor = new(0.65f, 0.65f, 0.70f), Metallic = 0.85f, Roughness = 0.3f });
        box.Layers = 4u;
        holder.AddChild(box);
        return holder;
    }

    // ── FP hands: camera-space arms at bottom screen corners ─────────────────
    // Parented to the camera, so they can never clip through world geometry.
    // Two forearm/gauntlet shapes sit at ±X, pushed down and slightly forward.
    Node3D BuildFpHands()
    {
        var root = new Node3D { Name = "FpHands" };

        var armMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.09f, 0.09f, 0.10f),
            Metallic    = 0.80f,
            Roughness   = 0.35f,
        };
        var gauntletMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.20f, 0.18f, 0.16f),
            Metallic    = 0.55f,
            Roughness   = 0.60f,
        };

        // Right arm — lower-right corner
        var rFore = new MeshInstance3D
        {
            Mesh            = new CapsuleMesh { Radius = 0.043f, Height = 0.32f },
            Position        = new Vector3( 0.29f, -0.46f, -0.38f),
            RotationDegrees = new Vector3( 25f, -12f,  22f),
        };
        rFore.SetSurfaceOverrideMaterial(0, armMat);
        var rKnuckle = new MeshInstance3D
        {
            Mesh     = new BoxMesh { Size = new Vector3(0.08f, 0.06f, 0.10f) },
            Position = new Vector3( 0.27f, -0.34f, -0.44f),
        };
        rKnuckle.SetSurfaceOverrideMaterial(0, gauntletMat);
        root.AddChild(rFore);
        root.AddChild(rKnuckle);

        // Left arm — lower-left corner
        var lFore = new MeshInstance3D
        {
            Mesh            = new CapsuleMesh { Radius = 0.043f, Height = 0.32f },
            Position        = new Vector3(-0.29f, -0.46f, -0.38f),
            RotationDegrees = new Vector3( 25f,  12f, -22f),
        };
        lFore.SetSurfaceOverrideMaterial(0, armMat);
        var lKnuckle = new MeshInstance3D
        {
            Mesh     = new BoxMesh { Size = new Vector3(0.08f, 0.06f, 0.10f) },
            Position = new Vector3(-0.27f, -0.34f, -0.44f),
        };
        lKnuckle.SetSurfaceOverrideMaterial(0, gauntletMat);
        root.AddChild(lFore);
        root.AddChild(lKnuckle);

        return root;
    }

    // ── FP legs: camera-space boots at screen bottom, shown only while sliding ─
    Node3D BuildFpLegs()
    {
        var root = new Node3D { Name = "FpLegs", Visible = false };

        var trouserMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.14f, 0.14f, 0.18f),
            Roughness   = 0.85f,
        };
        var bootMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.08f, 0.08f, 0.10f),
            Metallic    = 0.50f,
            Roughness   = 0.55f,
        };

        foreach (var side in new[] { 1f, -1f })
        {
            float x = side * 0.16f;

            // Shin/lower leg — angled forward as if extended during slide
            var shin = new MeshInstance3D
            {
                Mesh            = new CapsuleMesh { Radius = 0.055f, Height = 0.34f },
                Position        = new Vector3(x, -0.52f, -0.46f),
                RotationDegrees = new Vector3(35f, 0f, side * 6f),
            };
            shin.SetSurfaceOverrideMaterial(0, trouserMat);
            root.AddChild(shin);

            // Boot — flat box at the foot
            var boot = new MeshInstance3D
            {
                Mesh     = new BoxMesh { Size = new Vector3(0.10f, 0.07f, 0.24f) },
                Position = new Vector3(x, -0.66f, -0.36f),
            };
            boot.SetSurfaceOverrideMaterial(0, bootMat);
            root.AddChild(boot);
        }

        return root;
    }

    Node3D BuildFpArms()
    {
        var armMat = new StandardMaterial3D
        {
            AlbedoColor = new(0.10f, 0.10f, 0.11f),
            Metallic    = 0.75f,
            Roughness   = 0.45f,
        };
        var gauntletMat = new StandardMaterial3D
        {
            AlbedoColor = new(0.22f, 0.20f, 0.18f),
            Metallic    = 0.6f,
            Roughness   = 0.55f,
        };

        var root = new Node3D { Name = "FpArms", Visible = false };

        // Right forearm — grips the sword
        var rFore = new MeshInstance3D
        {
            Mesh             = new CapsuleMesh { Radius = 0.042f, Height = 0.36f },
            Position         = new(0.30f, -0.52f, -0.32f),
            RotationDegrees  = new(20f, -15f, 18f),
        };
        rFore.SetSurfaceOverrideMaterial(0, armMat);
        root.AddChild(rFore);

        var rHand = new MeshInstance3D
        {
            Mesh     = new SphereMesh { Radius = 0.046f, RadialSegments = 8, Rings = 6 },
            Position = new(0.24f, -0.34f, -0.44f),
        };
        rHand.SetSurfaceOverrideMaterial(0, gauntletMat);
        root.AddChild(rHand);

        // Left forearm — supporting hand lower and further away
        var lFore = new MeshInstance3D
        {
            Mesh            = new CapsuleMesh { Radius = 0.040f, Height = 0.33f },
            Position        = new(-0.18f, -0.60f, -0.38f),
            RotationDegrees = new(15f, 18f, -16f),
        };
        lFore.SetSurfaceOverrideMaterial(0, armMat);
        root.AddChild(lFore);

        var lHand = new MeshInstance3D
        {
            Mesh     = new SphereMesh { Radius = 0.042f, RadialSegments = 8, Rings = 6 },
            Position = new(-0.14f, -0.45f, -0.46f),
        };
        lHand.SetSurfaceOverrideMaterial(0, gauntletMat);
        root.AddChild(lHand);

        return root;
    }

    public void TriggerDeath()
    {
        if (_isDead) return;
        _isDead = true;
        _animator.PlayDeath();
    }

    public void ReleaseMouse() => Input.MouseMode = Input.MouseModeEnum.Visible;
    public Camera3D Camera    => _cam;
}
