using Godot;

/// res://scripts/SwordCombat.cs
/// First-pass sword combat state machine.
/// Owned and ticked by PlayerController when HasWeapon == true.
///
/// States:
///   Idle        — waiting for input
///   Swing1/2/3  — three-hit combo chain
///   Cooldown    — brief lockout after Swing3
///   Block       — RMB held; cancels swings, slows movement
///   SpinAttack  — aerial 360° spin (one-shot per airborne window)
public partial class SwordCombat : Node
{
    // ── Tunable constants ─────────────────────────────────────────────────────
    const float Swing1Duration  = 0.40f;   // seconds the Swing1 window stays open
    const float Swing2Duration  = 0.36f;   // seconds the Swing2 window stays open
    const float Swing3Duration  = 0.50f;   // seconds the Swing3 window stays open
    const float ComboInputZone  = 0.60f;   // fraction of window where follow-up input is accepted (last X%)
    const float CooldownDuration= 0.50f;   // lockout after Swing3 before returning to Idle
    const float SpinDuration    = 0.60f;   // total duration of SpinAttack
    const float SpinFullRot     = Mathf.Pi * 2f; // 360° in radians

    // ── State ─────────────────────────────────────────────────────────────────
    enum CombatState { Idle, Swing1, Swing2, Swing3, Block, SpinAttack, Cooldown }
    CombatState _state   = CombatState.Idle;
    float       _timer   = 0f;   // time remaining in current state
    bool        _spinUsed = false; // one spin per airborne window

    // ── Public API ────────────────────────────────────────────────────────────
    public bool IsBlocking { get; private set; } = false;
    public bool IsSpinning { get; private set; } = false;

    /// Called every physics frame by PlayerController.
    /// <param name="dt">Delta time in seconds.</param>
    /// <param name="isAirborne">True when player is not on the floor.</param>
    /// <param name="playerYaw">Current player yaw (unused here but available for future use).</param>
    /// <param name="yawDelta">Output: yaw rotation (radians) to apply this frame for spin.</param>
    public void Tick(float dt, bool isAirborne, bool isWallRunning, float playerYaw, out float yawDelta)
    {
        yawDelta = 0f;

        // Reset spin availability when player lands
        if (!isAirborne)
            _spinUsed = false;

        // ── Block takes priority in Idle / between swings ─────────────────────
        bool blockHeld = Input.IsActionPressed("block");

        if (blockHeld && _state is CombatState.Idle or CombatState.Swing1 or CombatState.Swing2 or CombatState.Swing3)
        {
            if (!IsBlocking)
            {
                GD.Print("[Combat] Block ON");
                IsBlocking = true;
            }
            _state = CombatState.Block;
            IsSpinning = false;
            return;
        }

        if (_state == CombatState.Block)
        {
            if (!blockHeld)
            {
                GD.Print("[Combat] Block OFF");
                IsBlocking = false;
                _state     = CombatState.Idle;
            }
            else
            {
                // Staying in block — consume dt and return
                return;
            }
        }

        // Ensure IsBlocking is clear outside Block state
        if (_state != CombatState.Block)
            IsBlocking = false;

        // ── Spin attack (jump pressed while airborne and not wall-running) ─────
        if (isAirborne && !isWallRunning && !_spinUsed
            && Input.IsActionJustPressed("jump")
            && _state == CombatState.Idle)
        {
            EnterSpin();
            _spinUsed = true;
        }

        // ── Tick active state ─────────────────────────────────────────────────
        switch (_state)
        {
            case CombatState.Idle:
                TickIdle();
                break;

            case CombatState.Swing1:
                TickSwing(CombatState.Swing2, Swing1Duration, ref yawDelta, dt);
                break;

            case CombatState.Swing2:
                TickSwing(CombatState.Swing3, Swing2Duration, ref yawDelta, dt);
                break;

            case CombatState.Swing3:
                TickSwing(CombatState.Cooldown, Swing3Duration, ref yawDelta, dt);
                break;

            case CombatState.Cooldown:
                _timer -= dt;
                if (_timer <= 0f)
                {
                    GD.Print("[Combat] Cooldown → Idle");
                    _state = CombatState.Idle;
                }
                break;

            case CombatState.SpinAttack:
                TickSpin(dt, ref yawDelta);
                break;
        }
    }

    // ── Idle: watch for LMB ───────────────────────────────────────────────────
    void TickIdle()
    {
        if (Input.IsActionJustPressed("attack"))
        {
            GD.Print("[Combat] Swing1");
            _state = CombatState.Swing1;
            _timer = Swing1Duration;
        }
    }

    // ── Generic swing tick ────────────────────────────────────────────────────
    // Advances to nextState when window expires or LMB is pressed in the combo zone.
    void TickSwing(CombatState nextState, float fullDuration, ref float yawDelta, float dt)
    {
        _timer -= dt;

        // Combo input zone: last (1 - ComboInputZone) fraction of the window
        float inputThreshold = fullDuration * (1f - ComboInputZone);
        bool  inComboZone    = _timer <= inputThreshold;

        if (inComboZone && Input.IsActionJustPressed("attack"))
        {
            AdvanceCombo(nextState);
            return;
        }

        if (_timer <= 0f)
        {
            // Window expired — no follow-up pressed, decide outcome
            if (nextState == CombatState.Cooldown)
            {
                // After Swing3 always go to Cooldown
                GD.Print("[Combat] Cooldown");
                _state = CombatState.Cooldown;
                _timer = CooldownDuration;
            }
            else
            {
                // Swing1 or Swing2 expired without input → back to Idle
                _state = CombatState.Idle;
            }
        }
    }

    void AdvanceCombo(CombatState nextState)
    {
        _state = nextState;
        switch (nextState)
        {
            case CombatState.Swing2:
                GD.Print("[Combat] Swing2");
                _timer = Swing2Duration;
                break;
            case CombatState.Swing3:
                GD.Print("[Combat] Swing3");
                _timer = Swing3Duration;
                break;
            case CombatState.Cooldown:
                GD.Print("[Combat] Cooldown");
                _timer = CooldownDuration;
                break;
        }
    }

    // ── Spin attack ───────────────────────────────────────────────────────────
    void EnterSpin()
    {
        GD.Print("[Combat] SpinAttack");
        _state     = CombatState.SpinAttack;
        _timer     = SpinDuration;
        IsSpinning = true;
    }

    void TickSpin(float dt, ref float yawDelta)
    {
        float rotThisFrame = (SpinFullRot / SpinDuration) * dt;
        yawDelta = rotThisFrame;

        _timer -= dt;
        if (_timer <= 0f)
        {
            IsSpinning = false;
            _state     = CombatState.Idle;
        }
    }
}
