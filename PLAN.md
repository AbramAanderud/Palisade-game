# Game Plan: Palisade

## Vision

Two players each build and run their own maze, meet in a central arena, and fight. The loop: survive your maze → escape → grab the arena sword → hunt your opponent. Over time, players craft builds from trinket chests and choose what sword waits in the middle.

## Core Gameplay Loop (Playtest Target)

1. Each player runs through their generated maze (first-person, fast movement)
2. After 20s, torches near the exit start glowing blue (BFS wave outward) — signal to find the exit
3. Exit the maze → earn **100 gold**
4. 30-second arena phase begins
5. Grab the **two-handed arena sword** (30 dmg) → opponent highlighted red for 5s (wall-hack)
6. Kill opponent within 30s → earn **+50 gold** + prestige currency
7. Use prestige currency to buy cosmetic/trinket chests and choose what sword appears in the next arena

## Piece System

Each piece occupies exactly one 10×10m grid cell. Openings are fixed at the center of each face (N/S/E/W).

| Piece      | Openings (rot=0)            | Cost |
| ---------- | --------------------------- | ---- |
| Start      | S only                      | free |
| Exit       | N only                      | free |
| Straight   | N + S                       | free |
| LHall      | N + E                       | free |
| THall      | N + E + S                   | 25g  |
| Cross      | N + E + S + W               | 40g  |
| StairsUp   | N (up) + S (flat)           | 50g  |
| StairsDown | N (flat) + S (down)         | 50g  |

## Combat

- **Starter sword** (spawn item, weaker model — distinct from arena sword): 23 damage per hit, 3-hit combo. Every player spawns with one at match start; no pickup required.
- **Arena two-hander** (center of arena, stronger model): 30 damage per hit
- Player health: **100 HP**, shown as world-space bar above head + HUD bar bottom-left
- Floating red damage numbers at hit location
- Grabbing arena sword highlights enemy in red for 5s (wall-hack through geometry)

## Trinket / Chest System (Future — Do Not Implement Yet)

**Status**: design only. No chests/trinkets in code until after multiplayer playtest. Placeholder UI tab is acceptable; do not place chests in mazes yet.

### How it works

1. **Fixed 4 chests per maze.** Always exactly four — no more, no less.
2. **Player places chests in their own maze** during map editing. Strategic axis: hide them in hard-to-reach spots so the opponent must choose between hunting chests (stronger but slower) or sprinting to the exit early.
3. **Player does NOT assign trinkets to specific chests.** Chests are just placement markers.
4. **Customization tab** (new, separate from map editor): each player picks **4 trinkets from their personal trinket pool** as their match loadout — like a deck.
5. **At match start**, the opponent's 4 chosen trinkets are randomly distributed across your 4 placed chests. So when you run your opponent's maze, the chests contain *their* loadout — you don't know which trinket is in which chest.
6. **Trinket pool** grows via rolling (currency-based gacha) — design TBD. Placeholder pool of fixed trinkets for now.
7. **Set bonuses**: trinkets belong to sets (e.g. ice, fire, speed). Each additional trinket from the same set boosts the bonus. Encourages building around a theme.
8. **Trinket effects**: stat bonuses (damage, move speed) or movement abilities (ice slide, air dash, double jump).

### Strategic loop

- I build a brutal maze with chests buried deep → opponent loses tempo hunting them.
- I stack an ice-set loadout in my chests → opponent who fully clears my maze comes into arena with my ice build.
- Skipping chests = faster exit but weaker arena fight.

### Placeholder scope (when we start scaffolding)

- New "Customization" tab in main menu / lobby flow
- Trinket pool data structure + a handful of stub trinkets
- 4-slot loadout selector UI
- Chest piece in map editor palette (placement only, no contents yet)

### Why this is in PLAN.md and not code yet

Multiplayer + health/combat + arena loop ship first. The chest/trinket layer is the **next major system after** the playtest build is stable. Capturing the design here so the vision survives.

## Playtest-Ready Feature Checklist

- [ ] **Multiplayer** — ENet host/client, lobby with IP connect, 2-player core
- [ ] **Health + damage** — HP bars, damage numbers, death
- [ ] **Iron sword spawns on player** — no pickup required for playtest
- [ ] **Exit BFS torch signal** — torches turn blue from exit outward after 20s
- [ ] **Gold system** — 100g exit, 50g kill bonus
- [ ] **Arena sword + wall-hack** — 30-second hunt phase
- [ ] **Synty character models** — replace placeholder capsule

## Build Order

1. NetworkManager + Lobby scene
2. PlayerHealth + SwordCombat hitbox (23/30 dmg)
3. Damage numbers + health bar above head
4. GoldSystem + exit Area3D trigger
5. TorchSignalSystem (BFS from exit piece)
6. Arena sword pickup + wall-hack highlight + kill bonus
7. Synty character model import + animation retarget

## New Scripts Required

| File | Purpose |
|------|---------|
| `scripts/NetworkManager.cs` | ENet autoload — host/join, player registry |
| `scripts/PlayerHealth.cs` | 100 HP, TakeDamage RPC, Die RPC |
| `scripts/PlayerSync.cs` | MultiplayerSynchronizer for position/state |
| `scripts/DamageNumber.cs` | Floating red damage text, pooled |
| `scripts/TorchSignalSystem.cs` | BFS exit signal, torch color wave |
| `scripts/GoldSystem.cs` | Per-player gold tracking + HUD display |
| `scenes/Lobby.tscn` + `.cs` | Host/Join UI, player list, ready/start |

## Modified Files

| File | What Changes |
|------|-------------|
| `scripts/SwordCombat.cs` | Add Area3D hitbox, damage values |
| `scripts/PlayerController.cs` | PlayerHealth ref, death, Synty model hookup |
| `scripts/DungeonBuilder.cs` | Store torch OmniLight3D refs, expose BuiltPieces |
| `scenes/DungeonArena.cs` | Exit triggers, arena timer, kill detection |
| `project.godot` | NetworkManager autoload, main scene = Lobby |

## Deferred (Post-Playtest)

- **Trinket / Chest system** — see "Trinket / Chest System" section above. Customization tab + 4-trinket loadout + 4 chests placed in maze + opponent's trinkets randomly fill your chests + set bonuses.
- Trinket pool / rolling (gacha-style) economy
- Trinket effects + item PNG HUD bar
- 3-4 player expansion (NetworkManager already supports 4 peers)
- Maze piece visual variations (5 variants per piece type, bookshelf/cubby props)
- New wall/floor textures
- Trinket unlock + prestige shop UI

## Multiplayer Notes

- **Transport**: Godot 4 ENet, port 7777 UDP
- **For internet playtest**: host port-forwards 7777, or use ZeroTier/Hamachi (no server needed)
- **Authority model**: host = server authority; clients send inputs, server validates hits
- **Syncing**: `MultiplayerSynchronizer` for continuous state (position, HP); `@rpc` for events (damage, pickup, die)
