# Game Plan: Palisade

## Game Description

Players design modular dungeons using snap-together pieces (straight hall, L-hall, T-junction, stairs). Before matchmaking, a player builds up to 5 saved mazes and selects the best one. Matchmaking pairs players by gold spent. Both mazes are joined at a small central weapon room. The game plays first-person: race to find your exit, claim the weapon, hunt the opponent. First out earns 50 gold; killing the opponent earns +50 (total 100). Winning by kill earns 200 gold. Gold funds new maze pieces.

## Piece System

Each piece occupies exactly one 10×10m grid cell. Openings are fixed at the center of each face (N/S/E/W). All pieces fit together automatically — no geometry math needed.

| Piece | Openings (rot=0) | Cost |
|-------|-----------------|------|
| Start | S only | free |
| Exit | N only | free |
| Straight | N + S | free |
| LHall | N + E | free |
| THall | N + E + S | 25g |
| Stairs | S (floor F) + N (floor F+1) | 50g |

Openings rotate clockwise with the piece.

## Risk Tasks

### 1. Modular Piece → 3D Dungeon Mesh
- **Why isolated:** Each piece maps to a pre-defined 3D mesh segment. Must get winding, UVs, and seams correct at piece boundaries. Openings must align at face centers.
- **Approach:** Per piece type, procedurally build walls/floor/ceiling using SurfaceTool quads. A 10m×10m×3m cell has 4 wall quads (each 10×3m), 1 floor quad, 1 ceiling quad, minus the opening faces. Opening = a 3m-wide × 2.5m-tall gap centered on the face. Stairs = ramp quad from floor F ground level to floor F+1 ground level at ~15° angle. Call GenerateNormals(). UV = world XZ/3 for seamless tiling.
- **Verify:** Screenshot from inside generated dungeon — floor/ceiling/walls visible, opening gaps align correctly between adjacent pieces, no holes, no inside-out faces.

### 2. First-Person Player Controller
- **Why isolated:** Mouse look + capsule in narrow (3m wide) corridors. Clamp, gravity, and step-through are finicky.
- **Approach:** CharacterBody3D GROUNDED mode. CapsuleShape3D radius=0.35m height=1.6m. Mouse captured in _Ready(). Horizontal yaw on root node, vertical pitch on Camera3D child (clamped ±85°). Gravity constant. MoveAndSlide() each physics tick.
- **Verify:** Walk all 4 directions, 360° horizontal look, vertical clamp, no clipping through walls in 3m-wide corridors.

## Main Build

Use verified risk systems + existing piece editor to assemble full game loop.

- Map editor redesign: piece-based drag/snap system with 5 save slots + select-for-matchmaking
- Main menu: Play (→ map selection), Map Builder, Settings
- Map selection screen: shows saved mazes, gold spent, Select for matchmaking button
- Dungeon assembly: load both players' mazes, attach exits to small central weapon room
- Weapon pickup: one iron sword in central room (Area3D detect), weapon disappears on pickup
- Enemy AI: simple NavigationAgent3D path-to-player
- Combat: swing weapon on LMB, 1-hit kill on enemy
- Survival timer: 30s countdown starts when first player exits
- Game state machine: Menu → MapSelect → Loading → Playing → Result
- HUD: HP dot indicator, timer (hidden until one player exits), gold counter
- Result screen: outcome text + gold earned + continue button
- Gold persistence: saved to user://profile.json

**Assets needed:**
- Stone wall texture (tileable 512×512)
- Stone floor texture (tileable 512×512)
- Iron sword sprite/model (for pickup + held weapon)

**Verify:**
- All 5 piece types snap correctly in editor
- Mazes save and load across sessions
- 3D dungeon generates from saved piece data, correctly joined to central room
- Movement, look, collision all correct
- Weapon pickup works, disappears from room
- Timer starts only after first player exits
- Gold awarded correctly for each outcome
- HUD readable, no overlap
- No missing textures
- reference.png consistency: stone dungeon, torch lighting, HUD layout
- **Presentation video:** ~30s MP4 — editor placing pieces → switch to 3D → walk → pick up sword → fight

## Task Status

- [x] Piece system data layer (PieceDB, MazeData, MazeSerializer)
- [x] Map editor rewrite (piece placement, save slots)
- [ ] Risk 1: Piece → 3D Mesh
- [ ] Risk 2: First-Person Controller
- [ ] Main Build
- [ ] Presentation Video
