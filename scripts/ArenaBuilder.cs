using Godot;
using System;

/// res://scripts/ArenaBuilder.cs
/// Builds a circular arena room (regular polygon, N=28 sides) that connects two
/// dungeon exits.  Two faces are left open as doorways matching corridor width.
///
/// Layout (arena mode):
///   Maze A  z = 0 … 100  (Exit faces South  → arena opening on north face of arena)
///   Arena   centre = (exitAX, 0, arenaZ), radius = 30 m
///   Maze B  z = 160 … 260 (Exit faces North → arena opening on south face of arena)
///
/// The 28-gon has face width ≈ 2π×30/28 ≈ 6.73 m ≈ OpeningW (6 m) — one face per opening.
public partial class ArenaBuilder : Node3D
{
    // Matches DungeonBuilder geometry constants
    const float CellHeight = DungeonBuilder.CellHeight;   // 8.4 m
    const float ArchRise   = DungeonBuilder.ArchRise;     // 4.0 m
    const float OpeningW   = DungeonBuilder.OpeningW;     // 6.0 m

    const int   Sides      = 28;
    const float Radius     = 30f;    // polygon circumradius (m)
    const float WallH      = CellHeight;
    const float SpringY    = WallH;
    const int   DomeSeg    = 10;     // dome ring subdivisions

    // ── Procedural stone shader (copy from DungeonBuilder) ────────────────────
    const string StoneShaderSrc = @"
shader_type spatial;
render_mode diffuse_burley, specular_schlick_ggx;
uniform vec3 base_color : source_color = vec3(0.42, 0.37, 0.30);
uniform vec3 mortar_color : source_color = vec3(0.22, 0.20, 0.18);
uniform float brick_scale = 0.28;
uniform float mortar_w = 0.07;
varying vec3 wpos;
varying vec3 wnorm;
void vertex() {
    wpos  = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    wnorm = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}
float brick_mask(vec2 uv) {
    float row = floor(uv.y);
    vec2 b = fract(vec2(uv.x + mod(row, 2.0) * 0.5, uv.y));
    return step(mortar_w, b.x) * step(b.x, 1.0 - mortar_w)
         * step(mortar_w, b.y) * step(b.y, 1.0 - mortar_w);
}
float hash21(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
void fragment() {
    vec3 n = abs(wnorm);
    vec2 uv;
    vec2 bid;
    if (n.y > n.x && n.y > n.z) {
        uv  = wpos.xz * brick_scale;
        bid = floor(vec2(uv.x + mod(floor(uv.y), 2.0) * 0.5, uv.y));
    } else if (n.x > n.z) {
        vec2 r = wpos.zy * brick_scale * vec2(0.6, 1.0);
        uv = r; bid = floor(vec2(r.x + mod(floor(r.y), 2.0) * 0.5, r.y));
    } else {
        vec2 r = wpos.xy * brick_scale * vec2(0.6, 1.0);
        uv = r; bid = floor(vec2(r.x + mod(floor(r.y), 2.0) * 0.5, r.y));
    }
    float is_brick = brick_mask(uv);
    float hv = (hash21(bid) - 0.5) * 0.18;
    vec3 bc = clamp(base_color + vec3(hv, hv * 0.85, hv * 0.65), vec3(0.0), vec3(1.0));
    ALBEDO    = mix(mortar_color, bc, is_brick);
    ROUGHNESS = 0.93;
    METALLIC  = 0.0;
}
";

    const string FloorShaderSrc = @"
shader_type spatial;
render_mode diffuse_burley, specular_schlick_ggx;
uniform vec3 stone_color : source_color = vec3(0.32, 0.29, 0.25);
uniform vec3 grout_color : source_color = vec3(0.18, 0.17, 0.15);
uniform float tile_scale = 0.20;
varying vec3 wpos;
void vertex() { wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; }
float hash21(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
void fragment() {
    vec2 uv   = wpos.xz * tile_scale;
    vec2 cell = floor(uv);
    vec2 f    = fract(uv);
    float gap = 0.09;
    float is_tile = step(gap, f.x) * step(f.x, 1.0-gap)
                  * step(gap, f.y) * step(f.y, 1.0-gap);
    float hv = (hash21(cell) - 0.5) * 0.12;
    vec3 sc  = clamp(stone_color + vec3(hv), vec3(0.0), vec3(1.0));
    ALBEDO    = mix(grout_color, sc, is_tile);
    ROUGHNESS = 0.97;
    METALLIC  = 0.0;
}
";

    ShaderMaterial? _stoneMat;
    ShaderMaterial? _floorMat;

    ShaderMaterial StoneMat()
    {
        if (_stoneMat != null && GodotObject.IsInstanceValid(_stoneMat)) return _stoneMat;
        var sh = new Shader { Code = StoneShaderSrc };
        return _stoneMat = new ShaderMaterial { Shader = sh };
    }
    ShaderMaterial FloorMat()
    {
        if (_floorMat != null && GodotObject.IsInstanceValid(_floorMat)) return _floorMat;
        var sh = new Shader { Code = FloorShaderSrc };
        return _floorMat = new ShaderMaterial { Shader = sh };
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  PUBLIC API
    // ══════════════════════════════════════════════════════════════════════════
    /// <summary>
    /// Build the arena centred at <paramref name="centre"/>.
    /// <paramref name="openNorth"/>: which face index (0–27) opens toward Maze A (north).
    /// <paramref name="openSouth"/>: which face index opens toward Maze B (south).
    /// In practice these are face 0 (north, angle=0) and face 14 (south, angle=π).
    /// </summary>
    public void Build(Vector3 centre, int openNorth = 0, int openSouth = 14)
    {
        Position = centre;

        var wallST  = MakeST();
        var floorST = MakeST();
        var ceilST  = MakeST();

        // Pre-compute polygon vertices (outer ring)
        var verts = new Vector3[Sides];
        for (int i = 0; i < Sides; i++)
        {
            float angle = 2f * Mathf.Pi * i / Sides;
            verts[i] = new Vector3(Mathf.Sin(angle) * Radius, 0f, -Mathf.Cos(angle) * Radius);
        }

        // ── Walls ──────────────────────────────────────────────────────────────
        for (int i = 0; i < Sides; i++)
        {
            if (i == openNorth || i == openSouth) continue;  // leave doorway open

            Vector3 a = verts[i];
            Vector3 b = verts[(i + 1) % Sides];

            // Outer wall face (inward-facing quad from floor to WallH)
            Quad(wallST,
                new(b.X, WallH, b.Z),
                new(a.X, WallH, a.Z),
                new(a.X, 0f,    a.Z),
                new(b.X, 0f,    b.Z));
        }

        // ── Floor (fan triangulation from centre) ──────────────────────────────
        for (int i = 0; i < Sides; i++)
        {
            Vector3 a = verts[i];
            Vector3 b = verts[(i + 1) % Sides];
            // Triangle fan facing up
            floorST.SetUV(UV(a));           floorST.AddVertex(a);
            floorST.SetUV(UV(b));           floorST.AddVertex(b);
            floorST.SetUV(new Vector2(0f, 0f)); floorST.AddVertex(Vector3.Zero);
        }

        // ── Dome ceiling — hemisphere from spring ring to apex ─────────────────
        for (int ring = 0; ring < DomeSeg; ring++)
        {
            float t0 = (float)ring       / DomeSeg;
            float t1 = (float)(ring + 1) / DomeSeg;
            float theta0 = t0 * Mathf.Pi * 0.5f;   // 0 → π/2
            float theta1 = t1 * Mathf.Pi * 0.5f;
            float r0 = Radius * Mathf.Cos(theta0);
            float r1 = Radius * Mathf.Cos(theta1);
            float y0 = SpringY + ArchRise * Mathf.Sin(theta0);
            float y1 = SpringY + ArchRise * Mathf.Sin(theta1);

            for (int i = 0; i < Sides; i++)
            {
                float a0 = 2f * Mathf.Pi * i       / Sides;
                float a1 = 2f * Mathf.Pi * (i + 1) / Sides;

                Vector3 p00 = new(Mathf.Sin(a0) * r0, y0, -Mathf.Cos(a0) * r0);
                Vector3 p10 = new(Mathf.Sin(a1) * r0, y0, -Mathf.Cos(a1) * r0);
                Vector3 p01 = new(Mathf.Sin(a0) * r1, y1, -Mathf.Cos(a0) * r1);
                Vector3 p11 = new(Mathf.Sin(a1) * r1, y1, -Mathf.Cos(a1) * r1);

                // Inward-facing winding (normals face toward centre/down)
                Quad(ceilST, p00, p10, p11, p01);
            }
        }

        // ── Commit geometry ────────────────────────────────────────────────────
        var body = new StaticBody3D { Name = "ArenaBody" };
        AddChild(body);
        AddMesh(body, Commit(wallST),  "ArenaWalls", isFloor: false);
        AddMesh(body, Commit(floorST), "ArenaFloor", isFloor: true);
        AddMesh(body, Commit(ceilST),  "ArenaCeil",  isFloor: false);

        // ── Torches evenly spaced around the ring ──────────────────────────────
        int torchCount = Sides / 2;
        for (int i = 0; i < torchCount; i++)
        {
            float angle = 2f * Mathf.Pi * i / torchCount;
            float tx    = Mathf.Sin(angle) * (Radius - 2f);
            float tz    = -Mathf.Cos(angle) * (Radius - 2f);
            AddChild(new OmniLight3D
            {
                Name            = $"ArenaLight{i}",
                Position        = new(tx, WallH * 0.55f, tz),
                LightColor      = new(1.0f, 0.68f, 0.22f),
                LightEnergy     = 2.8f,
                OmniRange       = 18f,
                OmniAttenuation = 0.6f,
                ShadowEnabled   = false,
            });
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    void AddMesh(StaticBody3D body, ArrayMesh? mesh, string name, bool isFloor)
    {
        if (mesh == null) return;
        var mi = new MeshInstance3D { Name = name, Mesh = mesh };
        mi.SetSurfaceOverrideMaterial(0, isFloor ? FloorMat() : StoneMat());
        body.AddChild(mi);
        body.AddChild(new CollisionShape3D
        {
            Name  = name + "Col",
            Shape = mesh.CreateTrimeshShape(),
        });
    }

    static SurfaceTool MakeST()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        return st;
    }

    static ArrayMesh? Commit(SurfaceTool st)
    {
        st.GenerateNormals();
        var m = st.Commit();
        return m.GetSurfaceCount() > 0 ? m : null;
    }

    static void Quad(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        st.SetUV(UV(a)); st.AddVertex(a);
        st.SetUV(UV(b)); st.AddVertex(b);
        st.SetUV(UV(c)); st.AddVertex(c);
        st.SetUV(UV(a)); st.AddVertex(a);
        st.SetUV(UV(c)); st.AddVertex(c);
        st.SetUV(UV(d)); st.AddVertex(d);
    }

    static Vector2 UV(Vector3 v) => new(v.X * 0.2f, v.Z * 0.2f);
}
