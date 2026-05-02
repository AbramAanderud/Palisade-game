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
    const float WalkSpeed      = 6.0f;
    const float SprintSpeed    = 11.0f;
    const float CrouchSpeed    = 2.5f;      // prone crouch walk

    const float AirAccel       = 18f;
    const float Gravity        = 22f;
    const float JumpSpeed      = 9.0f;
    const float JumpFwdBoost   = 3.5f;     // extra horizontal speed channeled forward on jump
    const float CoyoteTime     = 0.12f;

    // ── Slide constants ────────────────────────────────────────────────────────
    const float SlideBoost     = 20.0f;     // entry burst speed
    const float SlideGrace     = 0.18f;     // seconds of no-friction at slide start (burst feel)
    const float SlideFriction  = 6.5f;      // deceleration m/s² after grace period
    const float SlideMinSpeed  = 3.0f;      // slide ends below this
    const float SlideJumpH     = 11.0f;
    const float SlideJumpV     = 10.5f;
    const float LandSlideMin   = 8.0f;      // vertical speed on landing to trigger auto-slide

    // ── Camera heights ─────────────────────────────────────────────────────────
    const float EyeHeight      = 1.45f;
    const float CrouchEyeH    = 0.90f;     // crouch walk
    const float SlideEyeH     = 0.50f;     // full slide

    // ── Wall-run constants ─────────────────────────────────────────────────────
    const float WallRunSpeed   = 10.0f;
    const float WallRunAccel   = 7.0f;     // m/s² to ramp up to wall-run speed (60% slower than instant)
    const float WallRunTime    = 2.0f;
    const float WallRunGravity = 3.0f;
    const float WallJumpOff    = 8.0f;
    const float WallJumpUp     = 11.0f;
    const float WallRunCooldown= 0.4f;

    const float MouseSens      = 0.002f;
    const float PitchClamp     = 85f * Mathf.Pi / 180f;

    // ── Nodes ─────────────────────────────────────────────────────────────────
    Camera3D        _cam   = null!;
    Node3D          _mount = null!;
    MeshInstance3D? _heldWeapon;

    // ── Look ──────────────────────────────────────────────────────────────────
    float _yaw   = 0f;
    float _pitch = 0f;

    // ── Slide state ───────────────────────────────────────────────────────────
    bool    _sliding        = false;
    Vector3 _slideDir       = Vector3.Zero;
    float   _slideSp        = 0f;
    float   _slideGraceLeft = 0f; // no-friction burst window at slide start

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
    float _airFallSpeed = 0f;   // track downward speed while airborne

    // ── Sprint grace window ────────────────────────────────────────────────────
    const float SprintGrace    = 0.45f;  // seconds after releasing sprint where slide still works
    float _sprintGraceTimer    = 0f;

    // ── Stair speed regulation ─────────────────────────────────────────────────
    const float StairDownBoost = 10f;    // m/s² gravity assist going down stairs

    // ── Camera smoothing ──────────────────────────────────────────────────────
    float _camH    = EyeHeight;
    float _camRoll = 0f;

    // ── Third-person camera ────────────────────────────────────────────────────
    const float TpDistance      = 4.0f;
    const float TpDistanceBlock = 3.5f;
    const float TpElevation     = 1.8f;
    const float TpElevationSlide= 1.0f;
    bool        _tpMode         = false;
    Camera3D    _tpCam          = null!;
    Node3D      _playerBody     = null!;
    float       _tpDistCurrent  = 5.0f;
    float       _tpElevCurrent  = 2.5f;

    // ── Hold-R reset ──────────────────────────────────────────────────────────
    float _resetHoldTime = 0f;
    const float ResetHoldThreshold = 0.5f;

    // ── Animator ──────────────────────────────────────────────────────────────
    PlayerAnimator _animator = null!;

    // ── Combat ────────────────────────────────────────────────────────────────
    SwordCombat _combat = null!;

    // ── Public ────────────────────────────────────────────────────────────────
    public bool HasWeapon { get; private set; } = false;

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
        var shape = new CapsuleShape3D { Radius = 0.35f, Height = 1.6f };
        AddChild(new CollisionShape3D { Shape = shape, Name = "Body" });
        CollisionMask  = 0xFFFFFFFF;
        MotionMode     = MotionModeEnum.Grounded;
        UpDirection    = Vector3.Up;
        FloorMaxAngle  = 55f * Mathf.Pi / 180f;  // allow climbing stair ramps (~51°)

        _mount = new Node3D { Name = "CamMount", Position = new(0, EyeHeight, 0) };
        AddChild(_mount);
        _cam = new Camera3D { Name = "PlayerCam", Fov = 90f, Near = 0.05f, Far = 500f };
        _mount.AddChild(_cam);

        _heldWeapon = new MeshInstance3D
        {
            Name     = "HeldWeapon",
            Mesh     = new BoxMesh { Size = new(0.06f, 0.72f, 0.06f) },
            Position = new(0.28f, -0.20f, -0.45f),
            Visible  = false,
        };
        var wMat = new StandardMaterial3D
            { AlbedoColor = new(0.65f, 0.65f, 0.70f), Metallic = 0.85f, Roughness = 0.3f };
        _heldWeapon.SetSurfaceOverrideMaterial(0, wMat);
        _cam.AddChild(_heldWeapon);

        _playerBody = BuildPlayerBody();
        AddChild(_playerBody);

        _animator = new PlayerAnimator();
        AddChild(_animator);
        var modelRoot = _playerBody.GetNodeOrNull<Node3D>("RascalModel");
        if (modelRoot != null) _animator.Init(modelRoot);

        _tpCam = new Camera3D { Name = "TpCam", Fov = 75f, Near = 0.1f, Far = 500f };
        AddChild(_tpCam);

        _combat = new SwordCombat();
        AddChild(_combat);

        ApplyLook();
        _cam.MakeCurrent();
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public override void _Input(InputEvent ev)
    {
        if (ev is InputEventMouseMotion mm && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            _yaw   -= mm.Relative.X * MouseSens;
            _pitch  = Mathf.Clamp(_pitch - mm.Relative.Y * MouseSens, -PitchClamp, PitchClamp);
            ApplyLook();
        }
        if (ev.IsActionPressed("pause"))
            Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Captured
                ? Input.MouseModeEnum.Visible
                : Input.MouseModeEnum.Captured;
        if (ev.IsActionPressed("toggle_camera") && !(ev is InputEventKey ek && ek.Echo))
            ToggleCameraMode();
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt      = (float)delta;
        bool  onFloor = IsOnFloor();

        // ── Coyote time ───────────────────────────────────────────────────────
        if (onFloor) { _coyote = CoyoteTime; _wasOnFloor = true; }
        else         { _coyote = Mathf.MoveToward(_coyote, 0f, dt); }

        // ── Track fall speed for landing-slide ────────────────────────────────
        if (!onFloor && Velocity.Y < 0f)
            _airFallSpeed = Mathf.Abs(Velocity.Y);

        // ── Wall-run cooldown ─────────────────────────────────────────────────
        if (_wallCooldown > 0) _wallCooldown -= dt;

        // ── Landing-slide trigger ─────────────────────────────────────────────
        // Fire when we just hit the ground after falling fast while moving.
        if (onFloor && !_wasOnFloor && _airFallSpeed >= LandSlideMin && !_sliding)
        {
            var hv = new Vector3(Velocity.X, 0, Velocity.Z);
            if (hv.Length() > 1f)
            {
                StartSlide(hv, Mathf.Max(hv.Length(), SlideBoost));
            }
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

        Velocity = vel;
        MoveAndSlide();

        if (!IsOnFloor() && !_wallRunning && _wallCooldown <= 0)
            TryStartWallRun();

        // ── Sword combat ──────────────────────────────────────────────────────
        if (HasWeapon)
        {
            _combat.Tick(dt, !IsOnFloor(), _wallRunning, _yaw, out float yawDelta);
            _yaw += yawDelta;
            ApplyLook();
        }

        // ── Smooth camera height ───────────────────────────────────────────────
        float targetH = _sliding ? SlideEyeH : _crouching ? CrouchEyeH : EyeHeight;
        _camH = Mathf.MoveToward(_camH, targetH, 6f * dt);
        _mount.Position = new(0f, _camH, 0f);

        // ── Camera roll during wall-run ────────────────────────────────────────
        float targetRoll = _wallRunning ? _wallRollSign * 12f * Mathf.Pi / 180f : 0f;
        _camRoll = Mathf.Lerp(_camRoll, targetRoll, 10f * dt);
        _cam.Rotation = new(_pitch, 0f, _camRoll);

        // ── Animate player body ────────────────────────────────────────────────
        var hSpd = new Vector2(Velocity.X, Velocity.Z).Length();
        _animator.Tick(dt, IsOnFloor(), _sliding, _wallRunning, HasWeapon, hSpd, Velocity.Y);

        // ── FP FOV swell on sprint ─────────────────────────────────────────────
        bool isSpr = Input.IsActionPressed("sprint") && new Vector2(Velocity.X, Velocity.Z).Length() > WalkSpeed;
        float targetFov = isSpr ? 98f : 90f;
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

        // Sprint grace: keep timer alive while sprinting, decay after release.
        // This lets the player tap Ctrl briefly after letting go of Shift and still slide.
        if (isSprinting)
            _sprintGraceTimer = SprintGrace;
        else
            _sprintGraceTimer = Mathf.MoveToward(_sprintGraceTimer, 0f, dt);

        bool canSlide = isSprinting || _sprintGraceTimer > 0f;

        float speed = isSprinting ? SprintSpeed
            : slideHeld ? CrouchSpeed
            : (HasWeapon && _combat.IsBlocking) ? CrouchSpeed
            : WalkSpeed;

        // Crouch state: Ctrl while standing still or slow walk (not enough speed to slide)
        var hVelNow = new Vector3(vel.X, 0, vel.Z);
        bool hasMovement = hVelNow.Length() > 1.5f;
        _crouching = slideHeld && !hasMovement && !_sliding;

        var fwd   = new Vector3(-Mathf.Sin(_yaw), 0f, -Mathf.Cos(_yaw));
        var right = new Vector3( Mathf.Cos(_yaw), 0f, -Mathf.Sin(_yaw));
        var wish  = (fwd * (-input.Y) + right * input.X).Normalized();

        if (onFloor)
        {
            vel.X = wish.X * speed;
            vel.Z = wish.Z * speed;
        }
        else
        {
            var hVel = new Vector3(vel.X, 0, vel.Z);
            hVel += wish * AirAccel * dt;
            float cap = SprintSpeed * 3f;
            if (hVel.Length() > cap) hVel = hVel.Normalized() * cap;
            vel.X = hVel.X;
            vel.Z = hVel.Z;
        }

        // Jump — channels momentum forward in the direction of travel
        if (Input.IsActionJustPressed("jump") && _coyote > 0f)
        {
            vel.Y   = JumpSpeed;
            _coyote = 0f;
            // Boost horizontal velocity in movement direction so the jump lunges forward
            var hv = new Vector3(vel.X, 0f, vel.Z);
            if (hv.LengthSquared() > 0.01f)
            {
                hv += hv.Normalized() * JumpFwdBoost;
                vel.X = hv.X;
                vel.Z = hv.Z;
            }
        }

        // Start slide: Ctrl pressed while moving (any speed).
        // If sprinting or in grace window: full SlideBoost burst.
        // If just walking: slide at current speed (no extra boost, just the dive).
        if (Input.IsActionJustPressed("slide") && onFloor && !(HasWeapon && _combat.IsBlocking))
        {
            var hVel = new Vector3(vel.X, 0, vel.Z);
            if (hVel.Length() > 1f)
            {
                float entrySpeed = canSlide
                    ? Mathf.Max(hVel.Length(), SlideBoost)  // sprint → full burst
                    : hVel.Length();                          // walk  → momentum dive
                _sprintGraceTimer = 0f;
                StartSlide(hVel, entrySpeed);
            }
        }
    }

    // ── Slide ─────────────────────────────────────────────────────────────────
    void StartSlide(Vector3 hVel, float speed)
    {
        _sliding        = true;
        _crouching      = false;
        _slideDir       = hVel.Normalized();
        _slideSp        = speed;
        _slideGraceLeft = SlideGrace;   // brief no-friction burst window
    }

    void SlideTick(ref Vector3 vel, float dt, bool onFloor)
    {
        if (!onFloor)                       { EndSlide(ref vel); return; }
        if (!Input.IsActionPressed("slide")) { EndSlide(ref vel); return; }

        // Grace period: hold peak speed for a moment before friction bites in
        if (_slideGraceLeft > 0f)
            _slideGraceLeft -= dt;
        else
            _slideSp = Mathf.MoveToward(_slideSp, 0f, SlideFriction * dt);

        if (_slideSp < SlideMinSpeed)       { EndSlide(ref vel); return; }

        vel.X = _slideDir.X * _slideSp;
        vel.Z = _slideDir.Z * _slideSp;
        vel.Y = -0.5f;

        if (Input.IsActionJustPressed("jump"))
        {
            float launchSp = Mathf.Max(_slideSp, SlideJumpH);
            vel.X    = _slideDir.X * launchSp;
            vel.Z    = _slideDir.Z * launchSp;
            vel.Y    = SlideJumpV;
            _sliding = false;
        }
    }

    void EndSlide(ref Vector3 vel) => _sliding = false;

    // ── Wall-run ──────────────────────────────────────────────────────────────
    void TryStartWallRun()
    {
        var hVel = new Vector3(Velocity.X, 0, Velocity.Z);
        if (hVel.Length() < 3f) return;

        for (int i = 0; i < GetSlideCollisionCount(); i++)
        {
            var col = GetSlideCollision(i);
            Vector3 n = col.GetNormal();
            if (Mathf.Abs(n.Y) > 0.3f) continue;

            float alongWall = hVel.Dot(new Vector3(-n.Z, 0f, n.X));
            if (Mathf.Abs(alongWall) < 2f) continue;

            _wallRunning  = true;
            _wallRunTimer = WallRunTime;
            _wallNormal   = n;

            var rightVec  = new Vector3(Mathf.Cos(_yaw), 0f, -Mathf.Sin(_yaw));
            _wallRollSign = -Mathf.Sign(n.Dot(rightVec));
            return;
        }
    }

    void WallRunTick(ref Vector3 vel, float dt)
    {
        _wallRunTimer -= dt;
        if (_wallRunTimer <= 0f) { EndWallRun(); return; }

        vel.Y = Mathf.MoveToward(vel.Y, -WallRunGravity, WallRunGravity * dt);

        var hVel = new Vector3(vel.X, 0, vel.Z);
        hVel -= _wallNormal * hVel.Dot(_wallNormal);
        // Ramp up to wall-run speed gradually (WallRunAccel m/s²) rather than instantly
        float currentSpd = hVel.Length();
        float targetSpd  = Mathf.MoveToward(currentSpd, WallRunSpeed, WallRunAccel * dt);
        if (currentSpd > 0.01f)
            hVel = hVel.Normalized() * Mathf.Max(currentSpd, targetSpd);
        vel.X = hVel.X;
        vel.Z = hVel.Z;

        if (Input.IsActionJustPressed("jump"))
        {
            vel   = _wallNormal * WallJumpOff;
            vel.Y = WallJumpUp;
            EndWallRun();
        }
    }

    void EndWallRun()
    {
        _wallRunning  = false;
        _wallCooldown = WallRunCooldown;
        _wallRollSign = 0f;
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
            var tpW = _playerBody.GetNodeOrNull<MeshInstance3D>("TpWeapon");
            if (tpW != null) tpW.Visible = true;
        }
        else
        {
            if (_heldWeapon != null) _heldWeapon.Visible = true;
        }
    }

    public void ToggleCameraMode()
    {
        _tpMode = !_tpMode;
        _playerBody.Visible = _tpMode;
        if (_tpMode)
        {
            _tpCam.MakeCurrent();
            if (_heldWeapon != null) _heldWeapon.Visible = false;
            var tpW = _playerBody.GetNodeOrNull<MeshInstance3D>("TpWeapon");
            if (tpW != null) tpW.Visible = HasWeapon;
        }
        else
        {
            _cam.MakeCurrent();
            if (_heldWeapon != null) _heldWeapon.Visible = HasWeapon;
            var tpW = _playerBody.GetNodeOrNull<MeshInstance3D>("TpWeapon");
            if (tpW != null) tpW.Visible = false;
        }
    }

    // ── Model config — tweak these to align Rascal.glb ──────────────────────
    const string ModelPath   = "res://playerModels/rascal-cat/Rascal.glb";
    const float  ModelScale  = 3.0f;    // uniform scale; increase if model is too small
    // Y offset so model feet sit at capsule base (0 = pivot at world origin).
    const float  ModelYOff   = -0.05f;

    Node3D BuildPlayerBody()
    {
        var root = new Node3D { Name = "PlayerBody", Visible = false };

        var scene = GD.Load<PackedScene>(ModelPath);
        if (scene != null)
        {
            var model = scene.Instantiate<Node3D>();
            model.Name             = "RascalModel";
            model.Scale            = Vector3.One * ModelScale;
            model.Position         = new Vector3(0f, ModelYOff, 0f);
            model.RotationDegrees  = new Vector3(0f, 180f, 0f);
            root.AddChild(model);
        }
        else
        {
            // Fallback: primitive capsule body used when GLB is missing
            var mat = new StandardMaterial3D { AlbedoColor = new(0.25f, 0.30f, 0.45f), Roughness = 0.8f };
            var torso = new MeshInstance3D
            {
                Mesh     = new CapsuleMesh { Radius = 0.28f, Height = 0.80f },
                Position = new(0f, 0.75f, 0f),
            };
            torso.SetSurfaceOverrideMaterial(0, mat);
            root.AddChild(torso);
            var head = new MeshInstance3D
            {
                Mesh     = new SphereMesh { Radius = 0.20f, RadialSegments = 8, Rings = 6 },
                Position = new(0f, 1.45f, 0f),
            };
            head.SetSurfaceOverrideMaterial(0, mat);
            root.AddChild(head);
        }

        var tpWeapon = new MeshInstance3D
        {
            Name     = "TpWeapon",
            Mesh     = new BoxMesh { Size = new(0.05f, 0.70f, 0.05f) },
            Position = new(0.32f, 0.80f, -0.15f),
            Rotation = new(0.3f, 0f, 0f),
            Visible  = false,
        };
        var wMat = new StandardMaterial3D { AlbedoColor = new(0.6f, 0.6f, 0.65f), Metallic = 0.9f, Roughness = 0.25f };
        tpWeapon.SetSurfaceOverrideMaterial(0, wMat);
        root.AddChild(tpWeapon);

        return root;
    }

    public void ReleaseMouse() => Input.MouseMode = Input.MouseModeEnum.Visible;
    public Camera3D Camera    => _cam;
}
