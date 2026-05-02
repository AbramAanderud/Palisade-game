# ARCHITECTURE.md

# Palisade Architecture

## Overview

Palisade is a Godot 4 C# first-person dungeon/maze game. It uses a 2D piece-based map editor to define dungeon layouts, then generates a playable 3D dungeon from those layouts.

The game is built around modular maze pieces that must connect naturally through openings. Some pieces are flat rooms/corridors, while others are stairs that connect multiple levels.

## Tech Stack

- Engine: Godot 4.x
- Language: C#
- Project file: `project.godot`
- Runtime scripts: `scripts/*.cs`
- Scenes: `scenes/*.tscn`

## Core Flow

1. The user places pieces in the 2D map editor.
2. Each piece stores type/value, position, and rotation.
3. Piece openings are checked so neighboring pieces connect legally.
4. Map data is saved/loaded through serializer logic.
5. On game start or generation, the dungeon builder reads the map layout.
6. The dungeon builder creates 3D floors, walls, ramps, collisions, and center arena connections.
7. The player spawns into the generated dungeon.

## System Responsibilities

### Map Editor

Main files:

- `scripts/MapEditorMain.cs`
- `scripts/TileData.cs`
- `scripts/MapSerializer.cs`

Responsibilities:

- piece selection
- piece placement
- piece rotation
- layout editing
- layout save/load
- preparing data for generation

### Piece Database

Main file:

- `scripts/PieceDB.cs`

Responsibilities:

- defining piece types
- returning openings
- handling rotation
- stair up/down direction
- determining connection metadata

Important risk:
If piece openings or stair directions are wrong here, validation and 3D generation will both behave incorrectly.

### Map Validation

Main file:

- `scripts/MapValidator.cs`

Responsibilities:

- checking whether adjacent pieces connect correctly
- rejecting impossible layouts
- protecting generator from invalid map data

### Dungeon Builder

Main file:

- `scripts/DungeonBuilder.cs`

Responsibilities:

- converting tile layout into 3D geometry
- creating floors, walls, ramps, and collisions
- handling stair geometry
- filling blocked walls
- leaving valid openings clear
- stitching maps to the center arena / middle room

Important risk:
Most visible generation bugs probably live here, especially missing walls, wrong openings, bad collisions, and arena stitching.

### Player Controller

Main file:

- `scripts/Player.cs`

Responsibilities:

- first-person movement
- third-person toggle
- wall running
- bunny hopping
- momentum
- sliding
- sword combat
- block
- aerial spin attack
- debug controls

## Current Design Pillars

- First person by default
- Third person toggle on `3`
- Fast expressive movement
- Sword found in maze center before combat unlocks
- Stylized realistic retro dungeon vibe
- Modular, grid-aligned dungeon pieces
- Multi-level maze support through stair pieces

## Known Architecture Risks

- Stair pieces are more complex than flat pieces because they involve rotation, openings, height, and ramp geometry.
- Center arena stitching depends on consistent coordinate/origin math.
- Handmade maps and generated maps must use the same piece scale and connection rules.
- Collision must be generated alongside visuals, not as an afterthought.
