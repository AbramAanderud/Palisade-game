Use `/godogen` to generate or update this game from a natural language description.

The working directory is the project root. NEVER `cd` — use relative paths for all commands.

When a channel is connected (Telegram, Slack, etc.), share progress via `reply`. Attach screenshots and videos using `files` — task completions, QA verdicts, reference image, final video are all worth sharing.

# Project Structure

Game projects follow this layout once `/godogen` runs:

```
project.godot          # Godot config: viewport, input maps, autoloads, [dotnet]
{ProjectName}.csproj   # .NET project file
reference.png          # Visual target — art direction reference image
STRUCTURE.md           # Architecture reference: scenes, scripts, signals
PLAN.md                # Game plan — risk tasks, main build, verification criteria
ASSETS.md              # Asset manifest with art direction and paths
MEMORY.md              # Accumulated discoveries from task execution
scenes/
  Build*.cs            # Headless scene builders (produce .tscn)
  *.tscn               # Compiled scenes
scripts/*.cs           # Runtime C# scripts
test/
  TestTask.cs          # Per-task visual test harness (overwritten each task)
  Presentation.cs      # Final cinematic video script
assets/                # gitignored — img/*.png, glb/*.glb
screenshots/           # gitignored — per-task frames
.vqa.log               # Visual QA debug log (gitignored)
```

## Limitations

- No audio support
- No animated GLBs — static models only

## Palisade gameplay pillars

- First person by default, third person toggle on 3
- Fast expressive movement: wall run, bhop, momentum, slide
- Sword found in maze center before combat unlocks
- Combat: single swing, 3-hit combo, block, aerial 360 spin
- Stylized 3D with realistic retro dungeon vibe
- Placeholder/low-poly character models are acceptable during development

# Palisade Project Context

## Current State

This is an existing Godot C# project, not a fresh generated project. Be careful not to overwrite handmade systems unless explicitly asked.

The project already contains:

- a map editor
- tile/piece placement
- stair pieces
- dungeon generation
- a center arena / middle room
- first-person player movement
- experimental third-person/debug features

## Most Important Rule

Make minimal targeted fixes. Do not rewrite the whole generator, player controller, or map editor unless explicitly asked.

When fixing bugs, first identify the most likely file/function responsible, then make the smallest change.

## Known Fragile Areas

- Stair rotation and stair direction logic
- Wall filling behind stairs
- Openings between maze pieces
- Center arena stitching between generated maps and handmade maps
- Collision generation for floors, walls, ramps, and arena pieces
- Scene/node wiring in Godot `.tscn` files

## Current Known Bugs

- Wall behind stairs is not filled correctly for both stairs up and stairs down.
- Generated maps and handmade maps are no longer fixed/stitching together through the middle room.
- Stairs previously became vertical-only when rotation logic broke.
- Arena floor/wall collision has broken before.

## Likely Important Files

- `scripts/PieceDB.cs`
  - piece definitions
  - openings
  - stair direction
  - stair rotation
- `scripts/MapEditorMain.cs`
  - editor placement
  - rotation input
  - piece selection
  - saved map layout
- `scripts/DungeonBuilder.cs`
  - 3D geometry generation
  - walls
  - floors
  - stair ramps
  - center arena stitching
  - collision generation
- `scripts/MapValidator.cs`
  - piece compatibility and valid connections
- `scripts/MapSerializer.cs`
  - saving/loading layouts
- `scripts/Player.cs`
  - movement, debug controls, combat

## Workflow Rules

- Use `/godogen` when generating or updating the game from a natural language description.
- Working directory is the project root.
- NEVER `cd`; use relative paths for all commands.
- Prefer relative paths in commands and file references.
- After completing a meaningful fix, update `JOURNAL.md`.
- If a change affects architecture, update `ARCHITECTURE.md`.
- If a new command or workflow is discovered, update `BUILD.md`.
- If a bug or discovery is important for future work, update `MEMORY.md` if godogen uses it, otherwise update `JOURNAL.md`.

## Style / Design Direction

- Stylized 3D realistic retro dungeon vibe.
- Low-poly or placeholder models are acceptable during development.
- Prioritize gameplay correctness over final art.
- Keep pieces modular and grid-aligned so the dungeon can stitch naturally.
