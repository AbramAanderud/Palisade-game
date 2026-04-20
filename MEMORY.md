# Palisade — Build Memory

## Player Setup (2026-04-20)

### Rascal.glb inspection
- Located at `res://playerModels/rascal-cat/Rascal.glb`
- Contents: 1 mesh, 0 skins, 0 animations — static mesh only
- No AnimationPlayer available; procedural motion path is active

### Working configuration
- `ModelScale = 3.0f` — correct visual size, readable against 2m reference objects
- `ModelYOff = -0.05f` — feet flush at capsule base
- `model.RotationDegrees = new Vector3(0f, 180f, 0f)` — correct front-facing orientation
- FP mode: model hidden, eye height 1.45m, FOV 90°, near 0.05m (no self-clip)
- TP mode: `TpDistance = 4.0f`, `TpElevation = 1.8f` — model fills frame well

### PlayerAnimator
- `scripts/PlayerAnimator.cs` — procedural motion: walk bob (9 Hz), run bob (14 Hz), slide tilt (0.35 rad), wall-run lean (0.25 rad Z)
- All 12 AnimState slots stubbed; `AnimNames` dict ready for real clip names once a rigged GLB is provided
- `_ap` auto-discovery will activate when rigged GLB is dropped in — no code change needed

### Capture workflow (Windows)
- Binary: `C:/Godot_v4.6.2-stable_mono_win64/Godot_v4.6.2-stable_mono_win64.exe`
- Must set `project.godot run/main_scene` to test .tscn before capture, restore after
- Test harness pattern: `partial class TestXMain : Node3D` (not SceneTree)
- `--fixed-fps 10 --quit-after N` for physics-based tests

### Verified in-engine
- ✅ Model appears at correct scale
- ✅ First-person default, no self-clipping
- ✅ Third-person toggle on `3` shows Rascal model
- ✅ Procedural animator connected and printing confirmation at startup
- ✅ Physics: player falls and lands on floor correctly
