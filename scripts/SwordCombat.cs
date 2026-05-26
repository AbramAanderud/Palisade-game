using Godot;
using System;

/// res://scripts/SwordCombat.cs
/// Dark and Darker–style sword combat state machine.
/// Owned and ticked by PlayerController when HasWeapon == true.
///
/// Swing timeline (per swing): Windup → HitWindow → Recovery
///   Windup:    no hitbox, no cancel, animation starts
///   HitWindow: hitbox active, combo input accepted, one hit per window
///   Recovery:  hitbox off, vulnerable
///
/// Stamina drains on swing start; regenerates when idle/not blocking.
/// Directional block: must face attacker (within 90°) for block to succeed.
/// Impact Power: scales stamina cost on blocker; block breaks if stamina hits 0.
public partial class SwordCombat : Node
{
    // ── Swing phase durations at ActionSpeed 1.0 (seconds) ───────────────────
    const float S1Windup = 0.17f; const float S1Hit = 0.17f; const float S1Rec = 0.10f;
    const float S2Windup = 0.15f; const float S2Hit = 0.15f; const float S2Rec = 0.10f;
    const float S3Windup = 0.22f; const float S3Hit = 0.22f; const float S3Rec = 0.15f;
    const float CooldownDuration = 0.15f;

    // ── Stamina ───────────────────────────────────────────────────────────────
    const float MaxStamina          = 150f;
    const float SwingCost           = 20f;
    const float RegenRate           = 75f;    // fast regen — full circle empties in ~2 s
    const float ExhaustionDuration  = 3f;     // lock-out when circle maxes out

    // ── Block guard-break ─────────────────────────────────────────────────────
    // Block absorbs up to MaxBlockAbsorbs hits, then shatters.
    // Shattering costs stamina — can trigger exhaustion if already low.
    const int   MaxBlockAbsorbs       = 2;
    const float BlockBreakStaminaCost = 60f;
    int         _blockHitsAbsorbed    = 0;

    // ── Base damage ───────────────────────────────────────────────────────────
    const float Dmg1 = 23f; const float Dmg2 = 27f; const float Dmg3 = 35f;

    // ── Hit location thresholds (Y relative to target origin) ────────────────
    const float HeadMinY = 1.5f;
    const float LegsMaxY = 0.4f;

    // ── Action speed (future stat hook) ──────────────────────────────────────
    public float ActionSpeed = 1.0f;   // 1.0 = base; higher = faster swings

    // ── Per-weapon stat hooks (set by PlayerController when equipping a sword) ──
    /// Multiplied into every damage event. 0.5 = weaker spawn sword, 1.0 = arena sword.
    public float DamageMultiplier = 1.0f;
    /// Fraction of damage dealt that heals the attacker (0.10 = 10% lifesteal).
    public float Lifesteal        = 0.0f;

    // ── Team (0 = player, 1 = enemies; same-team bodies are immune to each other) ──
    public int  CombatTeam   = 0;
    // ── AI flag — disables keyboard-driven block and spin so enemies aren't affected ──
    public bool AiControlled = false;
    // ── AI block control — set true each frame to hold block; false to release ──
    public bool AiBlockHeld  = false;
    // ── Fires on the BLOCKER when their block successfully absorbs a hit ──────
    public Action? OnBlockImpact;
    // ── Aerial lunge ──────────────────────────────────────────────────────────
    public Action? OnAerialLungeStart;               // fires at lunge start (apply dash velocity)
    public float   AerialLungeSpeed = 0f;            // set by caller before triggering
    public bool    IsAerialLunge    { get; private set; } = false;
    public bool    IsLunging        => IsAerialLunge && _phase != SwingPhase.Recovery;
    const  float   LungeDmgBase     = 42f;
    const  float   AerialCooldownDur = 2.0f;
    float          _aerialCooldown   = 0f;
    public bool    AerialOnCooldown  => _aerialCooldown > 0f;

    // ── Stamina (public for HUD) ──────────────────────────────────────────────
    float _stamina          = MaxStamina;
    bool  _exhausted        = false;
    float _exhaustionTimer  = 0f;

    public float Stamina      => _stamina;
    public float MaxStam      => MaxStamina;
    public bool  IsExhausted  => _exhausted;

    // ── AI input ─────────────────────────────────────────────────────────────
    bool _attackRequested = false;

    /// Request a single attack on the next TickIdle evaluation (used by AI).
    public void RequestAttack() => _attackRequested = true;

    // ── State ─────────────────────────────────────────────────────────────────
    enum SwingPhase { Windup, HitWindow, Recovery }
    enum CombatState { Idle, Swing1, Swing2, Swing3, Block, Cooldown, AerialLunge }

    CombatState _state      = CombatState.Idle;
    SwingPhase  _phase      = SwingPhase.Windup;
    float       _phaseTimer = 0f;
    bool        _hitLanded  = false;
    bool        _isAirborne = false;

    // ── Delegates (wired by PlayerController) ────────────────────────────────
    public Action<int>?  OnSwingStart;    // 0/1/2 = Swing1/2/3
    public Action<bool>? OnBlockChange;

    // ── Public combat phase (used by PlayerController to animate the FP sword) ─
    public enum Phase { Idle, Windup, HitWindow, Recovery, Block, Cooldown }
    public Phase CombatPhase { get; private set; } = Phase.Idle;

    // ── Public properties ─────────────────────────────────────────────────────
    public bool IsBlocking  { get; private set; } = false;
    /// Which combo step is playing (0=Swing1, 1=Swing2, 2=Swing3). Resets to 0 at Idle.
    public int  ComboStep   { get; private set; } = 0;

    // ── Hitbox ────────────────────────────────────────────────────────────────
    Area3D? _hitbox;

    // ── Audio ─────────────────────────────────────────────────────────────────
    AudioStreamPlayer3D _regularSwingSfx = null!;
    AudioStreamPlayer3D _thirdSwingSfx   = null!;
    AudioStreamPlayer3D _blockSfx        = null!;
    AudioStreamPlayer3D _fleshSfx        = null!;

    AudioStreamPlayer3D MakeSfx3D(Node3D parent, string path)
    {
        var sfx = new AudioStreamPlayer3D { MaxDistance = 30f };
        var stream = GD.Load<AudioStream>(path);
        if (stream != null) sfx.Stream = stream;
        parent.AddChild(sfx);
        return sfx;
    }

    // ── Setup ─────────────────────────────────────────────────────────────────
    public override void _Ready()
    {
        var parent = GetParent<Node3D>();
        _hitbox = new Area3D { Name = "SwordHitbox" };
        parent.AddChild(_hitbox);

        var cs    = new CollisionShape3D();
        cs.Shape  = new CapsuleShape3D { Radius = 0.18f, Height = 0.8f };
        _hitbox.AddChild(cs);

        _hitbox.CollisionMask = 0xFFFFFFFF;
        _hitbox.Monitoring    = true;    // always on — gated by _phase check in Tick
        _hitbox.Monitorable   = false;

        _regularSwingSfx = MakeSfx3D(parent, "res://assets/audio/sfx/combat/RegularSwordSwing.wav");
        _thirdSwingSfx   = MakeSfx3D(parent, "res://assets/audio/sfx/combat/ThirdSwordSwing.wav");
        _blockSfx        = MakeSfx3D(parent, "res://assets/audio/sfx/combat/swordblock.wav");
        _fleshSfx        = MakeSfx3D(parent, "res://assets/audio/sfx/combat/swordfleshimpact.wav");
    }

    // ── Per-frame tick ────────────────────────────────────────────────────────
    public void Tick(float dt, bool isAirborne, bool isWallRunning, float playerYaw)
    {
        _isAirborne = isAirborne;
        if (_aerialCooldown > 0f) _aerialCooldown -= dt;
        UpdateHitboxPosition(playerYaw);

        // Exhaustion lock-out: regen continues so the ring visibly empties
        if (_exhausted)
        {
            // Force-drop block — can't hold block without stamina
            if (_state == CombatState.Block)
            {
                IsBlocking  = false;
                CombatPhase = Phase.Idle;
                OnBlockChange?.Invoke(false);
                _state = CombatState.Idle;
            }
            _stamina         = Mathf.Min(_stamina + RegenRate * dt, MaxStamina);
            _exhaustionTimer -= dt;
            if (_exhaustionTimer <= 0f) _exhausted = false;
            return;
        }

        // Regen stamina immediately when not actively swinging/blocking
        bool inSwing = _state is CombatState.Swing1 or CombatState.Swing2
                                or CombatState.Swing3;
        if (!inSwing && !IsBlocking)
            _stamina = Mathf.Min(_stamina + RegenRate * dt, MaxStamina);

        bool blockHeld = AiControlled ? AiBlockHeld : Input.IsActionPressed("block");

        // Block interrupts Idle or any swing — not when exhausted
        if (blockHeld && !_exhausted && _state is CombatState.Idle
                               or CombatState.Swing1 or CombatState.Swing2 or CombatState.Swing3)
        {
            if (!IsBlocking)
            {
                IsBlocking         = true;
                CombatPhase        = Phase.Block;
                _blockHitsAbsorbed = 0;
                OnBlockChange?.Invoke(true);
            }
            _state     = CombatState.Block;
            return;
        }

        if (_state == CombatState.Block)
        {
            if (!blockHeld)
            {
                IsBlocking   = false;
                CombatPhase  = Phase.Idle;
                OnBlockChange?.Invoke(false);
                _state = CombatState.Idle;
            }
            return;
        }

        if (_state != CombatState.Block)
            IsBlocking = false;

        switch (_state)
        {
            case CombatState.Idle:
                CombatPhase = Phase.Idle;
                ComboStep   = 0;
                TickIdle();
                break;
            case CombatState.Swing1:     TickSwing(0, S1Windup, S1Hit, S1Rec, dt);  break;
            case CombatState.Swing2:     TickSwing(1, S2Windup, S2Hit, S2Rec, dt);  break;
            case CombatState.Swing3:     TickSwing(2, S3Windup, S3Hit, S3Rec, dt);  break;
            case CombatState.AerialLunge: TickAerialLunge(dt); break;
            case CombatState.Cooldown:
                CombatPhase = Phase.Cooldown;
                _phaseTimer -= dt;
                if (_phaseTimer <= 0f) _state = CombatState.Idle;
                break;
        }
    }

    // ── Idle ──────────────────────────────────────────────────────────────────
    void TickIdle()
    {
        bool wantsAttack = Input.IsActionJustPressed("attack") || _attackRequested;
        _attackRequested = false;
        if (wantsAttack && !_exhausted)
        {
            if (_isAirborne && !AiControlled && _aerialCooldown <= 0f)
            {
                StartAerialLunge();
            }
            else
            {
                DrainStamina(SwingCost);
                if (!_exhausted)
                    StartSwing(CombatState.Swing1, 0, S1Windup);
            }
        }
    }

    // ── Swing tick ────────────────────────────────────────────────────────────
    void TickSwing(int swingIdx, float windupDur, float hitDur, float recDur, float dt)
    {
        _phaseTimer -= dt;

        switch (_phase)
        {
            case SwingPhase.Windup:
                CombatPhase = Phase.Windup;
                if (_phaseTimer <= 0f)
                {
                    _phase      = SwingPhase.HitWindow;
                    _phaseTimer = hitDur / ActionSpeed;
                    _hitLanded  = false;
                }
                break;

            case SwingPhase.HitWindow:
                CombatPhase = Phase.HitWindow;
                // Poll overlapping bodies for hit detection
                if (!_hitLanded && _hitbox != null)
                    CheckHits(swingIdx);

                // Combo input accepted during HitWindow
                var nextState = NextSwingState(_state);
                if (nextState != CombatState.Cooldown
                    && Input.IsActionJustPressed("attack")
                    && !_exhausted)
                {
                    DrainStamina(SwingCost);
                    if (_exhausted) break;
                    int nextIdx = swingIdx + 1;
                    float nw   = nextIdx == 1 ? S2Windup : S3Windup;
                    StartSwing(nextState, nextIdx, nw);
                    return;
                }

                if (_phaseTimer <= 0f)
                {
                    _phase      = SwingPhase.Recovery;
                    _phaseTimer = recDur / ActionSpeed;
                }
                break;

            case SwingPhase.Recovery:
                CombatPhase = Phase.Recovery;
                if (_phaseTimer <= 0f)
                {
                    var ns = NextSwingState(_state);
                    if (ns == CombatState.Cooldown)
                    {
                        _state      = CombatState.Cooldown;
                        _phaseTimer = CooldownDuration / ActionSpeed;
                    }
                    else
                        _state = CombatState.Idle;
                }
                break;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    // Drain stamina and trigger exhaustion if it hits zero.
    void DrainStamina(float cost)
    {
        _stamina = Mathf.Max(0f, _stamina - cost);
        if (_stamina <= 0f && !_exhausted)
        {
            _exhausted       = true;
            _exhaustionTimer = ExhaustionDuration;
            // Snap out of any in-progress swing
            _state      = CombatState.Idle;
            CombatPhase = Phase.Idle;
            IsBlocking  = false;
        }
    }

    void StartSwing(CombatState s, int idx, float windupDur)
    {
        _state      = s;
        _phase      = SwingPhase.Windup;
        _phaseTimer = windupDur / ActionSpeed;
        CombatPhase = Phase.Windup;   // set before OnSwingStart so callers don't see Phase.Idle
        ComboStep   = idx;
        OnSwingStart?.Invoke(idx);
        (idx == 2 ? _thirdSwingSfx : _regularSwingSfx).Play();
    }

    CombatState NextSwingState(CombatState cur) => cur switch
    {
        CombatState.Swing1 => CombatState.Swing2,
        CombatState.Swing2 => CombatState.Swing3,
        _                  => CombatState.Cooldown,
    };

    void UpdateHitboxPosition(float yaw)
    {
        if (_hitbox == null) return;
        var   player = GetParent<CharacterBody3D>();
        var   fwd    = new Vector3(-Mathf.Sin(yaw), 0f, -Mathf.Cos(yaw));
        float dist   = IsAerialLunge ? 1.4f : 0.8f;
        _hitbox.GlobalPosition = player.GlobalPosition + fwd * dist + Vector3.Up * 1.1f;
    }

    // ── Aerial lunge ──────────────────────────────────────────────────────────
    void StartAerialLunge()
    {
        IsAerialLunge   = true;
        _aerialCooldown = AerialCooldownDur;
        _state          = CombatState.AerialLunge;
        _phase          = SwingPhase.Windup;
        _phaseTimer     = 0.12f;
        ComboStep       = 0;
        OnAerialLungeStart?.Invoke();
        _regularSwingSfx.Play();
    }

    void TickAerialLunge(float dt)
    {
        _phaseTimer -= dt;
        switch (_phase)
        {
            case SwingPhase.Windup:
                CombatPhase = Phase.Windup;
                if (_phaseTimer <= 0f)
                {
                    _phase      = SwingPhase.HitWindow;
                    // Duration scales with entry speed — faster approach = longer lunge
                    _phaseTimer = Mathf.Clamp(AerialLungeSpeed / 10f, 0.3f, 1.5f);
                    _hitLanded  = false;
                }
                break;
            case SwingPhase.HitWindow:
                CombatPhase = Phase.HitWindow;
                if (!_hitLanded) CheckLungeHit();
                // Cut to recovery immediately on hit or when an enemy is within reach
                if (_hitLanded || IsEnemyWithin(2.5f))
                {
                    _phase      = SwingPhase.Recovery;
                    _phaseTimer = 0.25f;
                    break;
                }
                if (_phaseTimer <= 0f)
                {
                    _phase      = SwingPhase.Recovery;
                    _phaseTimer = 0.25f;
                }
                break;
            case SwingPhase.Recovery:
                CombatPhase = Phase.Recovery;
                if (_phaseTimer <= 0f)
                {
                    IsAerialLunge = false;
                    _state        = CombatState.Idle;
                }
                break;
        }
    }

    bool IsEnemyWithin(float range)
    {
        var self = GetParent<Node3D>();
        float r2 = range * range;
        foreach (var node in GetTree().GetNodesInGroup("enemies"))
        {
            if (node is not Node3D t) continue;
            var d = t.GlobalPosition - self.GlobalPosition;
            d.Y = 0f;
            if (d.LengthSquared() < r2) return true;
        }
        return false;
    }

    void CheckLungeHit()
    {
        var self = GetParent<Node3D>();
        foreach (var body in _hitbox!.GetOverlappingBodies())
        {
            if (body == self) continue;
            var otherCombat = FindSwordCombat(body);
            if (otherCombat != null && otherCombat.CombatTeam == CombatTeam) continue;
            if (body.GetNodeOrNull<PlayerHealth>("PlayerHealth") == null) continue;
            ProcessLungeHit(body);
            _hitLanded = true;
            return;
        }
    }

    void ProcessLungeHit(Node3D body)
    {
        float speedFactor = Mathf.Clamp(AerialLungeSpeed / 15f, 0f, 1.5f);
        float dmg         = LungeDmgBase * (1f + speedFactor);

        var targetCombat = FindSwordCombat(body);
        if (targetCombat != null && targetCombat.IsBlocking && IsDirectionalBlockValid(body, targetCombat))
        {
            // Lunge counts as both hits — always shatters the guard immediately
            targetCombat._blockHitsAbsorbed = MaxBlockAbsorbs;
            targetCombat.OnBlockImpact?.Invoke();
            _blockSfx.GlobalPosition = _hitbox?.GlobalPosition ?? GetParent<Node3D>().GlobalPosition;
            _blockSfx.Play();
            targetCombat.IsBlocking  = false;
            targetCombat.CombatPhase = Phase.Idle;
            targetCombat._state      = CombatState.Idle;
            targetCombat.OnBlockChange?.Invoke(false);
            targetCombat.DrainStamina(BlockBreakStaminaCost);
            GD.Print("[Combat] Guard broken by aerial lunge!");
            return;
        }
        DeliverDamage(body, dmg, "Aerial");
    }

    // ── Hit detection & damage ────────────────────────────────────────────────
    void CheckHits(int swingIdx)
    {
        var self = GetParent<Node3D>();
        foreach (var body in _hitbox!.GetOverlappingBodies())
        {
            if (body == self) continue;
            // Skip friendly fire — same team can't hurt each other
            var otherCombat = FindSwordCombat(body);
            if (otherCombat != null && otherCombat.CombatTeam == CombatTeam) continue;
            // Only register hits on damageable targets
            if (body.GetNodeOrNull<PlayerHealth>("PlayerHealth") == null) continue;
            ProcessHit(body, swingIdx);
            _hitLanded = true;
            return;   // one hit per swing window
        }
    }

    void ProcessHit(Node3D body, int swingIdx)
    {
        float baseDmg = swingIdx switch
        {
            0 => Dmg1, 1 => Dmg2, _ => Dmg3
        };

        // Hit location multiplier
        float relY     = (_hitbox?.GlobalPosition.Y ?? 0f) - body.GlobalPosition.Y;
        float locMult  = relY > HeadMinY ? 1.5f : relY < LegsMaxY ? 0.5f : 1.0f;
        string locName = relY > HeadMinY ? "Head" : relY < LegsMaxY ? "Legs" : "Torso";

        float finalDmg = baseDmg * locMult;

        // Check if target is blocking (directional)
        var targetCombat = FindSwordCombat(body);
        if (targetCombat != null && targetCombat.IsBlocking)
        {
            if (IsDirectionalBlockValid(body, targetCombat))
            {
                targetCombat._blockHitsAbsorbed++;
                targetCombat.OnBlockImpact?.Invoke();
                _blockSfx.GlobalPosition = _hitbox?.GlobalPosition ?? GetParent<Node3D>().GlobalPosition;
                _blockSfx.Play();

                if (targetCombat._blockHitsAbsorbed >= MaxBlockAbsorbs)
                {
                    // Guard broken — force exit block and drain stamina
                    targetCombat.IsBlocking  = false;
                    targetCombat.CombatPhase = Phase.Idle;
                    targetCombat._state      = CombatState.Idle;
                    targetCombat.OnBlockChange?.Invoke(false);
                    targetCombat.DrainStamina(BlockBreakStaminaCost);
                    GD.Print("[Combat] Guard broken!");
                }
                return;
            }
        }

        DeliverDamage(body, finalDmg, locName);
    }

    bool IsDirectionalBlockValid(Node3D body, SwordCombat targetCombat)
    {
        var attacker  = GetParent<Node3D>();
        var toAttacker = (attacker.GlobalPosition - body.GlobalPosition);
        toAttacker.Y  = 0f;
        if (toAttacker.LengthSquared() < 0.001f) return false;
        toAttacker = toAttacker.Normalized();

        // Get blocker's facing direction — PlayerController exposes ForwardVector
        Vector3 targetFwd = Vector3.Zero;
        if (body is PlayerController pc)
            targetFwd = pc.ForwardVector;
        else
            targetFwd = -body.GlobalTransform.Basis.Z;  // fallback for non-PC bodies
        targetFwd.Y = 0f;
        if (targetFwd.LengthSquared() < 0.001f) return false;
        targetFwd = targetFwd.Normalized();

        // Block succeeds if attacker is within 90° of blocker's forward (dot > 0)
        return toAttacker.Dot(targetFwd) > 0f;
    }

    void DeliverDamage(Node3D target, float damage, string location)
    {
        float finalDmg = damage * DamageMultiplier;
        GD.Print($"[Combat] HIT {target.Name} | {location} | {finalDmg:F0} dmg");
        var hitPos = _hitbox?.GlobalPosition ?? target.GlobalPosition;
        _fleshSfx.GlobalPosition = hitPos;
        _fleshSfx.Play();
        var health = target.GetNodeOrNull<PlayerHealth>("PlayerHealth");
        health?.TakeDamage(finalDmg, hitPos);
        SpawnDamageNumber(hitPos, finalDmg);

        // Lifesteal — heal the attacker for a fraction of the damage dealt
        if (Lifesteal > 0f)
        {
            var selfHealth = GetParent<Node3D>()?.GetNodeOrNull<PlayerHealth>("PlayerHealth");
            selfHealth?.Heal(finalDmg * Lifesteal);
        }
    }

    void SpawnDamageNumber(Vector3 worldPos, float damage)
    {
        var label = new Label3D
        {
            Text        = ((int)damage).ToString(),
            FontSize    = 72,
            Modulate    = new Color(1f, 0.12f, 0.08f, 1f),
            Billboard   = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            // Slight random offset so stacked hits don't perfectly overlap
            Position    = worldPos + new Vector3(GD.Randf() * 0.5f - 0.25f, 0f, GD.Randf() * 0.3f - 0.15f),
        };
        GetTree().Root.AddChild(label);

        var tween = label.CreateTween();
        tween.TweenProperty(label, "position:y", label.Position.Y + 1.8f, 0.85f)
             .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Quad);
        tween.Parallel()
             .TweenProperty(label, "modulate:a", 0f, 0.5f)
             .SetDelay(0.35f);
        tween.TweenCallback(Callable.From(label.QueueFree));
    }

    static SwordCombat? FindSwordCombat(Node3D body)
    {
        foreach (Node child in body.GetChildren())
            if (child is SwordCombat sc) return sc;
        return null;
    }
}
