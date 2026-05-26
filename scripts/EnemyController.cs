using System;
using Godot;

/// AI training dummy: walks toward the player, attacks with SwordCombat.
/// Uses UAL1_Standard.glb with a red tint on all surfaces.
/// CombatTeam = 1 — enemies don't hurt each other.
/// On death: plays Death01, body stays 6 s then QueueFree.
public partial class EnemyController : CharacterBody3D
{
    const float MoveSpeed        = 3.5f;
    const float AttackRange      = 1.9f;
    const float StrafeRange      = 4.5f;   // begin orbiting below this distance
    const float Gravity          = 22f;
    const float ModelScale       = 1.0f;   // must match PlayerController.ModelScale
    const float JumpSpeed        = 8.5f;
    const float LeapSpeed        = 11f;
    const float DodgeSpeed       = 13f;
    const float LeapCooldownDur  = 3.5f;
    const float DodgeCooldownDur = 1.2f;

    const string ModelPath = "res://playerModels/animations/Universal Animation Library[Standard]/Unreal-Godot/UAL1_Standard.glb";
    const string SwordPath = "res://assets/all_dmc2_swords_remade.glb";
    const string SwordMesh = "Dante_no_coat_001_RebellionSwordMaterial_002_0";

    SwordCombat?       _combat;
    Node3D?            _target;
    PlayerController?  _playerCtrl;    // typed ref to detect player combat phase
    float              _yaw         = 0f;
    bool               _dying       = false;
    bool               _isAttacking = false;
    CollisionShape3D?  _colShape;
    AnimationPlayer?   _ap;
    string             _curAnim     = "";

    // ── Behavior state machine ─────────────────────────────────────────────────
    enum BehaviorState { Stalk, CircleStrafe, BackOff, Leap, Dodge }
    BehaviorState _behavior      = BehaviorState.Stalk;
    float         _behaviorTimer = 0f;
    float         _strafeSign    = 1f;   // +1 or -1 orbit direction
    float         _aggrTimer      = 0f;   // countdown to next attack window
    float         _leapCooldown    = 1.5f;
    float         _dodgeCooldown   = 0f;
    Vector3       _dodgeDir        = Vector3.Zero;
    bool          _leapJumpApplied = false;
    bool          _dodgeHopApplied = false;

    public event Action? Died;

    // ── Spawn ─────────────────────────────────────────────────────────────────
    public static EnemyController Spawn(Node parent, Vector3 pos, Node3D target)
    {
        var e = new EnemyController();
        e.Name    = "Enemy";
        e._target = target;
        e._strafeSign = GD.Randf() > 0.5f ? 1f : -1f;  // random initial orbit direction
        parent.AddChild(e);
        e.Position = pos;
        return e;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    public override void _Ready()
    {
        _colShape = new CollisionShape3D
        {
            Shape = new CapsuleShape3D { Radius = 0.35f, Height = 1.6f },
            Name  = "Body",
        };
        AddChild(_colShape);
        CollisionMask = 0xFFFFFFFF;
        MotionMode    = MotionModeEnum.Grounded;
        UpDirection   = Vector3.Up;
        FloorMaxAngle   = 68f * Mathf.Pi / 180f;
        FloorSnapLength = 1.0f;

        var health = new PlayerHealth { Name = "PlayerHealth" };
        health.OnDied += OnHealthDied;
        AddChild(health);

        _combat = new SwordCombat { CombatTeam = 1, AiControlled = true };
        AddChild(_combat);
        _combat.OnSwingStart = _ => { _isAttacking = true; PlayClip("attack1"); };

        // Cache typed player reference for combat-phase reading
        _playerCtrl = _target as PlayerController;

        // When our block absorbs a hit, drop the block and counter-attack immediately
        _combat.OnBlockImpact = () =>
        {
            _combat.AiBlockHeld = false;
            _aggrTimer = 0f;
        };

        AddToGroup("enemies");
        BuildModel();
        AddChild(BuildHpBar());
    }

    // ── Model setup ───────────────────────────────────────────────────────────
    // Loads UAL1 directly — does NOT call SetupNpcAnimations, so no extra
    // GLB loads per enemy (avoiding the multi-second freeze on spawn).
    // Uses the clip names already baked into UAL1: "Idle", "Walk", "Death01".
    void BuildModel()
    {
        var scene = GD.Load<PackedScene>(ModelPath);
        if (scene == null)
        {
            GD.PrintErr("[Enemy] UAL1 model not found — using capsule fallback");
            BuildCapsuleFallback();
            return;
        }

        var model = scene.Instantiate<Node3D>();
        model.Name            = "CharacterModel";
        model.Scale           = Vector3.One * ModelScale;
        model.Position        = new Vector3(0f, -0.9f, 0f);   // matches PlayerController.ModelYOff
        model.RotationDegrees = new Vector3(0f, 180f, 0f);
        ApplyRedTint(model);
        AddChild(model);

        // Sword on right-hand bone
        var skel = FindFirst<Skeleton3D>(model);
        if (skel != null)
        {
            string handBone = FindHandBone(skel);
            if (handBone.Length > 0)
            {
                var attach = new BoneAttachment3D { BoneName = handBone, Name = "SwordAttach" };
                skel.AddChild(attach);
                var sword = LoadSwordMesh();
                if (sword != null)
                {
                    if (sword.Mesh != null)
                    {
                        var   aabb = sword.Mesh.GetAabb();
                        float maxD = Mathf.Max(aabb.Size.X, Mathf.Max(aabb.Size.Y, aabb.Size.Z));
                        sword.Scale = maxD > 0.01f ? Vector3.One * (0.9f / maxD) : Vector3.One;
                    }
                    sword.RotationDegrees = new Vector3(0f, 180f, -90f);
                    attach.AddChild(sword);
                }
            }
        }

        _ap = PlayerAnimator.FindAnimationPlayerStatic(model);
        if (_ap != null)
        {
            // Merge UAL2 attack + block clips into the UAL1 model's AnimationPlayer.
            // UAL1/UAL2 are already cached by Godot's resource loader — no freeze.
            PlayerAnimator.SetupNpcAnimations(_ap);
            PlayClip("Idle");
        }
    }

    void BuildCapsuleFallback()
    {
        var mesh = new MeshInstance3D
        {
            Mesh     = new CapsuleMesh { Radius = 0.28f, Height = 0.80f },
            Position = new Vector3(0f, 0.9f, 0f),
        };
        mesh.SetSurfaceOverrideMaterial(0, new StandardMaterial3D
        {
            AlbedoColor = new Color(0.80f, 0.08f, 0.08f),
            Emission     = new Color(0.35f, 0.0f, 0.0f),
            EmissionEnabled = true,
            Roughness    = 0.8f,
        });
        AddChild(mesh);
    }

    void ApplyRedTint(Node node)
    {
        if (node is MeshInstance3D mi)
            mi.MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor     = new Color(0.80f, 0.08f, 0.08f),
                Emission        = new Color(0.20f, 0.0f, 0.0f),
                EmissionEnabled = true,
                Roughness       = 0.75f,
                Metallic        = 0.15f,
            };
        foreach (Node child in node.GetChildren())
            ApplyRedTint(child);
    }

    void PlayClip(string clip)
    {
        if (_ap == null || _curAnim == clip) return;
        if (!_ap.HasAnimation(clip)) return;
        _curAnim = clip;
        _ap.Play(clip);
    }

    // ── Physics ───────────────────────────────────────────────────────────────
    public override void _PhysicsProcess(double delta)
    {
        if (_dying) return;

        float dt      = (float)delta;
        bool  onFloor = IsOnFloor();
        var   vel     = Velocity;

        if (!onFloor) vel.Y -= Gravity * dt;
        else          vel.Y  = -0.5f;

        if (_target != null && IsInstanceValid(_target))
        {
            var   toTarget = _target.GlobalPosition - GlobalPosition;
            float dist     = toTarget.Length();
            var   horizTo  = new Vector3(toTarget.X, 0f, toTarget.Z);

            if (horizTo.LengthSquared() > 0.01f)
            {
                var dir = horizTo.Normalized();
                _yaw = Mathf.Atan2(-dir.X, -dir.Z);
                UpdateBehavior(ref vel, dt, dist, horizTo, onFloor);
            }
        }
        else
        {
            vel.X = Mathf.MoveToward(vel.X, 0f, 12f * dt);
            vel.Z = Mathf.MoveToward(vel.Z, 0f, 12f * dt);
        }

        Velocity = vel;
        MoveAndSlide();

        _combat?.Tick(dt, !IsOnFloor(), false, _yaw);
        Rotation = new Vector3(0f, _yaw, 0f);

        // Animation clip switching
        bool isBlocking = _combat?.IsBlocking ?? false;
        if (_isAttacking && _combat?.CombatPhase == SwordCombat.Phase.Idle)
            _isAttacking = false;

        if (isBlocking)
            PlayClip("block");
        else if (!_isAttacking)
        {
            bool moving = new Vector2(Velocity.X, Velocity.Z).LengthSquared() > 0.3f;
            PlayClip(moving ? "Walk" : "Idle");
        }
        // while _isAttacking: "attack1" was initiated by OnSwingStart and plays through
    }

    public override void _Process(double delta)
    {
        const float BarW  = 0.9f;
        const float HpH   = 0.12f;
        const float StamH = 0.03f;
        const float Gap   = 0.005f;
        float stamY = -(HpH / 2f + Gap + StamH / 2f);

        // ── HP bar (left-anchored, depletes right→left) ───────────────────────
        var health = GetNodeOrNull<PlayerHealth>("PlayerHealth");
        if (health != null)
        {
            float hpFrac = Mathf.Clamp(health.CurrentHealth / PlayerHealth.MaxHealth, 0f, 1f);
            var   hpFill = GetNodeOrNull<MeshInstance3D>("HpBarRoot/HpFill");
            if (hpFill != null)
            {
                hpFill.Scale    = new Vector3(Mathf.Max(hpFrac, 0.001f), 1f, 1f);
                hpFill.Position = new Vector3(BarW * (hpFrac - 1f) / 2f, 0f, 0f);
            }
        }

        // ── Stamina bar (left-anchored, depletes right→left) ──────────────────
        if (_combat != null)
        {
            float sFrac    = Mathf.Clamp(_combat.Stamina / _combat.MaxStam, 0f, 1f);
            var   stamFill = GetNodeOrNull<MeshInstance3D>("HpBarRoot/StamFill");
            if (stamFill != null)
            {
                stamFill.Scale    = new Vector3(Mathf.Max(sFrac, 0.001f), 1f, 1f);
                stamFill.Position = new Vector3(BarW * (sFrac - 1f) / 2f, stamY, 0f);
            }
        }

        // ── Billboard toward camera ───────────────────────────────────────────
        var bar = GetNodeOrNull<Node3D>("HpBarRoot");
        var cam = GetViewport().GetCamera3D();
        if (bar != null && cam != null)
        {
            var toCamera = cam.GlobalPosition - bar.GlobalPosition;
            toCamera.Y   = 0f;
            if (toCamera.LengthSquared() > 0.001f)
                bar.LookAt(bar.GlobalPosition + toCamera, Vector3.Up);
        }
    }

    // ── Behavior FSM ──────────────────────────────────────────────────────────
    void UpdateBehavior(ref Vector3 vel, float dt, float dist, Vector3 horizTo, bool onFloor)
    {
        _behaviorTimer -= dt;
        if (_leapCooldown  > 0f) _leapCooldown  -= dt;
        if (_dodgeCooldown > 0f) _dodgeCooldown -= dt;

        var  playerPhase   = _playerCtrl?.PlayerCombatPhase ?? SwordCombat.Phase.Idle;
        bool playerWinding = playerPhase is SwordCombat.Phase.Windup or SwordCombat.Phase.HitWindow;
        bool punishWindow  = playerPhase is SwordCombat.Phase.Recovery or SwordCombat.Phase.Cooldown;

        int  playerCombo = _playerCtrl?.PlayerComboStep ?? 0;
        bool canBlock    = _behavior is not (BehaviorState.BackOff or BehaviorState.Leap or BehaviorState.Dodge);
        _combat!.AiBlockHeld = canBlock && playerWinding && dist <= AttackRange * 1.6f && playerCombo == 0;

        switch (_behavior)
        {
            case BehaviorState.Stalk:
            {
                if (dist <= StrafeRange)
                    EnterStrafe();
                else
                    ApplyMoveVel(ref vel, horizTo.Normalized() * MoveSpeed, dt);
                break;
            }

            case BehaviorState.CircleStrafe:
            {
                if (dist > StrafeRange * 1.8f) { _behavior = BehaviorState.Stalk; break; }

                if (_behaviorTimer <= 0f)
                {
                    _strafeSign    = -_strafeSign;
                    _behaviorTimer = 1.0f + GD.Randf() * 1.4f;
                }

                // React to player swinging: dodge sideways (hop) or back up
                if (playerWinding && dist < AttackRange * 2.5f)
                {
                    if (_dodgeCooldown <= 0f && GD.Randf() > 0.45f)
                    {
                        var fwd  = horizTo.Normalized();
                        var perp = new Vector3(-fwd.Z, 0f, fwd.X);
                        EnterDodge(perp * _strafeSign);
                        break;
                    }
                    ApplyMoveVel(ref vel, -horizTo.Normalized() * MoveSpeed * 0.6f, dt);
                }
                else
                {
                    var fwd    = horizTo.Normalized();
                    var right  = new Vector3(-fwd.Z, 0f, fwd.X);
                    var strafe = right * _strafeSign;
                    float inward = dist > AttackRange * 1.2f ? 0.55f : 0.0f;
                    var   wish   = (fwd * inward + strafe * (1f - inward)).Normalized();
                    var   flank  = ComputeFlankSteering();
                    if (flank.LengthSquared() > 0.01f)
                        wish = (wish * 0.55f + flank * 0.45f).Normalized();
                    ApplyMoveVel(ref vel, wish * MoveSpeed * 0.85f, dt);
                }

                _aggrTimer -= dt;

                // Ground attack when in melee range
                if (dist <= AttackRange && !playerWinding)
                {
                    if (punishWindow || _aggrTimer <= 0f)
                    {
                        _combat.AiBlockHeld = false;
                        _combat.RequestAttack();
                        _behavior      = BehaviorState.BackOff;
                        _behaviorTimer = 0.2f + GD.Randf() * 0.3f;
                        _aggrTimer     = 0.9f + GD.Randf() * 0.7f;
                    }
                }
                // Leap attack at medium range when cooldown ready
                else if (_leapCooldown <= 0f && dist > AttackRange && dist <= StrafeRange)
                {
                    if (punishWindow || (_aggrTimer <= 0f && GD.Randf() > 0.55f))
                    {
                        EnterLeap();
                        _aggrTimer = 1.2f + GD.Randf() * 0.8f;
                        break;
                    }
                    if (_aggrTimer <= 0f) _aggrTimer = 0.35f;
                }
                else if (_aggrTimer <= 0f)
                {
                    _aggrTimer = 0.35f;
                }
                break;
            }

            case BehaviorState.BackOff:
            {
                ApplyMoveVel(ref vel, -horizTo.Normalized() * MoveSpeed, dt);
                if (_behaviorTimer <= 0f)
                    EnterStrafe();
                break;
            }

            case BehaviorState.Leap:
            {
                // Apply jump impulse on the first grounded tick
                if (!_leapJumpApplied && onFloor)
                {
                    var dir2 = horizTo.Normalized();
                    vel.Y    = JumpSpeed;
                    vel.X    = dir2.X * LeapSpeed;
                    vel.Z    = dir2.Z * LeapSpeed;
                    _leapJumpApplied = true;
                }
                else if (!onFloor)
                {
                    // Steer toward player while airborne
                    ApplyMoveVel(ref vel, horizTo.Normalized() * LeapSpeed * 0.7f, dt);
                    // Attack when close enough mid-air
                    if (dist <= AttackRange + 1.0f && !_isAttacking)
                    {
                        _combat!.AiBlockHeld = false;
                        _combat.RequestAttack();
                    }
                }
                else if (_leapJumpApplied && _behaviorTimer <= 0f)
                {
                    EnterStrafe();
                }
                break;
            }

            case BehaviorState.Dodge:
            {
                // Pop upward on first tick for a visible hop
                if (!_dodgeHopApplied)
                {
                    vel.Y = 4.0f;
                    _dodgeHopApplied = true;
                }
                ApplyMoveVel(ref vel, _dodgeDir * DodgeSpeed, dt);
                if (_behaviorTimer <= 0f)
                    EnterStrafe();
                break;
            }
        }
    }

    // Steer away from nearby fellow enemies so they naturally spread and surround.
    Vector3 ComputeSeparation()
    {
        const float MinDist = 2.2f;
        var sep = Vector3.Zero;
        foreach (var node in GetTree().GetNodesInGroup("enemies"))
        {
            if (node == this || node is not EnemyController other) continue;
            var   diff = GlobalPosition - other.GlobalPosition;
            float d    = diff.Length();
            if (d < MinDist && d > 0.01f)
                sep += diff.Normalized() * ((MinDist - d) / MinDist);
        }
        return sep;
    }

    // With 2+ enemies, assigns each a stable angular slot around the player starting
    // from directly behind them, so they naturally flank. Falls back to separation alone
    // when solo.
    Vector3 ComputeFlankSteering()
    {
        if (_target == null) return Vector3.Zero;

        var allEnemies = GetTree().GetNodesInGroup("enemies");
        int total      = allEnemies.Count;

        if (total <= 1) return ComputeSeparation();

        // Stable slot assignment: sort by instance ID so every enemy agrees on ordering
        var ids = new System.Collections.Generic.List<ulong>();
        foreach (var n in allEnemies) ids.Add(n.GetInstanceId());
        ids.Sort();
        int idx = ids.IndexOf(GetInstanceId());

        // Slot 0 → directly behind player; subsequent slots spread evenly around
        float targetAngle = _target.Rotation.Y + Mathf.Pi + idx * (2f * Mathf.Pi / total);
        float r           = AttackRange * 1.5f;
        var   targetPos   = _target.GlobalPosition
                          + new Vector3(Mathf.Sin(targetAngle) * r, 0f, Mathf.Cos(targetAngle) * r);

        var toTarget = targetPos - GlobalPosition;
        toTarget.Y   = 0f;

        var steer = toTarget.LengthSquared() > 0.01f ? toTarget.Normalized() : Vector3.Zero;

        // Blend in separation so enemies don't clip through each other while flanking
        var sep = ComputeSeparation();
        if (sep.LengthSquared() > 0.01f)
            steer = steer.LengthSquared() > 0.01f
                  ? (steer * 0.7f + sep.Normalized() * 0.3f).Normalized()
                  : sep.Normalized();

        return steer;
    }

    void EnterStrafe()
    {
        _behavior      = BehaviorState.CircleStrafe;
        _behaviorTimer = 1.0f + GD.Randf() * 1.5f;
        _aggrTimer     = 0.4f + GD.Randf() * 0.5f;
        _strafeSign    = GD.Randf() > 0.5f ? 1f : -1f;
    }

    void EnterLeap()
    {
        _behavior        = BehaviorState.Leap;
        _behaviorTimer   = 0.35f;
        _leapCooldown    = LeapCooldownDur;
        _leapJumpApplied = false;
    }

    void EnterDodge(Vector3 dir)
    {
        _behavior        = BehaviorState.Dodge;
        _behaviorTimer   = 0.30f;
        _dodgeCooldown   = DodgeCooldownDur;
        _dodgeDir        = dir;
        _dodgeHopApplied = false;
    }

    void ApplyMoveVel(ref Vector3 vel, Vector3 target, float dt)
    {
        vel.X = Mathf.MoveToward(vel.X, target.X, 14f * dt);
        vel.Z = Mathf.MoveToward(vel.Z, target.Z, 14f * dt);
    }

    // ── Death ─────────────────────────────────────────────────────────────────
    void OnHealthDied()
    {
        if (_dying) return;
        _dying = true;

        SetPhysicsProcess(false);
        if (_colShape != null) _colShape.Disabled = true;

        // Play the UAL1 built-in death clip
        if (_ap != null && _ap.HasAnimation("Death01"))
        {
            _curAnim = "Death01";
            _ap.Play("Death01");
        }

        Died?.Invoke();
        GetTree().CreateTimer(6.0).Timeout += () => QueueFree();
    }

    // ── Asset helpers ─────────────────────────────────────────────────────────
    static string FindHandBone(Skeleton3D skel)
    {
        foreach (var c in new[] { "RightHand", "hand_r", "Hand_R", "R_Hand", "mixamorig:RightHand" })
            if (skel.FindBone(c) >= 0) return c;
        for (int i = 0; i < skel.GetBoneCount(); i++)
        {
            var bn = skel.GetBoneName(i);
            if (bn.ToLowerInvariant().Contains("hand") && !bn.ToLowerInvariant().Contains("left"))
                return bn;
        }
        return "";
    }

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

    static MeshInstance3D? LoadSwordMesh()
    {
        var packed = GD.Load<PackedScene>(SwordPath);
        if (packed == null) return null;
        var tempRoot = packed.Instantiate<Node3D>();
        var src = FindFirst<MeshInstance3D>(tempRoot, SwordMesh)
               ?? FindFirst<MeshInstance3D>(tempRoot);
        if (src == null) { tempRoot.Free(); return null; }
        var inst = new MeshInstance3D { Name = "EnemySword", Mesh = src.Mesh };
        tempRoot.Free();
        return inst;
    }

    internal static Node3D BuildBarGroup(float yOffset)
    {
        const float BarW  = 0.9f;
        const float HpH   = 0.12f;
        const float StamH = 0.03f;
        const float Gap   = 0.005f;
        const float Depth = 0.02f;
        float stamY = -(HpH / 2f + Gap + StamH / 2f);

        var root = new Node3D { Name = "HpBarRoot", Position = new Vector3(0f, yOffset, 0f) };

        var hpBack = new MeshInstance3D { Name = "HpBack", Mesh = new BoxMesh { Size = new Vector3(BarW, HpH, Depth) } };
        hpBack.SetSurfaceOverrideMaterial(0, new StandardMaterial3D
        {
            AlbedoColor = new Color(0.18f, 0.18f, 0.18f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        });
        root.AddChild(hpBack);

        var hpFill = new MeshInstance3D { Name = "HpFill", Mesh = new BoxMesh { Size = new Vector3(BarW, HpH, Depth) } };
        hpFill.SetSurfaceOverrideMaterial(0, new StandardMaterial3D
        {
            AlbedoColor = new Color(0.90f, 0.12f, 0.10f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        });
        root.AddChild(hpFill);

        var stamBack = new MeshInstance3D { Name = "StamBack", Mesh = new BoxMesh { Size = new Vector3(BarW, StamH, Depth) } };
        stamBack.Position = new Vector3(0f, stamY, 0.001f);
        stamBack.SetSurfaceOverrideMaterial(0, new StandardMaterial3D
        {
            AlbedoColor = new Color(0.12f, 0.12f, 0.12f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        });
        root.AddChild(stamBack);

        var stamFill = new MeshInstance3D { Name = "StamFill", Mesh = new BoxMesh { Size = new Vector3(BarW, StamH, Depth) } };
        stamFill.Position = new Vector3(0f, stamY, 0.001f);
        stamFill.SetSurfaceOverrideMaterial(0, new StandardMaterial3D
        {
            AlbedoColor = Colors.White,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        });
        root.AddChild(stamFill);

        return root;
    }

    Node3D BuildHpBar() => BuildBarGroup(2.3f);
}
