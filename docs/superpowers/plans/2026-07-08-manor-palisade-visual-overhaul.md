# Manor Palisade Visual Overhaul Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the maze hallways a gothic-manor look (real 3D trim, rug runners, sconces, coffers) and the arena a stained-glass rotunda second tier, driven by a deterministic per-piece variant system, godogen-generated GLB props, and sword-reactive physics clutter.

**Architecture:** All new visual work is ADDITIVE — variants swap materials and add child nodes to the per-cell `StaticBody3D` bodies after `DungeonBuilder.BuildFloor` runs (same pattern as the existing `VisualFlair.Decorate` hook). Piece geometry and collision are never modified. New code lives in three focused files (`PieceVariants.cs`, `PropLibrary.cs`, `PhysicsProp.cs`); `DungeonBuilder.cs` gains one integration line plus shader upgrades; `ArenaBuilder.cs` gains the dome + tier.

**Tech Stack:** Godot 4.6 C# (mono binary at `/c/Godot_v4.6.2-stable_mono_win64/Godot_v4.6.2-stable_mono_win64.exe`), Jolt physics (RigidBody3D fine, **SoftBody3D banned** per JOURNAL), godogen asset pipeline (Gemini refs → Tripo3D GLBs), PolyHaven/BlenderKit CC0 textures already in `assets/img/mats/` (gitignored — every material needs a procedural fallback).

## Global Constraints

- Verify every task: `dotnet build Palisade.csproj` → capture → view the screenshot → commit. Capture recipe (from MEMORY): temporarily `sed` `run/main_scene` in project.godot to the test scene, run `--write-movie screenshots/<dir>/x.png --fixed-fps 1 --quit-after 6`, then **always restore** `run/main_scene="res://scenes/TitleScreen.tscn"`.
- NEVER modify piece/arena geometry or collision shapes. Additive nodes + material overrides only.
- NEVER call `SurfaceTool.GenerateNormals()` on a mesh that must stay welded/indexed (it de-indexes). Set normals manually where welding matters.
- Runtime-loaded albedo JPGs need `Image.SrgbToLinear()` + `GenerateMipmaps()`. HDR/EXR files are linear — never SrgbToLinear them. Displacement/normal maps are data — never SrgbToLinear them.
- All texture/GLB loads: fall back gracefully (procedural material or skip prop) when the gitignored file is missing.
- Multiplayer determinism: any random visual choice must derive from `Fnv(pieceX, pieceY, floor, pieceType, rotation, salt)` — never `new Random()` without a deterministic seed.
- godogen budget: call `set_budget 1000` exactly ONCE (Task 5, step 1). Review every reference image BEFORE converting to GLB.
- Godot API questions → `Skill(skill="godot-api")` with a targeted query.
- After each task: update PLAN-tracking checkboxes in this file, commit with a descriptive message ending in the Claude co-author line.

---

### Task 1: Shader depth pass — parallax + tangent-free normal mapping

**Files:**
- Modify: `scripts/DungeonBuilder.cs` (const `TexSurfShaderSrc`, method `MakeTexturedMat`, the four `Get*Mat()` getters)

**Interfaces:**
- Produces: `MakeTexturedMat(string upperFile, float upperScale, string? lowerFile, float lowerScale, float wainscotH, float rough, bool doubleSided, string? heightFile, string? normalFile)` — later tasks (PieceVariants) copy this pattern but do not call it.

- [ ] **Step 1: Replace `TexSurfShaderSrc` with the depth-aware version**

In `scripts/DungeonBuilder.cs`, replace the entire `TexSurfShaderSrc` const with:

```glsl
const string TexSurfShaderSrc = @"
shader_type spatial;
render_mode diffuse_burley, specular_schlick_ggx;
uniform sampler2D upper_tex : filter_linear_mipmap, repeat_enable;
uniform sampler2D lower_tex : filter_linear_mipmap, repeat_enable;
uniform sampler2D height_tex : filter_linear_mipmap, repeat_enable;
uniform sampler2D normal_tex : filter_linear_mipmap, repeat_enable;
uniform float upper_scale = 0.25;
uniform float lower_scale = 0.40;
uniform float wainscot_h  = 0.0;   // 0 = single texture everywhere
uniform float level_h     = 18.0;  // DungeonBuilder.FloorHeight
uniform float rough       = 0.9;
uniform float parallax_depth = 0.0;   // 0 = disabled
uniform float normal_strength = 0.0;  // 0 = disabled
varying vec3 wpos; varying vec3 wnorm;
void vertex() {
    wpos  = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    wnorm = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}
void fragment() {
    vec3 n = abs(wnorm);
    vec2 uv;
    if (n.y > n.x && n.y > n.z) uv = wpos.xz;
    else if (n.x > n.z)         uv = wpos.zy;
    else                        uv = wpos.xy;
    float ly = mod(wpos.y, level_h);
    bool wall = n.y <= max(n.x, n.z);
    bool lower_zone = wainscot_h > 0.0 && wall && ly < wainscot_h;
    float scale = lower_zone ? lower_scale : upper_scale;

    // ── parallax: offset UV along view direction by heightmap ──────────────
    vec2 puv = uv * scale;
    if (parallax_depth > 0.0) {
        // view dir in world space projected onto the dominant plane
        vec3 vw = normalize(wpos - CAMERA_POSITION_WORLD);
        vec2 vplane;
        if (n.y > n.x && n.y > n.z) vplane = vw.xz;
        else if (n.x > n.z)         vplane = vw.zy;
        else                        vplane = vw.xy;
        float h = texture(height_tex, puv).r;
        puv -= vplane * (h - 0.5) * parallax_depth * scale;
    }

    vec3 col;
    if (lower_zone) {
        col = texture(lower_tex, puv).rgb;
        if (wainscot_h - ly < 0.10) col *= 0.5;   // trim shadow line at the cap
    } else {
        col = texture(upper_tex, puv).rgb;
    }
    ALBEDO = col; ROUGHNESS = rough; METALLIC = 0.0;

    // ── tangent-free normal mapping (screen-space derivative TBN) ──────────
    if (normal_strength > 0.0) {
        vec3 nm = texture(normal_tex, puv).rgb * 2.0 - 1.0;
        vec3 dp1 = dFdx(wpos), dp2 = dFdy(wpos);
        vec2 duv1 = dFdx(puv), duv2 = dFdy(puv);
        vec3 N = normalize(wnorm);
        vec3 dp2perp = cross(dp2, N), dp1perp = cross(N, dp1);
        vec3 T = dp2perp * duv1.x + dp1perp * duv2.x;
        vec3 B = dp2perp * duv1.y + dp1perp * duv2.y;
        float invmax = inversesqrt(max(dot(T,T), dot(B,B)) + 1e-8);
        vec3 wn = normalize(mat3(T*invmax, B*invmax, N) * mix(vec3(0,0,1), nm, normal_strength));
        NORMAL = normalize((VIEW_MATRIX * vec4(wn, 0.0)).xyz);
    }
}";
```

- [ ] **Step 2: Extend `MakeTexturedMat` with height/normal params**

Replace the existing `MakeTexturedMat` with:

```csharp
    ShaderMaterial? MakeTexturedMat(string upperFile, float upperScale,
        string? lowerFile = null, float lowerScale = 0.4f, float wainscotH = 0f,
        float rough = 0.9f, bool doubleSided = false,
        string? heightFile = null, string? normalFile = null)
    {
        var upper = LoadTexture(MatsDir + upperFile);
        if (upper == null) return null;                    // caller falls back
        var code = doubleSided
            ? TexSurfShaderSrc.Replace(
                "render_mode diffuse_burley, specular_schlick_ggx;",
                "render_mode diffuse_burley, specular_schlick_ggx, cull_disabled;")
            : TexSurfShaderSrc;
        var m = new ShaderMaterial { Shader = new Shader { Code = code } };
        m.SetShaderParameter("upper_tex", upper);
        m.SetShaderParameter("upper_scale", upperScale);
        m.SetShaderParameter("rough", rough);
        m.SetShaderParameter("level_h", FloorHeight);
        if (lowerFile != null)
        {
            var lower = LoadTexture(MatsDir + lowerFile);
            if (lower != null)
            {
                m.SetShaderParameter("lower_tex", lower);
                m.SetShaderParameter("lower_scale", lowerScale);
                m.SetShaderParameter("wainscot_h", wainscotH);
            }
        }
        if (heightFile != null)
        {
            var h = LoadDataTexture(MatsDir + heightFile);
            if (h != null) { m.SetShaderParameter("height_tex", h); m.SetShaderParameter("parallax_depth", 0.05f); }
        }
        if (normalFile != null)
        {
            var nrm = LoadDataTexture(MatsDir + normalFile);
            if (nrm != null) { m.SetShaderParameter("normal_tex", nrm); m.SetShaderParameter("normal_strength", 0.8f); }
        }
        return m;
    }

    // Data maps (height/normal): NO SrgbToLinear — they are not color.
    static ImageTexture? LoadDataTexture(string resPath)
    {
        var abs = ProjectSettings.GlobalizePath(resPath);
        var img = new Image();
        if (img.Load(abs) != Error.Ok) return null;
        img.GenerateMipmaps();
        return ImageTexture.CreateFromImage(img);
    }
```

- [ ] **Step 3: Wire the data maps into the four getters**

Update the `MakeTexturedMat` calls (jpg maps only — the EXR maps are 10MB+, skip them):

```csharp
// GetStoneMat (hallway walls):
        _stoneMat = MakeTexturedMat(
            "terracotta/terracotta_floor_tiles_diff.png.jpg", 0.30f,
            lowerFile: "woodpanel/wooden_panels_diff.png.jpg", lowerScale: 0.45f,
            wainscotH: 2.4f, rough: 0.85f,
            heightFile: "terracotta/terracotta_floor_tiles_disp.jpg.jpg");
// GetFloorMat (hallway floors):
        _floorMat = MakeTexturedMat("carpet/Carpet3_BaseColor.jpg.jpg", 0.28f, rough: 0.95f,
            heightFile: "carpet/Carpet3_Displacement.jpg.jpg",
            normalFile: "carpet/Carpet3_Normal.jpg.jpg");
// GetStairStoneMat (stair walls):
        _stairStoneMat = MakeTexturedMat("parquet/84.jpg.jpg", 0.35f, rough: 0.8f, doubleSided: true,
            normalFile: "parquet/normal.jpg.jpg");
// GetStairFloorMat (stair floors):
        _stairFloorMat = MakeTexturedMat("carpet/Carpet3_BaseColor.jpg.jpg", 0.28f, rough: 0.95f, doubleSided: true,
            heightFile: "carpet/Carpet3_Displacement.jpg.jpg",
            normalFile: "carpet/Carpet3_Normal.jpg.jpg");
```

- [ ] **Step 4: Build**

Run: `dotnet build Palisade.csproj 2>&1 | grep -E "error|Build succeeded"`
Expected: `Build succeeded.`

- [ ] **Step 5: Capture + verify**

```bash
sed -i 's|run/main_scene="res://scenes/TitleScreen.tscn"|run/main_scene="res://test/StairInspect.tscn"|' project.godot
"/c/Godot_v4.6.2-stable_mono_win64/Godot_v4.6.2-stable_mono_win64.exe" --path . --write-movie "screenshots/manor/depth.png" --fixed-fps 1 --quit-after 6 2>&1 | grep -Ei "^ERROR" | head -5
sed -i 's|run/main_scene="res://test/StairInspect.tscn"|run/main_scene="res://scenes/TitleScreen.tscn"|' project.godot
```

Read `screenshots/manor/depth00000003.png`. Expected: stair walls/floor show light response across the texture grain (normal mapping) and grout/pile depth shifting slightly with view angle vs previous flat captures. If surfaces went BLACK, the derivative TBN failed — set `normal_strength` to 0 for that material and file a JOURNAL note.

- [ ] **Step 6: Commit**

```bash
git add scripts/DungeonBuilder.cs
git commit -m "feat: parallax + tangent-free normal mapping in hall/stair materials"
```

---

### Task 2: PieceVariants — deterministic selection + per-piece material overrides

**Files:**
- Create: `scripts/PieceVariants.cs`
- Modify: `scripts/DungeonBuilder.cs` (`Build`: one call after the BuildFloor loop, before `AddTorches`)
- Create: `test/TestVariants.cs`, `test/TestVariants.tscn`

**Interfaces:**
- Produces: `static void PieceVariants.Apply(Node3D builder, IEnumerable<MazePiece> pieces)` — the single integration point.
- Produces: `static int PieceVariants.VariantIndex(MazePiece p, int count, int salt = 0)` — deterministic; Tasks 3/4/6/9 use it.
- Consumes: cell bodies named `Cell_{X}_{Y}_{floor}` (existing DungeonBuilder naming); `MazePiece` fields `X`, `Y`, `Floor`, `Type`, `Rotation`; `DungeonBuilder.CellSize/FloorHeight/OpeningW`; `PieceDB.GetOpenings(type, rotation)`.

- [ ] **Step 1: Create `scripts/PieceVariants.cs`**

```csharp
using Godot;
using System.Collections.Generic;

/// res://scripts/PieceVariants.cs
/// Deterministic per-piece visual variants. Applied AFTER DungeonBuilder
/// builds each cell: swaps mesh materials and adds decorative child nodes.
/// NEVER touches piece geometry or collision (fragile — see CLAUDE.md).
/// Determinism: both multiplayer clients hold identical MazeData, so a pure
/// hash of piece fields yields identical variants with no shared seed.
public partial class PieceVariants : Node
{
    public const int VariantCount = 4;

    // FNV-1a — stable across runs and machines (never use GetHashCode here).
    public static int Fnv(params int[] values)
    {
        unchecked
        {
            uint h = 2166136261;
            foreach (int v in values) { h ^= (uint)v; h *= 16777619; }
            return (int)(h & 0x7FFFFFFF);
        }
    }

    public static int VariantIndex(MazePiece p, int count, int salt = 0)
        => Fnv(p.X, p.Y, p.Floor, (int)p.Type, p.Rotation, salt) % count;

    /// Entry point — called once from DungeonBuilder.Build.
    public static void Apply(Node3D builder, IEnumerable<MazePiece> pieces)
    {
        foreach (var p in pieces)
        {
            var body = builder.GetNodeOrNull<StaticBody3D>($"Cell_{p.X}_{p.Y}_{p.Floor}");
            if (body == null) continue;
            int v = VariantIndex(p, VariantCount);
            ApplyVariant(builder, body, p, v);
        }
    }

    static void ApplyVariant(Node3D builder, StaticBody3D body, MazePiece p, int variant)
    {
        // Task 2 scope: deterministic wall-tint override proves the pipeline.
        // Later tasks replace this with real material sets + decor + props.
        foreach (var child in body.GetChildren())
        {
            if (child is not MeshInstance3D mi) continue;
            // Placeholder differentiation: subtle per-variant wall tint.
            // (Replaced in Task 3/4 with real variant material sets.)
        }
    }
}
```

- [ ] **Step 2: Integrate into DungeonBuilder.Build**

In `scripts/DungeonBuilder.cs`, in `Build(MazeData data, Vector3 worldOffset, Dir exitOpenDir)`, insert between the `BuildFloor` loop and `AddTorches(data.Pieces);`:

```csharp
        PieceVariants.Apply(this, data.Pieces);
```

- [ ] **Step 3: Create the determinism test harness**

`test/TestVariants.cs`:

```csharp
using Godot;

/// Determinism check: variant indices must be identical across two
/// evaluations (stands in for two multiplayer clients).
public partial class TestVariants : Node3D
{
    public override void _Ready()
    {
        var data = MazeSerializer.GenerateRandom(seed: 12345);   // adjust to actual generator API — see step note
        bool pass = true;
        foreach (var p in data.Pieces)
        {
            int a = PieceVariants.VariantIndex(p, PieceVariants.VariantCount);
            int b = PieceVariants.VariantIndex(p, PieceVariants.VariantCount);
            if (a != b || a < 0 || a >= PieceVariants.VariantCount) { pass = false; break; }
        }
        GD.Print(pass ? "[TestVariants] PASS deterministic" : "[TestVariants] FAIL");
        GetTree().Quit();
    }
}
```

NOTE FOR IMPLEMENTER: check how existing tests obtain a `MazeData` (grep `MazeData` in `test/TestArena.cs` / `test/ArenaConnTest.cs` and reuse that construction; if no generator exists, build a `MazeData` with 3–4 hand-made `MazePiece` entries — the test only needs pieces, not a playable maze).

`test/TestVariants.tscn`:

```
[gd_scene load_steps=2 format=3 uid="uid://tvariants1"]

[ext_resource type="Script" path="res://test/TestVariants.cs" id="1"]

[node name="TestVariants" type="Node3D"]
script = ExtResource("1")
```

- [ ] **Step 4: Build + run the test**

```bash
dotnet build Palisade.csproj 2>&1 | grep -E "error|Build succeeded"
sed -i 's|run/main_scene="res://scenes/TitleScreen.tscn"|run/main_scene="res://test/TestVariants.tscn"|' project.godot
"/c/Godot_v4.6.2-stable_mono_win64/Godot_v4.6.2-stable_mono_win64.exe" --path . --headless --quit-after 3 2>&1 | grep TestVariants
sed -i 's|run/main_scene="res://test/TestVariants.tscn"|run/main_scene="res://scenes/TitleScreen.tscn"|' project.godot
```

Expected: `[TestVariants] PASS deterministic`

- [ ] **Step 5: Commit**

```bash
git add scripts/PieceVariants.cs scripts/DungeonBuilder.cs test/TestVariants.cs test/TestVariants.tscn
git commit -m "feat: deterministic piece-variant system with integration hook"
```

---

### Task 3: Hall floor v2 — dark wood base + raised rug runner slabs

**Files:**
- Modify: `scripts/DungeonBuilder.cs` (`GetFloorMat`: carpet → dark wood base)
- Modify: `scripts/PieceVariants.cs` (rug slab decor in `ApplyVariant`)

**Interfaces:**
- Consumes: `PieceDB.GetOpenings(p.Type, p.Rotation)` returns `Dir` flags; corridor axis = the two opposite open directions.
- Produces: `static void AddRugSlab(StaticBody3D body, MazePiece p, int variant)` (private helper inside PieceVariants).

- [ ] **Step 1: Switch hall floor base to dark wood**

In `GetFloorMat()` (and `GetStairFloorMat()` keeps carpet — stairs read better carpeted), change the upper texture to the dark wood already on disk from the Blender concept work:

```csharp
        _floorMat = MakeTexturedMat("../arena_redesign/dark_wood/diffuse.jpg", 0.35f, rough: 0.85f,
            normalFile: "../arena_redesign/dark_wood/nor_gl.jpg");
        // (MatsDir + "../arena_redesign/..." resolves to assets/img/arena_redesign/)
```

- [ ] **Step 2: Add rug slabs in PieceVariants**

Replace `ApplyVariant`'s body (placeholder from Task 2) with material/decor dispatch, and add:

```csharp
    const string RugShaderSrc = @"
shader_type spatial;
render_mode diffuse_burley, specular_schlick_ggx;
uniform sampler2D rug_tex : filter_linear_mipmap, repeat_enable;
uniform vec3 border_col : source_color = vec3(0.10, 0.04, 0.03);
uniform vec3 tint : source_color = vec3(1.0, 1.0, 1.0);
void fragment() {
    // UV laid out 0..1 across the slab; border band 8% in from each edge
    vec2 e = min(UV, 1.0 - UV);
    float border = 1.0 - smoothstep(0.06, 0.085, min(e.x, e.y));
    vec3 rug = texture(rug_tex, UV * vec2(2.0, 6.0)).rgb * tint;
    ALBEDO = mix(rug, border_col, border * 0.85);
    ROUGHNESS = 0.97;
}";

    static ShaderMaterial? _rugMat0;
    static ShaderMaterial? RugMat(int variant)
    {
        // Per-variant tint over the same carpet texture
        var tints = new[] { new Color(1,1,1), new Color(0.7f,0.85f,1.1f),
                            new Color(1.1f,0.9f,0.7f), new Color(0.85f,1.0f,0.85f) };
        var abs = ProjectSettings.GlobalizePath("res://assets/img/mats/carpet/Carpet3_BaseColor.jpg.jpg");
        var img = new Image();
        if (img.Load(abs) != Error.Ok) return null;
        img.SrgbToLinear(); img.GenerateMipmaps();
        var m = new ShaderMaterial { Shader = new Shader { Code = RugShaderSrc } };
        m.SetShaderParameter("rug_tex", ImageTexture.CreateFromImage(img));
        m.SetShaderParameter("tint", tints[variant % tints.Length]);
        return m;
    }

    static void AddRugSlab(StaticBody3D body, MazePiece p, int variant)
    {
        if (PieceDB.IsStair(p.Type)) return;
        var mat = RugMat(variant);
        if (mat == null) return;                       // texture missing → skip
        var op = PieceDB.GetOpenings(p.Type, p.Rotation);
        // Corridor axis: N/S pair or E/W pair. Rooms/junctions get a centered square rug.
        bool ns = (op & Dir.N) != 0 && (op & Dir.S) != 0;
        bool ew = (op & Dir.E) != 0 && (op & Dir.W) != 0;
        float cs = DungeonBuilder.CellSize;
        float len = cs * 0.96f, wid = DungeonBuilder.OpeningW * 0.55f;
        if (!ns && !ew) { len = wid = DungeonBuilder.OpeningW * 0.8f; }   // junction: square rug

        var mesh = new BoxMesh { Size = new Vector3(ns ? wid : len, 0.05f, ns ? len : wid) };
        mesh.Material = mat;
        // Cell bodies sit at the builder origin with geometry in world-relative
        // coords, so a child's local position == piece coordinates.
        var mi = new MeshInstance3D
        {
            Name = "RugSlab",
            Mesh = mesh,
            Position = new Vector3(p.X * cs + cs * 0.5f,
                                   p.Floor * DungeonBuilder.FloorHeight + 0.03f,
                                   p.Y * cs + cs * 0.5f),
        };
        body.AddChild(mi);
    }
```

Call from `ApplyVariant`:

```csharp
    static void ApplyVariant(Node3D builder, StaticBody3D body, MazePiece p, int variant)
    {
        AddRugSlab(body, p, variant);
    }
```

IMPLEMENTER NOTE: verify cell-body coordinate space first — read `DungeonBuilder.BuildFloor` (~line 180): geometry vertices are in world coords and the body has no offset, so child-local == world-local as written. Confirm with the capture; if rugs land at the wrong spot, print `body.Position` and adjust.

- [ ] **Step 3: Build + capture (ArenaConnTest shows corridors) + verify rug placement + commit**

```bash
dotnet build Palisade.csproj 2>&1 | grep -E "error|Build succeeded"
sed -i 's|run/main_scene="res://scenes/TitleScreen.tscn"|run/main_scene="res://test/ArenaConnTest.tscn"|' project.godot
"/c/Godot_v4.6.2-stable_mono_win64/Godot_v4.6.2-stable_mono_win64.exe" --path . --write-movie "screenshots/manor/rugs.png" --fixed-fps 1 --quit-after 6 2>&1 | grep -Ei "^ERROR" | head -5
sed -i 's|run/main_scene="res://test/ArenaConnTest.tscn"|run/main_scene="res://scenes/TitleScreen.tscn"|' project.godot
git add scripts/DungeonBuilder.cs scripts/PieceVariants.cs
git commit -m "feat: dark wood hall floors with bordered rug-runner slabs per variant"
```

Read the capture: rug strip centered in the corridor, border visible, wood at edges.

---

### Task 4: Real trim geometry — wainscot frames, baseboards, coffer beams

**Files:**
- Modify: `scripts/PieceVariants.cs` (add `AddWallTrim` + `AddCofferBeams`, call from `ApplyVariant`)

**Interfaces:**
- Consumes: `PieceDB.GetOpenings`, `DungeonBuilder.CellSize/OpeningW/FloorHeight`, corridor walls run along closed sides at the corridor width (walls sit at `±OpeningW/2` from cell centerline — VERIFY by reading `AddPiece` wall emission ~line 440-540 before implementing).
- Produces: instanced `ArrayMesh` trim (one beveled-box mesh shared across all trim instances, bookshelf technique from `ArenaBuilder.OrientedBox`).

- [ ] **Step 1: Add a shared beveled-box helper + trim wood material to PieceVariants**

```csharp
    static ArrayMesh? _trimMesh;   // 1×1×1 beveled box, scaled per instance
    static ArrayMesh TrimMesh()
    {
        if (_trimMesh != null) return _trimMesh;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        // simple box (visual trim only; bevel via normal softness not needed at this size)
        Vector3[] c = {
            new(-0.5f,-0.5f,-0.5f), new(0.5f,-0.5f,-0.5f), new(0.5f,0.5f,-0.5f), new(-0.5f,0.5f,-0.5f),
            new(-0.5f,-0.5f, 0.5f), new(0.5f,-0.5f, 0.5f), new(0.5f,0.5f, 0.5f), new(-0.5f,0.5f, 0.5f) };
        int[][] faces = { new[]{0,1,2,3}, new[]{5,4,7,6}, new[]{4,0,3,7}, new[]{1,5,6,2}, new[]{3,2,6,7}, new[]{4,5,1,0} };
        foreach (var f in faces)
        {
            st.AddVertex(c[f[0]]); st.AddVertex(c[f[1]]); st.AddVertex(c[f[2]]);
            st.AddVertex(c[f[0]]); st.AddVertex(c[f[2]]); st.AddVertex(c[f[3]]);
        }
        st.GenerateNormals();      // fine here: trim boxes don't need welding
        _trimMesh = st.Commit();
        return _trimMesh;
    }

    const string TrimShaderSrc = @"
shader_type spatial;
render_mode diffuse_burley, specular_schlick_ggx;
varying vec3 wpos;
void vertex(){ wpos=(MODEL_MATRIX*vec4(VERTEX,1.0)).xyz; }
float hash13(vec3 p){ p=fract(p*0.1031); p+=dot(p,p.zyx+31.32); return fract((p.x+p.y)*p.z); }
float vnoise(vec3 p){
    vec3 i=floor(p), f=fract(p); f=f*f*(3.0-2.0*f);
    float a=hash13(i),b=hash13(i+vec3(1,0,0)),c=hash13(i+vec3(0,1,0)),d=hash13(i+vec3(1,1,0));
    float e=hash13(i+vec3(0,0,1)),g=hash13(i+vec3(1,0,1)),h=hash13(i+vec3(0,1,1)),k=hash13(i+vec3(1,1,1));
    return mix(mix(mix(a,b,f.x),mix(c,d,f.x),f.y),mix(mix(e,g,f.x),mix(h,k,f.x),f.y),f.z);
}
void fragment(){
    float grain = vnoise(vec3(wpos.x*5.0, wpos.y*0.8, wpos.z*5.0));
    ALBEDO = mix(vec3(0.12,0.07,0.045), vec3(0.26,0.16,0.09), grain);
    ROUGHNESS = 0.8;
}";
    static ShaderMaterial? _trimMat;
    static ShaderMaterial TrimMat() =>
        _trimMat ??= new ShaderMaterial { Shader = new Shader { Code = TrimShaderSrc } };

    static MeshInstance3D Trim(Vector3 center, Vector3 size)
        => new()
        {
            Mesh = TrimMesh(),
            MaterialOverride = TrimMat(),
            Position = center,
            Scale = size,
            Name = "Trim",
        };
```

- [ ] **Step 2: Wainscot frames + baseboard along closed walls**

BEFORE writing: read `scripts/DungeonBuilder.cs` `AddPiece` (~lines 430-545) and note the exact X/Z of corridor walls (they run at the corridor edges, `OpeningW/2` from center on closed sides). Then:

```csharp
    static void AddWallTrim(StaticBody3D body, MazePiece p, int variant)
    {
        if (PieceDB.IsStair(p.Type)) return;
        var op = PieceDB.GetOpenings(p.Type, p.Rotation);
        float cs = DungeonBuilder.CellSize, half = DungeonBuilder.OpeningW * 0.5f;
        float cx = p.X * cs + cs * 0.5f, cz = p.Y * cs + cs * 0.5f;
        float y0 = p.Floor * DungeonBuilder.FloorHeight;
        // trim heights: baseboard 0..0.25, chair rail cap at 2.4 (matches wainscot_h)
        foreach (var (dir, open) in new[] {
            (Dir.N, (op & Dir.N) != 0), (Dir.S, (op & Dir.S) != 0),
            (Dir.E, (op & Dir.E) != 0), (Dir.W, (op & Dir.W) != 0) })
        {
            if (open) continue;   // no wall on open sides
            bool alongX = dir is Dir.N or Dir.S;
            float wallOff = half - 0.06f;   // proud of the wall by 6cm
            Vector3 n = dir switch {
                Dir.N => new Vector3(0, 0, -1), Dir.S => new Vector3(0, 0, 1),
                Dir.E => new Vector3(1, 0, 0),  _     => new Vector3(-1, 0, 0) };
            Vector3 wallC = new Vector3(cx, 0, cz) + n * wallOff;
            float len = DungeonBuilder.OpeningW * 0.98f;
            Vector3 alongV = alongX ? new Vector3(1, 0, 0) : new Vector3(0, 0, 1);
            // baseboard
            body.AddChild(Trim(wallC + new Vector3(0, y0 + 0.125f, 0),
                Abs(alongV * len + n * 0.10f + Vector3.Up * 0.25f)));
            // chair-rail cap
            body.AddChild(Trim(wallC + new Vector3(0, y0 + 2.4f, 0),
                Abs(alongV * len + n * 0.12f + Vector3.Up * 0.10f)));
            // vertical stiles every ~1.2m (variant 3 = plain, skips stiles)
            if (variant != 3)
            {
                int stiles = 4;
                for (int i = 0; i <= stiles; i++)
                {
                    float t = -len / 2 + len * i / stiles;
                    body.AddChild(Trim(wallC + alongV * t + new Vector3(0, y0 + 1.3f, 0),
                        Abs(alongV * 0.09f + n * 0.07f + Vector3.Up * 2.1f)));
                }
            }
        }
    }
    static Vector3 Abs(Vector3 v) => new(Mathf.Abs(v.X), Mathf.Abs(v.Y), Mathf.Abs(v.Z));
```

- [ ] **Step 3: Coffer beams on flat ceiling strips**

Corridor ceilings are arched vaults; the flat sections flank them. Add a beam grid only on corridor pieces:

```csharp
    static void AddCofferBeams(StaticBody3D body, MazePiece p, int variant)
    {
        if (PieceDB.IsStair(p.Type) || variant == 2) return;   // variant 2 = plain ceiling
        var op = PieceDB.GetOpenings(p.Type, p.Rotation);
        bool ns = (op & Dir.N) != 0 && (op & Dir.S) != 0;
        bool ew = (op & Dir.E) != 0 && (op & Dir.W) != 0;
        if (!ns && !ew) return;   // corridors only
        float cs = DungeonBuilder.CellSize;
        float cx = p.X * cs + cs * 0.5f, cz = p.Y * cs + cs * 0.5f;
        float yCeil = p.Floor * DungeonBuilder.FloorHeight + DungeonBuilder.CellHeight - 0.15f;
        Vector3 along = ns ? new Vector3(0, 0, 1) : new Vector3(1, 0, 0);
        Vector3 across = ns ? new Vector3(1, 0, 0) : new Vector3(0, 0, 1);
        // cross beams every 2.5m spanning the corridor width
        for (int i = -1; i <= 1; i++)
            body.AddChild(Trim(new Vector3(cx, yCeil, cz) + along * (i * 2.5f),
                Abs(across * DungeonBuilder.OpeningW + along * 0.22f + Vector3.Up * 0.30f)));
    }
```

Call both from `ApplyVariant` after `AddRugSlab`.

- [ ] **Step 4: Build + capture + verify + commit**

Same ArenaConnTest capture recipe → `screenshots/manor/trim.png`. Verify: baseboards + chair rails on closed walls, stiles between, beams across the corridor ceiling, nothing floating in doorways. Commit: `feat: real wainscot/baseboard/coffer trim geometry per variant`.

---

### Task 5: godogen asset batch (budget $10, set ONCE)

**Files:**
- Create: `assets/img/props/*.png` (refs), `assets/glb/props/*.glb`
- Modify: `ASSETS.md` (manifest table)

**Interfaces:**
- Produces GLB files consumed by Task 6's PropLibrary: `assets/glb/props/{torch_a,torch_b,candelabra_a,candelabra_b,bookrow_a,bookrow_b,bookrow_c,balustrade_a,column_a,plant_a,plant_b,clutter_bookstack,clutter_goblet,clutter_pot}.glb`

- [ ] **Step 1: Set budget (ONCE — do not repeat on retries)**

```bash
python3 .claude/skills/godogen/tools/asset_gen.py set_budget 1000
```

- [ ] **Step 2: Generate reference images (Gemini 1K, parallel Bash calls)**

Template per prop (adjust subject line per prop below):

```bash
python3 .claude/skills/godogen/tools/asset_gen.py image --model gemini --size 1K \
  --prompt "3D model reference of {SUBJECT}. Gothic manor style, dark wrought iron and aged brass, stylized realistic game asset, modest polygon-friendly forms. 3/4 front elevated camera angle, solid white background, soft diffused studio lighting, matte material finish, single centered subject, no shadows on background." \
  -o assets/img/props/{NAME}.png
```

Subjects:
- `torch_a`: "a medieval wall torch sconce, wrought iron bracket with burning wick cup"
- `torch_b`: "an ornate gothic wall torch, dark iron with brass filigree backplate"
- `candelabra_a`: "a standing five-arm candelabra, wrought iron, lit candles with wax drips"
- `candelabra_b`: "a tall gothic floor candelabra, three arms, dark bronze"
- `bookrow_a`: "a row of old leather-bound books tightly packed on a shelf, varied heights, deep jewel-tone spines with gold lettering" *(add: viewed straight-on from the front, books fill the frame edge to edge)*
- `bookrow_b`: same subject, "warm red and brown spines, one book leaning"
- `bookrow_c`: same subject, "cool blue and green spines, slightly uneven row"
- `balustrade_a`: "a stone balustrade segment with three turned balusters and a handrail"
- `column_a`: "a slender gothic column with an ornate gold-leaf capital"
- `plant_a`: "a potted broadleaf plant in an ornate ceramic urn"
- `plant_b`: "a potted fern in a bronze planter"
- `clutter_bookstack`: "a small stack of three worn leather books"
- `clutter_goblet`: "a tarnished brass goblet"
- `clutter_pot`: "a small terracotta pot"

- [ ] **Step 3: REVIEW every reference (Read each PNG)**

Checklist per image: centered? complete object? clean white bg? matte? If bad → regenerate (2–7¢) before converting. A bad ref wastes 40–50¢.

- [ ] **Step 4: Convert approved refs to GLBs (parallel)**

```bash
python3 .claude/skills/godogen/tools/asset_gen.py glb --quality high \
  --image assets/img/props/{NAME}.png -o assets/glb/props/{NAME}.glb
```

14 GLBs × ~40¢ ≈ $5.60 + refs ≈ $1 → ~$3 headroom for retries.

- [ ] **Step 5: Manifest + commit**

Append a table to `ASSETS.md` (name, path, prompt subject, cost). Note: `assets/` is gitignored — commit only ASSETS.md.

```bash
git add ASSETS.md
git commit -m "assets: generate manor prop GLB batch via godogen (14 props)"
```

---

### Task 6: PropLibrary + sconces and props in variants

**Files:**
- Create: `scripts/PropLibrary.cs`
- Modify: `scripts/PieceVariants.cs` (sconce/prop placements per variant)

**Interfaces:**
- Produces: `static Node3D? PropLibrary.Instance(string propName, bool withLight = false)` — returns a ready node or null if GLB missing (caller skips).
- Consumes: GLBs from Task 5 at `assets/glb/props/{name}.glb`.

- [ ] **Step 1: Create `scripts/PropLibrary.cs`**

```csharp
using Godot;
using System.Collections.Generic;

/// res://scripts/PropLibrary.cs
/// Loads generated GLB props at runtime with caching. Returns null when a
/// prop file is missing (gitignored assets) — callers must skip gracefully.
public static class PropLibrary
{
    static readonly Dictionary<string, PackedScene?> _cache = new();

    public static Node3D? Instance(string propName, bool withLight = false)
    {
        if (!_cache.TryGetValue(propName, out var scene))
        {
            var path = $"res://assets/glb/props/{propName}.glb";
            scene = ResourceLoader.Exists(path) ? GD.Load<PackedScene>(path) : null;
            if (scene == null)
            {
                // runtime load fallback for un-imported files (headless captures)
                var abs = ProjectSettings.GlobalizePath(path);
                if (System.IO.File.Exists(abs))
                {
                    var gltf = new GltfDocument();
                    var state = new GltfState();
                    if (gltf.AppendFromFile(abs, state) == Error.Ok)
                    {
                        var node = gltf.GenerateScene(state);
                        var ps = new PackedScene();
                        ps.Pack(node);
                        scene = ps;
                    }
                }
            }
            _cache[propName] = scene;
        }
        if (scene == null) return null;
        var inst = scene.Instantiate<Node3D>();
        if (withLight)
            inst.AddChild(new OmniLight3D
            {
                Name = "FlameLight",
                Position = new Vector3(0, 0.9f, 0),
                LightColor = new Color(1.0f, 0.62f, 0.24f),
                LightEnergy = 2.4f,
                OmniRange = 12f,
                OmniAttenuation = 0.7f,
                ShadowEnabled = false,
            });
        return inst;
    }
}
```

IMPLEMENTER NOTE: generated GLBs arrive at arbitrary scale. After Task 5, load one in a scratch scene, print its AABB (`GetAabb()` on the MeshInstance), and add a per-prop scale table to PropLibrary (e.g. `{"torch_a", 0.8f}`) so props stand ~1.5–2m as appropriate. Budget 30 minutes of eyeballing; verify in the Task 6 capture.

- [ ] **Step 2: Sconce + prop placements per variant in PieceVariants**

```csharp
    static void AddSconces(StaticBody3D body, MazePiece p, int variant)
    {
        if (PieceDB.IsStair(p.Type)) return;
        var op = PieceDB.GetOpenings(p.Type, p.Rotation);
        float cs = DungeonBuilder.CellSize, half = DungeonBuilder.OpeningW * 0.5f;
        float cx = p.X * cs + cs * 0.5f, cz = p.Y * cs + cs * 0.5f;
        float y0 = p.Floor * DungeonBuilder.FloorHeight;
        string prop = (variant % 2 == 0) ? "torch_a" : "torch_b";
        // one sconce per closed wall; REAL light only when Fnv says so (1 in 3)
        int lightRoll = 0;
        foreach (var (dir, open) in new[] {
            (Dir.N, (op & Dir.N) != 0), (Dir.S, (op & Dir.S) != 0),
            (Dir.E, (op & Dir.E) != 0), (Dir.W, (op & Dir.W) != 0) })
        {
            if (open) continue;
            bool lit = PieceVariants.Fnv(p.X, p.Y, p.Floor, (int)dir, 7) % 3 == 0;
            var node = PropLibrary.Instance(prop, withLight: lit);
            if (node == null) return;                    // GLBs absent → skip all
            Vector3 n = dir switch {
                Dir.N => new Vector3(0, 0, -1), Dir.S => new Vector3(0, 0, 1),
                Dir.E => new Vector3(1, 0, 0),  _     => new Vector3(-1, 0, 0) };
            node.Position = new Vector3(cx, y0 + 2.9f, cz) + n * (half - 0.15f);
            node.LookAt(node.Position - n, Vector3.Up);   // face into the corridor
            body.AddChild(node);
            lightRoll++;
        }
    }
```

Call from `ApplyVariant`. Variant-specific extras (same pattern, exact placements chosen by implementer with the capture loop): variant 0 adds `plant_a` at junction corners; variant 1 adds `candelabra_a` mid-corridor edge; variant 3 adds a velvet drape panel (reuse `ArenaBuilder` velvet shader source string — copy the const, codebase duplicates shaders deliberately).

- [ ] **Step 3: Build + capture + verify + commit**

ArenaConnTest capture → `screenshots/manor/sconces.png`. Verify sconces on walls (not floating), warm pools every ~3rd sconce. Commit: `feat: PropLibrary + sconce/prop placement per variant`.

---

### Task 7: Arena second tier — balustrade, columns, arches, swags

**Files:**
- Modify: `scripts/ArenaBuilder.cs` (new `AddSecondTier()` called from `Build` after `AddBookshelfRing`)

**Interfaces:**
- Consumes: `PropLibrary.Instance("balustrade_a"/"column_a")`; shelf-top constant `WallH - 6f` (bookshelf `topY` — keep in sync).

- [ ] **Step 1: Implement `AddSecondTier`**

```csharp
    void AddSecondTier()
    {
        float tierY = WallH - 6f;               // shelf-top / balcony floor line
        // Balcony ring: shallow ledge (visual only, no collision)
        var ledge = new MeshInstance3D
        {
            Name = "TierLedge",
            Mesh = new TorusMesh { InnerRadius = Radius - 1.6f, OuterRadius = Radius - 0.2f,
                                   Rings = 64, RingSegments = 8 },
            Position = new Vector3(0, tierY, 0),
            MaterialOverride = PanelWallMat() as Material ?? StoneMat(),
        };
        AddChild(ledge);

        for (int i = 0; i < Sides; i++)
        {
            float a = 2f * Mathf.Pi * i / Sides;
            var pos = new Vector3(Mathf.Sin(a) * (Radius - 1.0f), tierY + 0.2f,
                                  -Mathf.Cos(a) * (Radius - 1.0f));
            // balustrade segment between columns
            var bal = PropLibrary.Instance("balustrade_a");
            if (bal != null)
            {
                bal.Position = pos;
                bal.RotationDegrees = new Vector3(0, -Mathf.RadToDeg(a) + 90f, 0);
                AddChild(bal);
            }
            // column every other bay, rising to the wall top
            if (i % 2 == 0)
            {
                var col = PropLibrary.Instance("column_a");
                if (col != null)
                {
                    col.Position = new Vector3(Mathf.Sin(a) * (Radius - 0.8f), tierY,
                                               -Mathf.Cos(a) * (Radius - 0.8f));
                    AddChild(col);
                }
            }
        }
        // Red swag drapes between columns: reuse the velvet cloth shader with a
        // swag-shaped grid (same indexed-grid technique as AddCurtain, but the
        // hem follows a catenary: y = top - sag * (1 - 4*(u-0.5)^2)).
        AddSwags(tierY + 4.5f);
    }

    void AddSwags(float rodY)
    {
        const int SegsX = 16, SegsY = 6;
        float span = 2f * Mathf.Pi * (Radius - 0.9f) / Sides * 2f;   // two bays
        for (int i = 0; i < Sides; i += 2)
        {
            float a = 2f * Mathf.Pi * (i + 1) / Sides;
            var st = new SurfaceTool();
            st.Begin(Mesh.PrimitiveType.Triangles);
            for (int row = 0; row <= SegsY; row++)
            for (int c = 0; c <= SegsX; c++)
            {
                float u = (float)c / SegsX, v = (float)row / SegsY;
                float sag = 1.6f * (1f - 4f * (u - 0.5f) * (u - 0.5f));
                st.SetUV(new Vector2(u, v));
                st.SetNormal(new Vector3(0, 0, 1));
                st.AddVertex(new Vector3((u - 0.5f) * span, -v * (0.9f + sag), 0));
            }
            for (int row = 0; row < SegsY; row++)
            for (int c = 0; c < SegsX; c++)
            {
                int i0 = row * (SegsX + 1) + c, i1 = i0 + 1, i2 = i0 + SegsX + 1, i3 = i2 + 1;
                st.AddIndex(i0); st.AddIndex(i2); st.AddIndex(i1);
                st.AddIndex(i1); st.AddIndex(i2); st.AddIndex(i3);
            }
            var mesh = st.Commit();
            mesh.SurfaceSetMaterial(0, new ShaderMaterial { Shader = new Shader { Code = VelvetShaderSrc } });
            AddChild(new MeshInstance3D
            {
                Name = $"Swag{i}",
                Mesh = mesh,
                Position = new Vector3(Mathf.Sin(a) * (Radius - 0.9f), rodY, -Mathf.Cos(a) * (Radius - 0.9f)),
                Rotation = new Vector3(0, -a + Mathf.Pi * 0.5f, 0),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }
    }
```

NOTE: `VelvetShaderSrc` already exists in ArenaBuilder (curtains). Swag meshes don't need the push uniforms — the shader's defaults keep them inert.

- [ ] **Step 2: Swap box-books for book-row GLBs on the shelves**

In `ArenaBuilder.AddBookshelfRing`, replace the per-book inner `while (t < ...)` loop with one book-row instance per board (keep the board/post code unchanged). The three GLB variants rotate deterministically; fall back to the existing box-book loop when GLBs are missing:

```csharp
                // Book rows: one detailed GLB per board (3 variants), replacing
                // the ~26 individual box-books. Fallback: original box loop.
                string rowName = new[] { "bookrow_a", "bookrow_b", "bookrow_c" }
                    [PieceVariants.Fnv(bay, (int)(y * 100), 5) % 3];
                var row = PropLibrary.Instance(rowName);
                if (row != null)
                {
                    row.Position = (a + b) * 0.5f + inward * (depth * 0.45f)
                                   + new Vector3(0, y + boardTh * 0.5f, 0);
                    row.Rotation = new Vector3(0, -mid + Mathf.Pi * 0.5f, 0);
                    // scale the row to fill the board (measure GLB AABB once and
                    // set a uniform scale so its length ≈ boardLen)
                    AddChild(row);
                    continue;   // skip the box-book loop for this level
                }
                // ...existing box-book while-loop stays as the fallback...
```

IMPLEMENTER NOTE: book-row GLB length is unknown until Task 5 delivers; measure `GetAabb()` and scale so the row spans `boardLen`. Verify shelf fill in the capture — gaps at bay ends are acceptable, floating/overlapping rows are not.

- [ ] **Step 3: Build + TestArena capture + verify + commit**

Capture → `screenshots/manor/tier.png`. Verify: balustrade ring above shelves, columns rhythm, swags draping, book-rows filling boards (or box-book fallback if GLBs absent). Missing GLBs (pre-Task-5 machines) → ring simply absent, no errors. Commit: `feat: arena second tier — balustrade, columns, swags, GLB book rows`.

---

### Task 8: Stained-glass dome (visual-only, sky glows through)

**Files:**
- Modify: `scripts/ArenaBuilder.cs` (new `AddStainedGlassDome()` called from `Build`; new shader consts)

**Interfaces:**
- Consumes: hemisphere math previously deleted (restore ring-loop geometry, but as visual-only meshes — NO collision, NO Emit()).

- [ ] **Step 1: Glass + ironwork shaders**

```csharp
    const string StainedGlassShaderSrc = @"
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_disabled, diffuse_burley, specular_schlick_ggx;
uniform float alpha_base = 0.42;
varying vec3 lpos;
void vertex(){ lpos = VERTEX; }
float hash13(vec3 p){ p=fract(p*0.1031); p+=dot(p,p.zyx+31.32); return fract((p.x+p.y)*p.z); }
void fragment(){
    // leaded cells: spherical-ish grid from direction angles
    vec3 dir = normalize(lpos - vec3(0.0, 22.0, 0.0));
    float az = atan(dir.z, dir.x) * 6.0;
    float el = acos(clamp(dir.y, -1.0, 1.0)) * 7.0;
    vec2 cell = floor(vec2(az, el));
    vec2 f = fract(vec2(az, el));
    float lead = step(f.x, 0.06) + step(0.94, f.x) + step(f.y, 0.06) + step(0.94, f.y);
    // warm pane tints: amber / rose / pale gold per cell
    float r = hash13(vec3(cell, 3.0));
    vec3 pane = r < 0.4 ? vec3(0.95, 0.62, 0.25) : (r < 0.7 ? vec3(0.85, 0.35, 0.30) : vec3(0.98, 0.85, 0.55));
    ALBEDO = pane * 0.25;
    EMISSION = pane * 0.06;                 // faint inner glow
    ALPHA = clamp(lead, 0.0, 1.0) > 0.5 ? 0.95 : alpha_base;   // lead lines near-opaque
    ROUGHNESS = 0.25; METALLIC = clamp(lead, 0.0, 1.0) * 0.8;
}";
```

- [ ] **Step 2: Rebuild the hemisphere (visual-only) + iron ribs**

```csharp
    void AddStainedGlassDome()
    {
        const int DomeRings = 10;
        var glassST = MakeST();
        for (int ring = 0; ring < DomeRings; ring++)
        {
            float t0 = (float)ring / DomeRings, t1 = (float)(ring + 1) / DomeRings;
            float th0 = t0 * Mathf.Pi * 0.5f, th1 = t1 * Mathf.Pi * 0.5f;
            float r0 = Radius * Mathf.Cos(th0), r1 = Radius * Mathf.Cos(th1);
            float y0 = WallH + Radius * 0.35f * Mathf.Sin(th0);   // squashed dome (0.35 height ratio)
            float y1 = WallH + Radius * 0.35f * Mathf.Sin(th1);
            for (int i = 0; i < Sides; i++)
            {
                float a0 = 2f * Mathf.Pi * i / Sides - FaceHalf;
                float a1 = 2f * Mathf.Pi * (i + 1) / Sides - FaceHalf;
                Quad(glassST,
                    new(Mathf.Sin(a0)*r0, y0, -Mathf.Cos(a0)*r0),
                    new(Mathf.Sin(a1)*r0, y0, -Mathf.Cos(a1)*r0),
                    new(Mathf.Sin(a1)*r1, y1, -Mathf.Cos(a1)*r1),
                    new(Mathf.Sin(a0)*r1, y1, -Mathf.Cos(a0)*r1));
            }
        }
        var glass = Commit(glassST);
        if (glass != null)
            AddChild(new MeshInstance3D
            {
                Name = "DomeGlass", Mesh = glass,
                MaterialOverride = new ShaderMaterial { Shader = new Shader { Code = StainedGlassShaderSrc } },
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });

        // ironwork: radial ribs (thin boxes following the dome curve) + rings
        var ribST = MakeST();
        for (int i = 0; i < Sides; i += 2)
        {
            float a = 2f * Mathf.Pi * i / Sides - FaceHalf;
            for (int s = 0; s < 8; s++)
            {
                float u0 = s / 8f, u1 = (s + 1) / 8f;
                float th0 = u0 * Mathf.Pi * 0.5f, th1 = u1 * Mathf.Pi * 0.5f;
                Vector3 p0 = new(Mathf.Sin(a) * Radius * Mathf.Cos(th0), WallH + Radius * 0.35f * Mathf.Sin(th0) + 0.06f, -Mathf.Cos(a) * Radius * Mathf.Cos(th0));
                Vector3 p1 = new(Mathf.Sin(a) * Radius * Mathf.Cos(th1), WallH + Radius * 0.35f * Mathf.Sin(th1) + 0.06f, -Mathf.Cos(a) * Radius * Mathf.Cos(th1));
                Vector3 side = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 0.14f;
                Quad(ribST, p0 - side, p1 - side, p1 + side, p0 + side);
            }
        }
        var ribs = Commit(ribST);
        if (ribs != null)
        {
            var iron = new StandardMaterial3D { AlbedoColor = new Color(0.06f, 0.05f, 0.05f),
                Roughness = 0.5f, Metallic = 0.8f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
            AddChild(new MeshInstance3D { Name = "DomeRibs", Mesh = ribs, MaterialOverride = iron });
        }
    }
```

Call `AddStainedGlassDome();` from `Build` right after `SetupNightSky();`. NO collision anywhere.

- [ ] **Step 3: Build + TestArena capture + verify + commit**

Capture → `screenshots/manor/dome.png`. Verify frame 1 (look-up): warm leaded panes with starfield visible through them, dark rib silhouettes. If panes render fully opaque check `blend_mix` took (transparent queue); if invisible check winding (this hemisphere reuses the ORIGINAL inward winding — with cull_disabled either way renders). Commit: `feat: stained-glass dome with night sky glowing through`.

---

### Task 9: Physics props + sword impulse

**Files:**
- Create: `scripts/PhysicsProp.cs`
- Modify: `scripts/PieceVariants.cs` (clutter spawns)
- Modify: `scripts/SwordCombat.cs` (one call in the swing-hit path)

**Interfaces:**
- Produces: `PhysicsProp.Spawn(string propName, Vector3 worldPos)` and `static void PhysicsProp.ApplySwingImpulse(Node3D source, Vector3 origin, Vector3 dir)`.

- [ ] **Step 1: Create `scripts/PhysicsProp.cs`**

```csharp
using Godot;

/// res://scripts/PhysicsProp.cs
/// Small knockable clutter (books, goblets, pots). RigidBody3D under Jolt.
/// Client-side flavor only — NOT synced over the multiplayer relay.
public partial class PhysicsProp : RigidBody3D
{
    public const string Group = "PhysicsProp";

    public static PhysicsProp? Spawn(string propName, Vector3 worldPos)
    {
        var visual = PropLibrary.Instance(propName);
        if (visual == null) return null;
        var body = new PhysicsProp
        {
            Name = $"Prop_{propName}",
            Mass = 0.8f,
            CanSleep = true,
            Position = worldPos,
        };
        body.AddToGroup(Group);
        body.AddChild(visual);
        // approximate collision from the visual AABB
        var aabb = ComputeAabb(visual);
        body.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = aabb.Size.Clamp(new Vector3(0.05f, 0.05f, 0.05f), new Vector3(1f, 1f, 1f)) },
            Position = aabb.GetCenter(),
        });
        return body;
    }

    static Aabb ComputeAabb(Node3D root)
    {
        Aabb total = new(root.Position, Vector3.Zero);
        bool first = true;
        foreach (var mi in root.FindChildren("*", "MeshInstance3D", recursive: true, owned: false))
            if (mi is MeshInstance3D m)
            {
                var b = m.GetAabb();
                total = first ? b : total.Merge(b);
                first = false;
            }
        return first ? new Aabb(Vector3.Zero, Vector3.One * 0.3f) : total;
    }

    /// Sword-swing hook: shove any PhysicsProp within the swing arc.
    public static void ApplySwingImpulse(Node3D source, Vector3 origin, Vector3 dir)
    {
        foreach (var n in source.GetTree().GetNodesInGroup(Group))
        {
            if (n is not PhysicsProp prop || !prop.IsInsideTree()) continue;
            var to = prop.GlobalPosition - origin;
            if (to.Length() > 2.6f) continue;
            if (to.Normalized().Dot(dir.Normalized()) < 0.35f) continue;   // ~70° arc
            prop.ApplyCentralImpulse((to.Normalized() + Vector3.Up * 0.6f) * 3.5f);
        }
    }
}
```

- [ ] **Step 2: Clutter spawns in PieceVariants**

In `ApplyVariant`, after decor (deterministic placement, ≤3 per piece):

```csharp
        string[] clutter = { "clutter_bookstack", "clutter_goblet", "clutter_pot" };
        int count = Fnv(p.X, p.Y, p.Floor, 21) % 3;    // 0..2 items
        float cs2 = DungeonBuilder.CellSize;
        for (int i = 0; i < count; i++)
        {
            string prop = clutter[Fnv(p.X, p.Y, i, 22) % clutter.Length];
            float ox = (Fnv(p.X, p.Y, i, 23) % 100) / 100f * 3f - 1.5f;
            float oz = (Fnv(p.X, p.Y, i, 24) % 100) / 100f * 3f - 1.5f;
            var prop3d = PhysicsProp.Spawn(prop,
                new Vector3(p.X * cs2 + cs2 * 0.5f + ox,
                            p.Floor * DungeonBuilder.FloorHeight + 0.4f,
                            p.Y * cs2 + cs2 * 0.5f + oz));
            if (prop3d != null) body.AddChild(prop3d);
        }
```

- [ ] **Step 3: SwordCombat hook**

Recon first: `grep -n "Swing\|Attack\|hit\|Area3D\|Raycast" scripts/SwordCombat.cs | head -20` — find where a swing applies damage/detects hits. Insert ONE line at swing execution (where the swing direction and player position are in scope):

```csharp
        PhysicsProp.ApplySwingImpulse(this, GlobalPosition, swingDirection);
```

(Adapt variable names to the actual method: origin = player/blade position, dir = facing/swing direction. Do NOT modify damage logic.)

- [ ] **Step 4: Physics test harness + verify + commit**

`test/TestPhysicsProps.cs` + `.tscn` (TestGrass pattern): spawn a floor plane, 5 clutter props, apply `ApplySwingImpulse` after 2s, capture 8 frames → props visibly scattered between early and late frames. Build, capture → `screenshots/manor/physics.png`, verify, commit: `feat: physics clutter props with sword-swing impulse`.

---

### Task 10: QA sweep + docs + playtest handoff

**Files:**
- Modify: `JOURNAL.md`, `ARCHITECTURE.md` (if exists — else skip), this plan (check boxes)

- [ ] **Step 1: Full capture set**

TestArena (arena: tier + dome + sky), ArenaConnTest (corridor: rugs/trim/sconces), StairInspect (stairs), TestVariants (determinism PASS), TestPhysicsProps.

- [ ] **Step 2: Unbiased visual QA**

Run `Skill(skill="visual-qa")` on the arena and corridor captures with context "gothic manor / ornate rotunda reference match". Save verdicts to `visual-qa/manor-{n}.md`. Fix `fail` verdicts (max 3 cycles each, then escalate to Abe per godogen rules).

- [ ] **Step 3: Perf sanity**

Capture with `--fixed-fps 30 --quit-after 90` on ArenaConnTest; if frame time visibly degrades vs pre-overhaul captures, halve sconce real-light count (the 1-in-3 Fnv roll → 1-in-5) and re-verify.

- [ ] **Step 4: JOURNAL entry + commit + report to Abe with the money shots**

---

## Deferred (explicitly out of scope)

- **CellSize increase** (longer hallways / shallower stairs): evaluated, deferred — touches arena stitching (already broken), saved MazeData maps, stair ramp + FloorMaxAngle coupling, and movement feel. Future project: "cell rescale + stitching repair".
- Walkable balcony (needs collision decision).
- Physics prop multiplayer sync.
