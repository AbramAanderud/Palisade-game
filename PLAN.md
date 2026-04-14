# Game Plan: Palisade

## Game Description

Players design modular dungeons using snap-together pieces (straight hall, L-hall, T-junction, stairs). Before matchmaking, a player builds up to 5 saved mazes and selects the best one. Matchmaking pairs players by gold spent. Both mazes are joined at a small central weapon room or "arena". The game plays first-person: race to find your exit, claim the weapon, hunt the opponent. First out earns 50 gold; killing the opponent earns +50 (total 100). Winning by kill earns 200 gold. Gold funds new maze pieces.

## Piece System

Each piece occupies exactly one 10×10m grid cell. Openings are fixed at the center of each face (N/S/E/W). All pieces fit together automatically — no geometry math needed.

| Piece    | Openings (rot=0)            | Cost |
| -------- | --------------------------- | ---- |
| Start    | S only                      | free |
| Exit     | N only                      | free |
| Straight | N + S                       | free |
| LHall    | N + E                       | free |
| THall    | N + E + S                   | 25g  |
| Stairs   | S (floor F) + N (floor F+1) | 50g  |

Openings rotate clockwise with the piece.

## Risk Tasks

### 1. Modular Piece → 3D Dungeon Mesh

- **Why isolated:** Each piece maps to a pre-defined 3D mesh segment. Must get winding, UVs, and seams correct at piece boundaries. Openings must align at face centers.
- **Aesthetic:** Liminal / backrooms feel — very tall ceilings, nearly full-height openings, pale off-white walls, harsh flat lighting. Oppressive scale.
- **Dimensions:** Cell = 10m × 10m footprint. Ceiling height = 6m (tall and liminal). Opening gap = 3m wide × 5.5m tall, centered on face. Floor F+1 starts at Y = 6m (floor-to-floor height = 6m). Stairs ramp from Y=0 at S face to Y=6m at N face across 10m depth (~31° incline).
- **Approach:** Per piece type, procedurally build walls/floor/ceiling using SurfaceTool quads. Call GenerateNormals(). UV = world position / tileSize for seamless tiling. StaticBody3D with ConcavePolygonShape3D for collision (mesh shape, not CSG). No textures for risk prototype — solid color materials only.
- **Verify:** Screenshot from inside generated dungeon — tall corridor visible, opening gaps align between pieces, floor and ceiling present, no holes, no inside-out faces.

### 2. First-Person Player Controller

- **Why isolated:** Mouse look + capsule in narrow (3m wide) corridors. Clamp, gravity, and step-through are finicky.
- **Approach:** CharacterBody3D GROUNDED mode. CapsuleShape3D radius=0.35m height=1.6m. Mouse captured in \_Ready(). Horizontal yaw on root node, vertical pitch on Camera3D child (clamped ±85°). Gravity constant. MoveAndSlide() each physics tick.
- **Verify:** Walk all 4 directions, 360° horizontal look, vertical clamp, no clipping through walls in 3m-wide corridors.

## Main Build

Use verified risk systems + existing piece editor to assemble full game loop.

- Map editor redesign: piece-based drag/snap system with 5 save slots + select-for-matchmaking
- Main menu: Play (→ map selection), Map Builder, Settings
- Map selection screen: shows saved mazes, gold spent, Select for matchmaking button
- Dungeon assembly: load both players' mazes, join Exit-to-Exit via a connector corridor (see Dual-Maze Connection below)
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

## Dual-Maze Connection Architecture

### Layout

```
[Player A Maze]          [Connector]         [Player B Maze]
  Start (row 0)                                Start (row 0)
  ↓  (corridors)                               ↓  (corridors)
  Exit (row 9) → [ 6m wide bridge corridor ] ← Exit (row 9)
```

### Implementation Plan

1. **Maze A** is built normally at world offset (0, 0, 0).
2. **Maze B** is flipped 180° (rotated around Y) and placed so its Exit
   aligns with Maze A's Exit. The bridge between them is a straight 6m-wide
   corridor segment, length = 1 cell (10 m). Both exit openings face this
   bridge.
3. **World offset for Maze B**:
   - Maze B is placed at `z = GridH * CellSize + BridgeLen` (south of Maze A),
     then its pieces are rendered with `z' = offset - piece.Y * CellSize`
     (mirror Z) so its row 0 is farthest from the bridge and row 9 (Exit) faces
     Maze A's Exit.
4. **Bridge corridor**: a single straight N-S corridor piece placed between the
   two exits at `z = GridH * CellSize` (Maze A's exit face) to `z + CellSize`
   (Maze B's exit face). The weapon pickup spawns in the center of the bridge.
5. **Single DungeonGame scene** hosts both maze builds and the bridge, sharing
   one physics world. Both players spawn at their respective Start pieces.

### Key Design Constraints (enforced by editor)

- **Start** piece: row 0 only, floor 0 only → always at the far end of the maze.
- **Exit** piece: row GridH-1 only, floor 0 only → always at the connecting end.
- Both constraints enforced in `PlaceNew()` in MapEditorMain.cs.

### Data Flow

```
GameState.SlotA (int)   → MazeSerializer.Load(SlotA) → DungeonBuilder.Build(dataA, offsetA, flipA=false)
GameState.SlotB (int)   → MazeSerializer.Load(SlotB) → DungeonBuilder.Build(dataB, offsetB, flipB=true)
Bridge corridor         → DungeonBuilder.BuildBridge(exitA_pos, exitB_pos)
```

### Next Implementation Steps (Risk 3)

- Add `Build(MazeData, Vector3 offset, bool flipZ)` overload to DungeonBuilder
- Add `BuildBridge(Vector3 posA, Vector3 posB)` helper
- Add `GameState.SlotA`, `GameState.SlotB` fields
- Add maze-selection screen so each player picks their slot before entering

## Task Status

- [x] Piece system data layer (PieceDB, MazeData, MazeSerializer)
- [x] Map editor rewrite (piece placement, save slots)
- [x] Risk 1: Piece → 3D Mesh
- [x] Risk 2: First-Person Controller (ULTRAKILL movement — slide, slide-jump, wall-run, wall-jump)
- [ ] Main Build
- [ ] Presentation Video
