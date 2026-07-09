# Manor Palisade — Visual Overhaul Design

**Date:** 2026-07-08
**Status:** Approved by Abe (pending spec review)
**References:** two mood images — (1) ornate warm rotunda with stained-glass dome
skylight, two-tier colonnade, balcony ring, gold ornament; (2) gothic manor
corridor: candelabra sconces, red drapes, ornate rugs, paneled walls, coffered
ceiling, fog.

## Goals

1. Arena ("middle room") evokes reference 1 while keeping its existing
   identity: bookshelf ring, interactive grass, velvet valances, real night sky.
2. Maze hallways/stairs evoke reference 2 (interior-only — no fake windows).
3. **Piece variant system**: 3–4 visual variants per maze piece type, chosen
   deterministically from the maze seed so multiplayer clients build identical
   mazes.
4. **Generated 3D props** via godogen (Gemini ref → Tripo3D GLB) replace
   poor-looking procedural props. Budget: **$10** (set once via asset_gen
   set_budget 1000).
5. **Physics props**: bump- and sword-reactive RigidBody3D clutter.
6. **Real 3D depth**: geometry trim (not just textures) + parallax + normal
   mapping.

## Non-goals

- No fake windows in hallways (decided against).
- No walkable balcony (visual-only; no collision changes to the arena shell).
- No multiplayer sync of physics props (client-side flavor).
- No replacement of the Synty character-asset roadmap; props are environment
  only.
- Piece GEOMETRY and COLLISION are untouched by variants (fragile per
  CLAUDE.md); variants swap materials and add child nodes only.

## Decisions (from brainstorming)

| Question | Decision |
|---|---|
| Arena roof | Stained-glass + ironwork dome, translucent; real HDRI stars glow through; visual-only, no collision |
| Arena contents | Blend: keep shelves/grass/valances, add second-tier colonnade + balcony + gold arches |
| Hallway windows | None — interior-only manor |
| Hallway floors | Dark wood planks + bordered rug runner down the center |
| Asset budget | $10 |
| Physics scope | Bump + sword-impulse, no pickup/throw |
| Texture depth | Real trim geometry + heightmap parallax + derivative-based normal mapping |

## Architecture

### A. Arena (scripts/ArenaBuilder.cs — additive)

- **Dome (visual-only)**: restore hemisphere geometry as two meshes, no
  collision:
  - *Ironwork lattice*: radial ribs + concentric rings + central star
    medallion; dark metal shader with gold emissive trim.
  - *Glass panes*: hemisphere surface, BLENDED stained-glass shader — leaded
    cell pattern (procedural voronoi/grid), warm amber/rose tint per cell,
    alpha ~0.35–0.5 so the HDRI starfield reads through. Sky renders at
    infinity so sorting is safe.
- **Second tier (22m→28m band)**: balustrade GLB segment instanced around the
  ring; column GLB above each shelf post; procedural gold-trimmed arches
  between columns; red swag drapes (existing velvet shader-cloth, swag mesh).
- Lighting: warm amber shift; keep band lights; keep valances/shelves/grass.

### B. Hallways (scripts/DungeonBuilder.cs — material/prop layer only)

- **Floor shader v2**: distance-from-corridor-centerline → rug field (carpet
  texture) + dark border band + dark wood planks outside. Rug also gets a
  thin raised slab mesh (bevel edge) for real depth.
- **Walls**: wainscot becomes real geometry (see D); terracotta above stays.
  Occasional tapestry/drape cloth panels per variant.
- **Ceiling**: real coffer beams (crossing box grid) on flat vault sections.
- **Sconces**: candelabra/torch GLBs at intervals; emissive flames; real
  OmniLight on every 2nd–3rd sconce only.
- Subtle floor fog via environment tuning (VisualFlair scenes).

### C. Piece variants (new scripts/PieceVariants.cs)

- `VariantDef` = { floor material key, wall material key, prop placements:
  [prop id, local Transform3D, physics? ] }.
- 3–4 defs per piece type (corridor, corner, T, room, stairs…).
- Selection: `hash(mazeSeed, cellX, cellZ, pieceTypeId) % variantCount` —
  pure function of shared maze data → multiplayer-identical.
- DungeonBuilder consults PieceVariants when building each piece; applies
  materials + instantiates prop children. No geometry/collision edits.

### D. Depth pass (shared)

- **Trim geometry** (instanced meshes, bookshelf technique): wainscot panel
  frames + rails + baseboard + chair-rail cap; coffer beams; arch trim ribs;
  rug slab. All visual-only, no collision.
- **Parallax**: TexSurf-style shaders gain heightmap UV offset using the
  downloaded `_disp` maps (wooden panels, terracotta, carpet).
- **Normal mapping without tangents**: derivative-based (dFdx/dFdy) normal
  perturbation using downloaded `nor_gl` maps — works on SurfaceTool meshes
  that lack tangents (known blocker from earlier work, documented in JOURNAL).

### E. Props & assets (godogen pipeline, $10)

~15–17 GLBs @ 40–50¢: wall torch ×2, standing candelabra ×2, **book-row ×3**
(replaces ~2,600 box-books via per-bay instancing — more detail, fewer draw
calls), balustrade segment ×1, column ×1, potted plant ×2, clutter kit
(book stack, goblet, pot) ×3–4. Per prop: Gemini 3D-ref image (7¢, reviewed
BEFORE conversion) → Tripo3D GLB → import/scale/place. Store under
`assets/glb/props/` (gitignored, per project convention) with an ASSETS.md
manifest and procedural fallbacks where sensible.

### F. Physics props (new scripts/PhysicsProp.cs)

- RigidBody3D + collision + GLB mesh child; small mass; aggressive sleep;
  ≤6 per piece; spawned from variant layouts.
- SwordCombat hook: on swing hit, apply radial impulse to PhysicsProp bodies
  in the arc (additive; no change to damage logic).
- Client-side only; Jolt rigid bodies (SoftBody3D remains banned per JOURNAL).

## Error handling / fallbacks

- Missing GLB or texture on a machine → procedural fallback or skip prop;
  build never fails on absent gitignored assets (established pattern).
- GLB that converts badly → regenerate within budget; if budget exhausted,
  procedural placeholder and note in ASSETS.md.
- Variant system defaults to variant 0 (current look) if defs missing.

## Testing / verification

- godogen task discipline: per-task build → capture → visual-qa skill verdict
  (Gemini, unbiased) → commit. Save verdicts to visual-qa/N.md.
- Capture scenes: TestArena (arena/dome/tier), StairInspect (stairs),
  ArenaConnTest (corridor + connection), plus a new TestVariants scene that
  builds one corridor per variant side by side.
- Multiplayer determinism: unit-style check — build maze twice from same seed,
  assert identical variant indices per cell.
- Physics: TestGrass-style harness — spawn clutter, drive an impulse, capture.
- Final: playtest checkpoint (user).

## Rollout order (feeds writing-plans)

1. Depth pass on hallways (trim geometry + parallax + normals) — biggest vibe
   win, no assets needed.
2. Floor v2 (wood + rug runner) + coffer beams + drapes.
3. Asset generation batch (refs → review → GLBs) — parallelizable.
4. Sconces + props placement + arena second tier + dome.
5. Variant system wiring + physics props + sword hook.
6. QA sweep + playtest.
