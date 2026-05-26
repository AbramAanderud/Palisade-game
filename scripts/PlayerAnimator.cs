using System;
using Godot;
using System.Collections.Generic;

/// res://scripts/PlayerAnimator.cs
/// Drives the visible player body animations.
/// Attach as child of PlayerController; call Tick() from _PhysicsProcess.
///
/// Loads animations from UAL1 and UAL2 GLBs (Quaternius CC0) at runtime,
/// merges them into the character's AnimationPlayer, then builds an
/// AnimationTree state machine so locomotion blends smoothly.
public partial class PlayerAnimator : Node
{
    // ── State enum ────────────────────────────────────────────────────────────
    public enum AnimState
    {
        Idle, Walk, Run, Jump, Fall, Slide, SlideLoop, SlideExit, WallRun,
        SwordSwing, SwordSwing2, SwordSwing3, Block, AerialLunge
    }

    AnimState _state = AnimState.Idle;

    AnimationPlayer? _ap;
    AnimationTree? _animTree;
    AnimationNodeStateMachinePlayback? _smPlayback;
    readonly HashSet<string> _validSmNodes = new();

    // Internal clip names we store in the merged AnimationPlayer library.
    static readonly Dictionary<AnimState, string> AnimNames = new()
    {
        [AnimState.Idle]        = "idle",
        [AnimState.Walk]        = "walk",
        [AnimState.Run]         = "run",
        [AnimState.Jump]        = "jump",
        [AnimState.Fall]        = "fall",
        [AnimState.Slide]       = "slide",
        [AnimState.SlideLoop]   = "slide_loop",
        [AnimState.SlideExit]   = "slide_exit",
        [AnimState.WallRun]     = "sprint",
        [AnimState.SwordSwing]  = "attack1",
        [AnimState.SwordSwing2] = "attack2",
        [AnimState.SwordSwing3] = "attack3",
        [AnimState.Block]       = "block",
        [AnimState.AerialLunge] = "air_slash",
    };

    static readonly Dictionary<AnimState, string> StateToSmNode = new()
    {
        [AnimState.Idle]        = "Locomotion",
        [AnimState.Walk]        = "Locomotion",
        [AnimState.Run]         = "Locomotion",
        [AnimState.Jump]        = "Jump",
        [AnimState.Fall]        = "Fall",
        [AnimState.Slide]       = "Slide",
        [AnimState.SlideLoop]   = "SlideLoop",
        [AnimState.SlideExit]   = "SlideExit",
        [AnimState.WallRun]     = "WallRun",
        [AnimState.SwordSwing]  = "Swing1",
        [AnimState.SwordSwing2] = "Swing2",
        [AnimState.SwordSwing3] = "Swing3",
        [AnimState.Block]       = "Block",
        [AnimState.AerialLunge] = "AerialLunge",
    };

    // ── UAL paths ─────────────────────────────────────────────────────────────
    const string Ual1Path =
        "res://playerModels/animations/Universal Animation Library[Standard]/Unreal-Godot/UAL1_Standard.glb";
    const string Ual2Path =
        "res://playerModels/animations/Universal Animation Library 2[Standard]/Unreal-Godot/UAL2_Standard.glb";

    // UAL1 clips to import: GLB clip name → our internal name
    static readonly Dictionary<string, string> Ual1Clips = new()
    {
        ["Idle"]       = "idle",
        ["Walk"]       = "walk",
        ["Jog_Fwd"]    = "run",
        ["Sprint"]     = "sprint",
        ["Death01"]    = "death",
        ["Sword_Idle"] = "sword_idle",
    };

    // UAL2 clips to import: GLB clip name → our internal name
    static readonly Dictionary<string, string> Ual2Clips = new()
    {
        ["NinjaJump_Start"] = "jump",
        ["NinjaJump_Idle"]  = "fall",
        ["NinjaJump_Land"]  = "land",
        ["Slide_Start"]     = "slide",
        ["Slide"]           = "slide_loop",
        ["Slide_Exit"]      = "slide_exit",
        ["Sword_Regular_A"] = "attack1",
        ["Sword_Regular_B"] = "attack2",
        ["Sword_Regular_C"] = "attack3",
        ["Sword_Block"]     = "block",
        ["Sword_Dash_RM"]   = "air_slash",
        ["Hit_Knockback"]   = "hit",
    };

    Node3D?         _modelRoot;
    AnimationPlayer? _fpAp;   // FP arms AnimationPlayer (optional — null until InitFpArms is called)

    // ── Init ─────────────────────────────────────────────────────────────────
    /// Call after placing UAL1_FpArms.glb as a camera-space child.
    /// Loads the same animation aliases into the arm model's AnimationPlayer and
    /// mirrors all subsequent playback calls to it automatically.
    public void InitFpArms(Node3D fpArmsRoot)
    {
        _fpAp = FindAnimationPlayer(fpArmsRoot);
        if (_fpAp == null)
        {
            GD.PrintErr("[PlayerAnimator] FpArms: no AnimationPlayer found");
            return;
        }
        PrepareBaseLibrary(_fpAp);
        LoadUalAnimations(_fpAp);
        GD.Print("[PlayerAnimator] FpArms AnimationPlayer wired");
    }

    public void Init(Node3D modelRoot)
    {
        _modelRoot = modelRoot;
        _ap = FindAnimationPlayer(modelRoot);
        if (_ap == null)
        {
            GD.Print("[PlayerAnimator] No AnimationPlayer found — using procedural motion.");
            return;
        }

        PrepareBaseLibrary(_ap);
        LoadUalAnimations(_ap);
        BuildAnimationTree(_ap);
    }

    // ── Library prep ─────────────────────────────────────────────────────────
    // Make the imported library mutable. Preserve existing clips — if the character
    // IS the UAL1 mannequin it already has its locomotion clips; we only add UAL2 extras.
    void PrepareBaseLibrary(AnimationPlayer ap)
    {
        AnimationLibrary lib;
        if (ap.HasAnimationLibrary(""))
        {
            lib = (AnimationLibrary)ap.GetAnimationLibrary("").Duplicate(true);
            ap.RemoveAnimationLibrary("");
        }
        else
        {
            lib = new AnimationLibrary();
        }
        ap.AddAnimationLibrary("", lib);
    }

    // ── UAL loading ───────────────────────────────────────────────────────────
    // Add our internal-name aliases for all clips.  For UAL1 clips the character may
    // already have them under their original names; we add our lowercased aliases on top.
    void LoadUalAnimations(AnimationPlayer ap)
    {
        if (!ap.HasAnimationLibrary("")) return;
        var lib = ap.GetAnimationLibrary("");
        CopyClipsFromGlb(Ual1Path, Ual1Clips, lib);
        CopyClipsFromGlb(Ual2Path, Ual2Clips, lib);
    }

    void CopyClipsFromGlb(string path, Dictionary<string, string> clipMap, AnimationLibrary lib)
    {
        var packed = GD.Load<PackedScene>(path);
        if (packed == null)
        {
            GD.PrintErr($"[PlayerAnimator] Could not load: {path}");
            return;
        }
        var tempRoot = packed.Instantiate<Node>();
        var srcAp = FindAnimationPlayer(tempRoot);
        if (srcAp == null)
        {
            GD.PrintErr($"[PlayerAnimator] No AnimationPlayer in: {path}");
            tempRoot.Free();
            return;
        }

        foreach (var (srcName, dstName) in clipMap)
        {
            if (!srcAp.HasAnimation(srcName))
            {
                GD.PrintErr($"[PlayerAnimator] Clip not found: '{srcName}' in {path}");
                continue;
            }
            var anim = (Animation)srcAp.GetAnimation(srcName).Duplicate(true);
            // Root-motion clips (_RM suffix) contain Position3D tracks that physically
            // translate the mesh away from the CharacterBody3D origin. Strip them so
            // our physics-based movement drives all translation and the mesh never drifts.
            if (dstName == "air_slash") StripPosition3DTracks(anim);
            if (lib.HasAnimation(dstName)) lib.RemoveAnimation(dstName);
            lib.AddAnimation(dstName, anim);
        }

        tempRoot.Free();
    }

    static void StripPosition3DTracks(Animation anim)
    {
        for (int i = anim.GetTrackCount() - 1; i >= 0; i--)
            if (anim.TrackGetType(i) == Animation.TrackType.Position3D)
                anim.RemoveTrack(i);
    }

    // ── AnimationTree construction ────────────────────────────────────────────
    void BuildAnimationTree(AnimationPlayer ap)
    {
        var sm = new AnimationNodeStateMachine();

        // Locomotion blend space: 0.0 → idle, 0.35 → walk, 1.0 → run/sprint
        var loco = new AnimationNodeBlendSpace1D();
        bool locoHasPoints = false;
        foreach (var (state, blendPos) in new (AnimState, float)[] {
            (AnimState.Idle, 0.0f), (AnimState.Walk, 0.35f), (AnimState.Run, 1.0f) })
        {
            if (AnimNames.TryGetValue(state, out var clip) && ap.HasAnimation(clip))
            {
                loco.AddBlendPoint(new AnimationNodeAnimation { Animation = clip }, blendPos);
                locoHasPoints = true;
            }
        }
        if (locoHasPoints)
        {
            sm.AddNode("Locomotion", loco, new Vector2(-200f, 0f));
            _validSmNodes.Add("Locomotion");
        }

        // One-shot states — only added when the clip loaded successfully
        var added = new HashSet<string>(_validSmNodes);
        foreach (var (state, nodeName) in StateToSmNode)
        {
            if (nodeName == "Locomotion" || added.Contains(nodeName)) continue;
            if (AnimNames.TryGetValue(state, out var clip) && ap.HasAnimation(clip))
            {
                sm.AddNode(nodeName, new AnimationNodeAnimation { Animation = clip }, Vector2.Zero);
                _validSmNodes.Add(nodeName);
                added.Add(nodeName);
            }
        }

        // Hub-and-spoke: every state ↔ Locomotion
        var blendReturn = new AnimationNodeStateMachineTransition { XfadeTime = 0.2f };
        var snapEnter   = new AnimationNodeStateMachineTransition { XfadeTime = 0.05f };
        foreach (var nodeName in _validSmNodes)
        {
            if (nodeName == "Locomotion") continue;
            if (_validSmNodes.Contains("Locomotion"))
            {
                sm.AddTransition("Locomotion", nodeName, snapEnter);
                sm.AddTransition(nodeName, "Locomotion", blendReturn);
            }
        }
        foreach (var (from, to) in new[] {
            ("Swing1", "Swing2"), ("Swing2", "Swing3"),
            ("Slide", "SlideLoop"), ("SlideLoop", "SlideExit") })
            if (_validSmNodes.Contains(from) && _validSmNodes.Contains(to))
                sm.AddTransition(from, to, snapEnter);

        var animTree = new AnimationTree
        {
            Name       = "AnimationTree",
            TreeRoot   = sm,
            AnimPlayer = ap.GetPath(),
            Active     = true,
        };
        _modelRoot!.GetParent().AddChild(animTree);
        _animTree = animTree;
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

    // ── Static NPC helpers (used by EnemyController) ──────────────────────────
    static readonly Dictionary<string, string> NpcUal1Clips = new()
    {
        ["Idle"]    = "idle",
        ["Walk"]    = "walk",
        ["Death01"] = "death",
    };
    static readonly Dictionary<string, string> NpcUal2Clips = new()
    {
        ["Sword_Regular_A"] = "attack1",
        ["Sword_Block"]     = "block",
    };

    public static AnimationPlayer? FindAnimationPlayerStatic(Node root)
    {
        if (root is AnimationPlayer ap) return ap;
        foreach (Node child in root.GetChildren())
        {
            var found = FindAnimationPlayerStatic(child);
            if (found != null) return found;
        }
        return null;
    }

    public static void SetupNpcAnimations(AnimationPlayer ap)
    {
        AnimationLibrary lib;
        if (ap.HasAnimationLibrary(""))
        {
            lib = (AnimationLibrary)ap.GetAnimationLibrary("").Duplicate(true);
            ap.RemoveAnimationLibrary("");
        }
        else
        {
            lib = new AnimationLibrary();
        }
        ap.AddAnimationLibrary("", lib);
        CopyClipsFromGlbStatic(Ual1Path, NpcUal1Clips, lib);
        CopyClipsFromGlbStatic(Ual2Path, NpcUal2Clips, lib);
    }

    static void CopyClipsFromGlbStatic(string path, Dictionary<string, string> clipMap, AnimationLibrary lib)
    {
        var packed = GD.Load<PackedScene>(path);
        if (packed == null) { GD.PrintErr($"[NpcAnim] Could not load: {path}"); return; }
        var tempRoot = packed.Instantiate<Node>();
        var srcAp    = FindAnimationPlayerStatic(tempRoot);
        if (srcAp == null) { GD.PrintErr($"[NpcAnim] No AnimationPlayer in: {path}"); tempRoot.Free(); return; }
        foreach (var (srcName, dstName) in clipMap)
        {
            if (!srcAp.HasAnimation(srcName)) { GD.PrintErr($"[NpcAnim] Clip not found: '{srcName}'"); continue; }
            var anim = (Animation)srcAp.GetAnimation(srcName).Duplicate(true);
            if (lib.HasAnimation(dstName)) lib.RemoveAnimation(dstName);
            lib.AddAnimation(dstName, anim);
        }
        tempRoot.Free();
    }

    // Lazily resolved on the first Tick after AnimationTree is ready.
    void EnsurePlayback()
    {
        if (_smPlayback != null || _animTree == null) return;
        _smPlayback = _animTree.Get("parameters/playback").As<AnimationNodeStateMachinePlayback>();
        if (_smPlayback == null) return;

        string startNode = _validSmNodes.Contains("Locomotion") ? "Locomotion"
                         : _validSmNodes.Count > 0 ? System.Linq.Enumerable.First(_validSmNodes)
                         : "";
        if (startNode.Length > 0)
            _smPlayback.Start(startNode);
    }

    // ── Per-frame tick ────────────────────────────────────────────────────────
    public void Tick(float dt, bool onFloor, bool sliding, bool wallRunning,
                     float wallRollSign, bool hasWeapon, float speed, float verticalVel)
    {
        EnsurePlayback();

        bool slideJustStarted = sliding  && !_prevSliding;
        bool slideJustEnded   = !sliding && _prevSliding;
        if (slideJustStarted) _slidePhase = 0f;
        if (sliding)          _slidePhase += dt;
        _prevSliding = sliding;

        if (_combatLock)
        {
            _combatLockTimer -= dt;
            if (_combatLockTimer <= 0f)
            {
                _combatLock = false;
                // Return to locomotion once the combat animation finishes
                _state = DetermineState(onFloor, sliding, wallRunning, speed, verticalVel);
                PlayState(_state, speed);
            }
        }
        else
        {
            var next = DetermineState(onFloor, sliding, wallRunning, speed, verticalVel);
            if (slideJustEnded) next = AnimState.SlideExit;

            if (next != _state)
            {
                _state = next;
                PlayState(_state, speed);
            }
            else if (_state is AnimState.Idle or AnimState.Walk or AnimState.Run)
            {
                UpdateLocoBlend(speed);
                UpdateFpArmsLocoClip(speed);
            }
        }

        if (_modelRoot != null)
            ApplyProceduralMotion(dt, onFloor, sliding, wallRunning, wallRollSign, speed);
    }

    AnimState DetermineState(bool onFloor, bool sliding, bool wallRunning,
                              float speed, float verticalVel)
    {
        if (wallRunning)  return AnimState.WallRun;
        if (sliding)      return AnimState.SlideLoop;
        if (!onFloor)     return verticalVel > 0f ? AnimState.Jump : AnimState.Fall;
        if (speed > 7f)   return AnimState.Run;
        if (speed > 0.5f) return AnimState.Walk;
        return AnimState.Idle;
    }

    void PlayState(AnimState state, float speed = 0f)
    {
        // Restore any direct-playback freeze (block guard pose, aerial lunge hold)
        if (_ap   != null && _ap.SpeedScale   == 0f) _ap.SpeedScale   = 1f;
        if (_fpAp != null && _fpAp.SpeedScale == 0f) _fpAp.SpeedScale = 1f;
        if (_animTree != null && !_animTree.Active)  _animTree.Active  = true;

        if (_smPlayback != null && StateToSmNode.TryGetValue(state, out var smNode))
        {
            string target = _validSmNodes.Contains(smNode) ? smNode
                          : _validSmNodes.Contains("Locomotion") ? "Locomotion" : "";
            if (target.Length > 0)
            {
                _smPlayback.Travel(target);
                if (target == "Locomotion") UpdateLocoBlend(speed);
            }
        }
        else if (_ap != null && AnimNames.TryGetValue(state, out var directName))
        {
            if (_ap.HasAnimation(directName)) _ap.Play(directName);
        }

        // Mirror to FP arms AnimationPlayer (no AnimationTree — simple clip play)
        if (_fpAp != null && AnimNames.TryGetValue(state, out var fpName))
        {
            // For locomotion, pick the closest discrete clip rather than blend-space
            if (state is AnimState.Idle or AnimState.Walk or AnimState.Run)
                UpdateFpArmsLocoClip(speed);
            else if (_fpAp.HasAnimation(fpName))
                _fpAp.Play(fpName);
        }
    }

    void UpdateLocoBlend(float speed)
    {
        if (_animTree == null || !_validSmNodes.Contains("Locomotion")) return;
        _animTree.Set("parameters/Locomotion/blend_position", Mathf.Clamp(speed / 11f, 0f, 1f));
    }

    void UpdateFpArmsLocoClip(float speed)
    {
        if (_fpAp == null) return;
        string clip = speed > 7f ? "run" : speed > 0.5f ? "walk" : "idle";
        if (_fpAp.HasAnimation(clip) && _fpAp.CurrentAnimation != clip)
            _fpAp.Play(clip);
    }

    // ── Slide phase tracking ─────────────────────────────────────────────────
    float _slidePhase   = 0f;
    bool  _prevSliding  = false;
    const float SlideEntryDur = 0.35f;  // seconds before transitioning to slide_loop

    // ── Combat animation lock ─────────────────────────────────────────────────
    // Prevents locomotion DetermineState from overriding a playing combat animation.
    bool  _combatLock      = false;
    float _combatLockTimer = 0f;

    // Total durations (Windup+HitWindow+Recovery) for each combo step, matching SwordCombat constants
    static readonly float[] SwingDurations = { 0.40f, 0.36f, 0.70f };

    // ── Procedural motion: bob, tilt, lean ───────────────────────────────────
    float _bobTimer   = 0f;
    float _prevBobSin = 0f;
    float _tiltAngle  = 0f;
    float _leanAngle  = 0f;

    /// Fires once per footfall with the player's horizontal speed.
    public Action<float>? StepTaken;

    void ApplyProceduralMotion(float dt, bool onFloor, bool sliding, bool wallRunning,
                               float wallRollSign, float speed)
    {
        if (_modelRoot == null) return;

        if ((onFloor || wallRunning) && !sliding && speed > 0.5f)
        {
            _bobTimer += dt * (speed > 7f ? 14f : 9f);
            float sinNow = Mathf.Sin(_bobTimer);
            float bob    = sinNow * 0.04f;
            _modelRoot.Position = _modelRoot.Position with { Y = bob };

            // Fire StepTaken once per full bob cycle (negative→positive crossing only)
            if (_prevBobSin < 0f && sinNow >= 0f)
                StepTaken?.Invoke(speed);
            _prevBobSin = sinNow;
        }
        else
        {
            _bobTimer   = 0f;
            _prevBobSin = 0f;
            _modelRoot.Position = _modelRoot.Position with
            {
                Y = Mathf.MoveToward(_modelRoot.Position.Y, 0f, 3f * dt)
            };
        }

        float targetTilt = sliding ? 0.35f : 0f;
        _tiltAngle = Mathf.MoveToward(_tiltAngle, targetTilt, 4f * dt);

        // Lean toward whichever wall we're running along.
        // wallRollSign: +1 = wall on right, lean right; -1 = wall on left, lean left.
        // Model has 180° Y rotation so Z-lean sign is negated relative to world.
        float targetLean = wallRunning ? -wallRollSign * 0.25f : 0f;
        _leanAngle = Mathf.MoveToward(_leanAngle, targetLean, 5f * dt);

        _modelRoot.Rotation = new Vector3(_tiltAngle, _modelRoot.Rotation.Y, _leanAngle);
    }

    // ── Combat animation hooks ────────────────────────────────────────────────
    public void PlayAttack(int comboStep)
    {
        var state = comboStep switch
        {
            1 => AnimState.SwordSwing2,
            2 => AnimState.SwordSwing3,
            _ => AnimState.SwordSwing
        };
        _state           = state;
        _combatLock      = true;
        _combatLockTimer = SwingDurations[Mathf.Clamp(comboStep, 0, 2)];
        PlayState(state);
    }

    // How long to let the block animation play before freezing at the guard pose.
    // Tune this to match the rise portion of the Sword_Block clip.
    const float BlockRiseDur = 0.25f;
    bool _blockHeld = false;

    public void PlayBlock(bool blocking)
    {
        if (blocking)
        {
            if (_blockHeld) return;
            _blockHeld       = true;
            _state           = AnimState.Block;
            _combatLock      = true;
            _combatLockTimer = float.MaxValue;

            // Bypass AnimationTree so we can freeze the AnimationPlayer at the peak frame.
            // AnimationTree overrides _ap every frame, so Stop() only works with the tree off.
            if (_animTree != null) _animTree.Active = false;
            _ap?.Play("block");
            _fpAp?.Play("block");

            // After the raise completes, freeze at the guard pose using SpeedScale=0.
            // More reliable than Stop(false) for holding a mid-animation pose.
            GetTree().CreateTimer(BlockRiseDur).Timeout += () =>
            {
                if (!_blockHeld) return;   // released before timer fired
                if (_ap   != null) _ap.SpeedScale   = 0f;
                if (_fpAp != null) _fpAp.SpeedScale = 0f;
            };
        }
        else
        {
            _blockHeld       = false;
            _combatLock      = false;
            _combatLockTimer = 0f;
            if (_ap   != null) _ap.SpeedScale   = 1f;   // restore before re-enabling tree
            if (_fpAp != null) _fpAp.SpeedScale = 1f;
            if (_animTree != null) _animTree.Active = true;
        }
    }


    public void PlayAerialLunge()
    {
        _state           = AnimState.AerialLunge;
        _combatLock      = true;
        _combatLockTimer = 2.0f;

        if (_ap == null || !_ap.HasAnimation("air_slash")) return;

        // Bypass AnimationTree for precise timing control (same pattern as PlayBlock).
        if (_animTree != null) _animTree.Active = false;
        _ap.SpeedScale = 1f;
        _ap.Play("air_slash");

        // Freeze at the forward-slash peak (~40% through) before the return phase begins.
        float cutTime = (_ap.GetAnimation("air_slash")?.Length ?? 0.7f) * 0.40f;
        GetTree().CreateTimer(cutTime).Timeout += () =>
        {
            if (_state == AnimState.AerialLunge && _ap != null)
                _ap.SpeedScale = 0f;
        };
    }

    public void PlayDeath()
    {
        if (_animTree != null) _animTree.Active = false;
        if (_ap != null && _ap.HasAnimation("death"))
            _ap.Play("death");
    }
}
