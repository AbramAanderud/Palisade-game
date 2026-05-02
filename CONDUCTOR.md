# CONDUCTOR.md

# Palisade Documentation Hub

This is the navigation file for the Palisade Godot C# dungeon project.

## Core Documents

- `CLAUDE.md` — rules Claude must follow while working in this repo
- `ARCHITECTURE.md` — system design and how the game pieces connect
- `BUILD.md` — how to build, run, test, and verify the project
- `JOURNAL.md` — chronological record of fixes, bugs, and decisions
- `STRUCTURE.md` — godogen architecture reference, if present
- `PLAN.md` — godogen task plan, if present
- `MEMORY.md` — accumulated godogen discoveries, if present

## Main Systems

### Map Editor

Files:

- `scenes/MapEditor.tscn`
- `scripts/MapEditorMain.cs`
- `scripts/TileData.cs`
- `scripts/MapSerializer.cs`

Responsible for:

- placing map pieces
- rotating pieces
- saving/loading handmade maps
- assigning tile/piece values

### Piece Definitions

Files:

- `scripts/PieceDB.cs`

Responsible for:

- piece openings
- stair metadata
- stair up/down direction
- rotation rules
- connection compatibility

### Dungeon Builder

Files:

- `scripts/DungeonBuilder.cs`

Responsible for:

- converting 2D layout data into 3D dungeon geometry
- floors
- walls
- stair ramps
- collision
- center arena stitching
- generated/handmade map connection

### Validation

Files:

- `scripts/MapValidator.cs`

Responsible for:

- legal piece connections
- validating openings
- checking whether map layout can generate correctly

### Player / Gameplay

Files:

- `scenes/Player.tscn`
- `scripts/Player.cs`

Responsible for:

- first-person movement
- third-person toggle
- wall run
- bhop
- slide
- sword combat
- blocking
- aerial 360 spin
- debug controls

## Debug Routing

### Missing wall behind stairs

Check in this order:

1. `scripts/DungeonBuilder.cs`
2. `scripts/PieceDB.cs`
3. `scripts/MapEditorMain.cs`

### Stairs only place vertically or rotate wrong

Check in this order:

1. `scripts/PieceDB.cs`
2. `scripts/MapEditorMain.cs`
3. `scripts/DungeonBuilder.cs`

### Generated maps and handmade maps do not stitch to middle room

Check in this order:

1. `scripts/DungeonBuilder.cs`
2. map coordinate/origin logic
3. center arena placement logic
4. `scripts/MapSerializer.cs`

### Player falls through floor or walls are intangible

Check in this order:

1. `scripts/DungeonBuilder.cs`
2. collision shape creation
3. generated node hierarchy
4. relevant `.tscn` scene wiring
