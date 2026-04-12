# Game Plan: Palisade

## Game Description

Top-down 2D dungeon map editor where each player designs a 10×10 tile maze (with multi-floor support via stairs). When matched, both players' mazes are connected via a central circular room containing a randomly-rolled weapon. The game is then played first-person: players are placed in each other's mazes and race to find the exit. The first player out claims the weapon and enters the opponent's maze to hunt them. If the hunted player survives 30 seconds they earn 100 gold; the hunter earns 200 gold on a kill. Gold is spent on maze upgrades. Players are matched by gold spent.

## Risk Tasks

### 1. Map → 3D Dungeon Mesh Generation
- **Why isolated:** Runtime procedural mesh from 2D tile array. Easy to get wrong winding, UV tiling, or seams at tile boundaries. Must produce walkable geometry with correct normals for lighting.
- **Approach:** Per-tile approach: for each Floor/Entrance/Exit tile, emit floor quad + ceiling quad. For each Wall tile, emit 4 side-wall quads only where the neighbor is open (no wall face where two walls meet). Use SurfaceTool, call GenerateNormals(). UV = world XZ / tile_size for seamless tiling.
- **Verify:** Run headless, capture screenshot in generated dungeon — floor visible, walls block view, no holes, no inside-out faces (no magenta), normals correct (lighting on wall faces).

### 2. First-Person Player Controller
- **Why isolated:** FPS mouse look + CharacterBody3D collision in narrow corridors is finicky. Mouse sensitivity, vertical clamp, gravity, and corridor sliding all interact.
- **Approach:** CharacterBody3D with Camera3D child. Mouse captured in `_Ready()`. Horizontal rotation on Node root, vertical on Camera3D only (clamped ±85°). Gravity always applied. `MoveAndSlide()` in GROUNDED mode. Collision shape: CapsuleShape3D radius 0.35m height 1.6m.
- **Verify:** Player walks all 4 directions, mouse look 360° horizontal, vertical clamped, no clipping through walls in 1-tile-wide corridor.

## Main Build

Build the complete game loop using the existing 2D map editor (already scaffolded) and the two verified risk systems.

**What to build:**
- Central circular connecting room (procedural mesh, radius 4m, domed ceiling)
- Weapon pickup in center room (glowing sword, InteractableArea3D)
- Enemy placeholder (capsule mesh, NavigationAgent3D, simple path-to-player)
- Combat system: swing weapon on LMB, 1-hit kill, kill/death detection
- Survival timer (30s countdown, shown on HUD)
- Game state machine: Editor → Loading → Playing → Result
- HUD: HP bar, timer, gold counter, crosshair
- Result screen: winner/gold earned
- Map editor "Play" button that triggers dungeon generation + scene switch
- Gold persistence (simple file save)
- Texture assets: stone wall, floor, ceiling (generated)
- Torch OmniLight3D placements every 4 tiles along corridors

**Assets needed:**
- Stone brick wall texture (tileable, 512×512)
- Stone floor texture (tileable, 512×512)
- Sword pickup GLB (from reference image)

**Verify:**
- Movement direction matches player input
- Mouse look works, no gimbal lock
- Walls block movement in narrow corridors
- Weapon pickup triggers on enter, sword disappears from room
- Timer counts down 0:30 → 0:00
- Kill/survival detected and gold awarded correctly
- HUD elements readable, no overlap
- No missing textures (no magenta/checkerboard)
- No holes in dungeon mesh
- reference.png consistency: stone aesthetic, torch lighting, HUD layout
- **Presentation video:** ~30s cinematic MP4
  - Camera pans through editor, switches to 3D dungeon, walks toward central room, picks up sword, swings at enemy
  - Output: screenshots/presentation/gameplay.mp4

## Task Status

- [ ] Risk 1: Map → 3D Mesh Generation
- [ ] Risk 2: First-Person Player Controller
- [ ] Main Build
- [ ] Presentation Video
