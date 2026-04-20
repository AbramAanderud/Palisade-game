using Godot;

/// res://scripts/PlayerAnimator.cs
/// Drives the visible player body animations.
/// Attach as child of PlayerController; call Tick() from _PhysicsProcess.
///
/// Rascal.glb inspection result: 1 mesh, 0 skins, 0 animations.
/// Static mesh only — no AnimationPlayer available.
/// All motion is procedural (bob, tilt, lean) until rigged animations are added.
public partial class PlayerAnimator : Node
{
    // ── State enum ────────────────────────────────────────────────────────────
    public enum AnimState
    {
        Idle, Walk, Run, Jump, Fall, Slide, WallRun,
        SwordSwing, SwordSwing2, SwordSwing3, Block, SpinAttack
    }

    AnimState _state = AnimState.Idle;
    AnimationPlayer? _ap;   // null if no AnimationPlayer found (currently always null)

    // Animation name map — fill in real names once rigged animations are added to the GLB
    static readonly System.Collections.Generic.Dictionary<AnimState, string> AnimNames = new()
    {
        [AnimState.Idle]        = "idle",
        [AnimState.Walk]        = "walk",
        [AnimState.Run]         = "run",
        [AnimState.Jump]        = "jump",
        [AnimState.Fall]        = "fall",
        [AnimState.Slide]       = "slide",
        [AnimState.WallRun]     = "wallrun",
        [AnimState.SwordSwing]  = "attack1",
        [AnimState.SwordSwing2] = "attack2",
        [AnimState.SwordSwing3] = "attack3",
        [AnimState.Block]       = "block",
        [AnimState.SpinAttack]  = "spin",
    };

    Node3D? _modelRoot;   // the RascalModel node inside PlayerBody

    public void Init(Node3D modelRoot)
    {
        _modelRoot = modelRoot;
        // Find AnimationPlayer anywhere in model subtree
        _ap = FindAnimationPlayer(modelRoot);
        if (_ap != null)
            GD.Print($"[PlayerAnimator] AnimationPlayer found. Clips: {string.Join(", ", _ap.GetAnimationList())}");
        else
            GD.Print("[PlayerAnimator] No AnimationPlayer found — using procedural motion.");
    }

    AnimationPlayer? FindAnimationPlayer(Node root)
    {
        if (root is AnimationPlayer ap) return ap;
        foreach (Node child in root.GetChildren())
        {
            var found = FindAnimationPlayer(child);
            if (found != null) return found;
        }
        return null;
    }

    public void Tick(float dt, bool onFloor, bool sliding, bool wallRunning,
                     bool hasWeapon, float speed, float verticalVel)
    {
        var next = DetermineState(onFloor, sliding, wallRunning, speed, verticalVel);
        if (next != _state)
        {
            _state = next;
            PlayState(_state);
        }

        // Procedural motion fallback (always runs — adds feel even with AnimationPlayer)
        if (_modelRoot != null)
            ApplyProceduralMotion(dt, onFloor, sliding, wallRunning, speed);
    }

    AnimState DetermineState(bool onFloor, bool sliding, bool wallRunning,
                              float speed, float verticalVel)
    {
        if (wallRunning)  return AnimState.WallRun;
        if (sliding)      return AnimState.Slide;
        if (!onFloor)     return verticalVel > 0f ? AnimState.Jump : AnimState.Fall;
        if (speed > 7f)   return AnimState.Run;
        if (speed > 0.5f) return AnimState.Walk;
        return AnimState.Idle;
    }

    void PlayState(AnimState state)
    {
        if (_ap == null) return;
        if (!AnimNames.TryGetValue(state, out var name)) return;
        if (_ap.HasAnimation(name))
            _ap.Play(name);
        // silently skip if animation clip doesn't exist yet
    }

    // ── Procedural motion: bob, tilt, lean ───────────────────────────────────
    // Works with or without AnimationPlayer. Gives life to the static mesh in TP view.
    float _bobTimer  = 0f;
    float _tiltAngle = 0f;
    float _leanAngle = 0f;   // Z lean for wall-run

    void ApplyProceduralMotion(float dt, bool onFloor, bool sliding, bool wallRunning, float speed)
    {
        if (_modelRoot == null) return;

        // Walk/run bob on Y
        if (onFloor && speed > 0.5f)
        {
            _bobTimer += dt * (speed > 7f ? 14f : 9f);
            float bob = Mathf.Sin(_bobTimer) * 0.04f;
            _modelRoot.Position = _modelRoot.Position with { Y = bob };
        }
        else
        {
            _bobTimer = 0f;
            _modelRoot.Position = _modelRoot.Position with
            {
                Y = Mathf.MoveToward(_modelRoot.Position.Y, 0f, 3f * dt)
            };
        }

        // Slide: lean model forward on X
        float targetTilt = sliding ? 0.35f : 0f;
        _tiltAngle = Mathf.MoveToward(_tiltAngle, targetTilt, 4f * dt);

        // Wall-run: lean model sideways on Z
        float targetLean = wallRunning ? 0.25f : 0f;
        _leanAngle = Mathf.MoveToward(_leanAngle, targetLean, 5f * dt);

        _modelRoot.Rotation = new Vector3(_tiltAngle, _modelRoot.Rotation.Y, _leanAngle);
    }

    // ── Combat animation hooks ────────────────────────────────────────────────
    // Called by SwordCombat (or PlayerController relaying combat events) to
    // trigger attack state. comboStep: 0 = Swing1, 1 = Swing2, 2 = Swing3.
    public void PlayAttack(int comboStep)
    {
        var state = comboStep switch
        {
            1 => AnimState.SwordSwing2,
            2 => AnimState.SwordSwing3,
            _ => AnimState.SwordSwing
        };
        _state = state;
        PlayState(state);
    }

    public void PlayBlock(bool blocking)
    {
        if (blocking)
        {
            _state = AnimState.Block;
            PlayState(AnimState.Block);
        }
    }

    public void PlaySpin()
    {
        _state = AnimState.SpinAttack;
        PlayState(AnimState.SpinAttack);
    }
}
