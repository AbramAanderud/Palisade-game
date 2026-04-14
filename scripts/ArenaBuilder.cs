using Godot;
using System;

/// res://scripts/ArenaBuilder.cs
/// Builds a tall circular arena room that bridges two dungeon exits.
///
/// Geometry:
///   - Walls: N=32 polygon cylinder, wall height = 2 × CellHeight
///   - Ceiling: hemisphere dome springing from the wall top
///   - Two arch doorways matching corridor cross-section (width=OpeningW, vault arch)
///   - Doorways face north (toward Maze A exit) and south (toward Maze B exit)
///
/// Gap elimination: arena is positioned so its face vertices lie exactly flush
/// with the exit tile south/north faces (no float gap).
public partial class ArenaBuilder : Node3D
{
    public const float WallH    = DungeonBuilder.CellHeight * 2f;  // 16.8 m
    public const float Radius   = 30f;
    const float OpeningW        = DungeonBuilder.OpeningW;         // 6.0 m
    const float ArchRise        = DungeonBuilder.ArchRise;         // 4.0 m
    const float SpringY         = DungeonBuilder.CellHeight;       // arch spring = normal CellHeight
    const int   Sides           = 32;
    const int   DomeRings       = 12;
    const int   ArchSegs        = 10;   // arch subdivisions

    // Angle offset so face 0 is centred exactly on north (±π/Sides from north)
    static float FaceHalf => Mathf.Pi / Sides;

    // The north face of the polygon reaches exactly this far from centre (apothem)
    public static float Apothem => Radius * Mathf.Cos(FaceHalf);

    // ── Shaders (duplicated from DungeonBuilder for self-contained build) ─────
    const string StoneShaderSrc = @"
shader_type spatial;
render_mode diffuse_burley, specular_schlick_ggx;
uniform vec3 base_color : source_color = vec3(0.42, 0.37, 0.30);
uniform vec3 mortar_color : source_color = vec3(0.22, 0.20, 0.18);
uniform float brick_scale = 0.28;
uniform float mortar_w = 0.07;
varying vec3 wpos; varying vec3 wnorm;
void vertex() {
    wpos  = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    wnorm = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}
float brick_mask(vec2 uv) {
    float row = floor(uv.y);
    vec2 b = fract(vec2(uv.x + mod(row,2.0)*0.5, uv.y));
    return step(mortar_w,b.x)*step(b.x,1.0-mortar_w)*step(mortar_w,b.y)*step(b.y,1.0-mortar_w);
}
float hash21(vec2 p){return fract(sin(dot(p,vec2(127.1,311.7)))*43758.5453);}
void fragment(){
    vec3 n=abs(wnorm); vec2 uv; vec2 bid;
    if(n.y>n.x&&n.y>n.z){uv=wpos.xz*brick_scale;bid=floor(vec2(uv.x+mod(floor(uv.y),2.0)*0.5,uv.y));}
    else if(n.x>n.z){vec2 r=wpos.zy*brick_scale*vec2(0.6,1.0);uv=r;bid=floor(vec2(r.x+mod(floor(r.y),2.0)*0.5,r.y));}
    else{vec2 r=wpos.xy*brick_scale*vec2(0.6,1.0);uv=r;bid=floor(vec2(r.x+mod(floor(r.y),2.0)*0.5,r.y));}
    float ib=brick_mask(uv); float hv=(hash21(bid)-0.5)*0.18;
    vec3 bc=clamp(base_color+vec3(hv,hv*0.85,hv*0.65),vec3(0.0),vec3(1.0));
    ALBEDO=mix(mortar_color,bc,ib); ROUGHNESS=0.93; METALLIC=0.0;
}";
    const string FloorShaderSrc = @"
shader_type spatial;
render_mode diffuse_burley, specular_schlick_ggx;
uniform vec3 stone_color : source_color = vec3(0.32, 0.29, 0.25);
uniform vec3 grout_color : source_color = vec3(0.18, 0.17, 0.15);
uniform float tile_scale = 0.20;
varying vec3 wpos;
void vertex(){wpos=(MODEL_MATRIX*vec4(VERTEX,1.0)).xyz;}
float hash21(vec2 p){return fract(sin(dot(p,vec2(127.1,311.7)))*43758.5453);}
void fragment(){
    vec2 uv=wpos.xz*tile_scale; vec2 cell=floor(uv); vec2 f=fract(uv);
    float gap=0.09;
    float it=step(gap,f.x)*step(f.x,1.0-gap)*step(gap,f.y)*step(f.y,1.0-gap);
    float hv=(hash21(cell)-0.5)*0.12;
    vec3 sc=clamp(stone_color+vec3(hv),vec3(0.0),vec3(1.0));
    ALBEDO=mix(grout_color,sc,it); ROUGHNESS=0.97; METALLIC=0.0;
}";

    ShaderMaterial? _stoneMat, _floorMat;
    ShaderMaterial StoneMat() { if (_stoneMat!=null&&GodotObject.IsInstanceValid(_stoneMat)) return _stoneMat; var sh=new Shader{Code=StoneShaderSrc}; return _stoneMat=new ShaderMaterial{Shader=sh}; }
    ShaderMaterial FloorMat() { if (_floorMat!=null&&GodotObject.IsInstanceValid(_floorMat)) return _floorMat; var sh=new Shader{Code=FloorShaderSrc}; return _floorMat=new ShaderMaterial{Shader=sh}; }

    // ══════════════════════════════════════════════════════════════════════════
    //  PUBLIC API
    // Build the arena room centred at 'centre' (local coords, parent sets world pos).
    // openNorth=true: leave north arch open (toward Maze A)
    // openSouth=true: leave south arch open (toward Maze B)
    // ══════════════════════════════════════════════════════════════════════════
    public void Build(Vector3 centre, bool openNorth = true, bool openSouth = true)
    {
        Position = centre;

        var wallST  = MakeST();
        var floorST = MakeST();
        var domeST  = MakeST();

        // Polygon vertices with half-face offset so face 0 is centred on north
        var outer = new Vector3[Sides];
        for (int i = 0; i < Sides; i++)
        {
            float a = 2f * Mathf.Pi * i / Sides - FaceHalf;
            outer[i] = new Vector3(Mathf.Sin(a) * Radius, 0f, -Mathf.Cos(a) * Radius);
        }

        // Half-angle of the opening in radians: enough to span OpeningW at the wall
        float openHalfAngle = Mathf.Asin(OpeningW * 0.5f / Radius);

        // ── Cylindrical walls ─────────────────────────────────────────────────
        for (int i = 0; i < Sides; i++)
        {
            float midAngle = 2f * Mathf.Pi * i / Sides - FaceHalf + Mathf.Pi / Sides;
            // North opening: midAngle near 0; south opening: midAngle near π
            bool isNorth = Mathf.Abs(midAngle) < openHalfAngle ||
                           Mathf.Abs(midAngle - 2f * Mathf.Pi) < openHalfAngle;
            bool isSouth = Mathf.Abs(midAngle - Mathf.Pi) < openHalfAngle;

            if ((isNorth && openNorth) || (isSouth && openSouth))
            {
                // Leave wall segment open — arch geometry added separately
                continue;
            }

            Vector3 a = outer[i];
            Vector3 b = outer[(i + 1) % Sides];
            // Inward-facing wall quad
            Quad(wallST,
                new(b.X, WallH, b.Z),
                new(a.X, WallH, a.Z),
                new(a.X, 0f, a.Z),
                new(b.X, 0f, b.Z));
        }

        // ── Arch doorways ──────────────────────────────────────────────────────
        // Each doorway matches the corridor cross-section: OpeningW wide, CellHeight
        // flat section, then a parabolic vault arch up to CellHeight + ArchRise.
        // The arch is placed at the wall circle radius (Apothem distance from centre).
        if (openNorth) AddArchDoorway(wallST, domeST, northFacing: true);
        if (openSouth) AddArchDoorway(wallST, domeST, northFacing: false);

        // ── Floor (polygon fan) ────────────────────────────────────────────────
        for (int i = 0; i < Sides; i++)
        {
            Vector3 a = outer[i];
            Vector3 b = outer[(i + 1) % Sides];
            floorST.SetUV(UV(Vector3.Zero)); floorST.AddVertex(Vector3.Zero);
            floorST.SetUV(UV(a));            floorST.AddVertex(a);
            floorST.SetUV(UV(b));            floorST.AddVertex(b);
        }

        // ── Dome ceiling ───────────────────────────────────────────────────────
        // Hemisphere from WallH (spring) up to WallH + Radius (apex)
        for (int ring = 0; ring < DomeRings; ring++)
        {
            float t0    = (float)ring / DomeRings;
            float t1    = (float)(ring + 1) / DomeRings;
            float theta0 = t0 * Mathf.Pi * 0.5f;
            float theta1 = t1 * Mathf.Pi * 0.5f;
            float r0     = Radius * Mathf.Cos(theta0);
            float r1     = Radius * Mathf.Cos(theta1);
            float y0     = WallH + Radius * Mathf.Sin(theta0);
            float y1     = WallH + Radius * Mathf.Sin(theta1);

            for (int i = 0; i < Sides; i++)
            {
                float a0 = 2f * Mathf.Pi * i / Sides - FaceHalf;
                float a1 = 2f * Mathf.Pi * (i + 1) / Sides - FaceHalf;
                Vector3 p00 = new(Mathf.Sin(a0)*r0, y0, -Mathf.Cos(a0)*r0);
                Vector3 p10 = new(Mathf.Sin(a1)*r0, y0, -Mathf.Cos(a1)*r0);
                Vector3 p01 = new(Mathf.Sin(a0)*r1, y1, -Mathf.Cos(a0)*r1);
                Vector3 p11 = new(Mathf.Sin(a1)*r1, y1, -Mathf.Cos(a1)*r1);
                // Inward-facing winding
                Quad(domeST, p00, p10, p11, p01);
            }
        }

        // ── Commit ─────────────────────────────────────────────────────────────
        var body = new StaticBody3D { Name = "ArenaBody" };
        AddChild(body);
        Emit(body, Commit(wallST),  "ArenaWalls", isFloor: false);
        Emit(body, Commit(floorST), "ArenaFloor", isFloor: true);
        Emit(body, Commit(domeST),  "ArenaDome",  isFloor: false);

        // ── Torches ring ───────────────────────────────────────────────────────
        int tc = Sides / 2;
        for (int i = 0; i < tc; i++)
        {
            float a  = 2f * Mathf.Pi * i / tc;
            float tx = Mathf.Sin(a) * (Radius - 2f);
            float tz = -Mathf.Cos(a) * (Radius - 2f);
            AddChild(new OmniLight3D
            {
                Name            = $"ArenaLight{i}",
                Position        = new(tx, WallH * 0.45f, tz),
                LightColor      = new(1.0f, 0.68f, 0.22f),
                LightEnergy     = 3.2f,
                OmniRange       = 22f,
                OmniAttenuation = 0.55f,
                ShadowEnabled   = false,
            });
        }
    }

    // ── Arch doorway geometry ─────────────────────────────────────────────────
    // Places a corridor-shaped arch in the arena wall:
    //   - Side walls left and right of the opening (from Apothem inward)
    //   - Flat header up to SpringY, then parabolic arch up to SpringY+ArchRise
    //   - Lintel connecting to rest of wall above the arch spring
    void AddArchDoorway(SurfaceTool wallST, SurfaceTool domeST, bool northFacing)
    {
        float hw    = OpeningW * 0.5f;
        float zFace = northFacing ? -Apothem : Apothem;  // wall plane Z
        float nSign = northFacing ? -1f : 1f;           // outward normal direction

        // ── Side jambs: wall flanking the arch ────────────────────────────────
        // West jamb: from wall edge to -hw at the face Z
        float wallEdge = Mathf.Sqrt(Mathf.Max(0f, Radius * Radius - zFace * zFace));
        // West side of corridor: x from -wallEdge to -hw
        AddVertPanel(wallST, -wallEdge, -hw, zFace, 0f, WallH, normalX: nSign > 0 ? 1f : -1f);
        // East side: x from +hw to +wallEdge
        AddVertPanel(wallST, hw, wallEdge, zFace, 0f, WallH, normalX: nSign > 0 ? 1f : -1f);

        // ── Arch soffit: barrel vault above corridor opening ──────────────────
        for (int i = 0; i < ArchSegs; i++)
        {
            float t0 = (float)i / ArchSegs, t1 = (float)(i + 1) / ArchSegs;
            float xi0 = -hw + OpeningW * t0, xi1 = -hw + OpeningW * t1;
            float archY0 = SpringY + ArchRise * 4f * t0 * (1f - t0);
            float archY1 = SpringY + ArchRise * 4f * t1 * (1f - t1);

            // Flat section below arch spring (if needed) — just the spring-to-wall line
            // Arch panel (facing inward / downward)
            if (northFacing)
                Quad(domeST,
                    new(xi0, archY0, zFace),
                    new(xi1, archY1, zFace),
                    new(xi1, WallH,  zFace),
                    new(xi0, WallH,  zFace));
            else
                Quad(domeST,
                    new(xi1, archY1, zFace),
                    new(xi0, archY0, zFace),
                    new(xi0, WallH,  zFace),
                    new(xi1, WallH,  zFace));
        }

        // ── Lintel: flat wall above arch spring up to WallH ───────────────────
        // (filled by the arch panels above — nothing extra needed)
    }

    // Vertical flat panel at constant Z, spanning x from xA to xB, y from y0 to y1
    static void AddVertPanel(SurfaceTool st, float xA, float xB, float z,
        float y0, float y1, float normalX)
    {
        if (normalX > 0)
            Quad(st, new(xA, y0, z), new(xB, y0, z), new(xB, y1, z), new(xA, y1, z));
        else
            Quad(st, new(xB, y0, z), new(xA, y0, z), new(xA, y1, z), new(xB, y1, z));
    }

    // ── Mesh helpers ──────────────────────────────────────────────────────────
    void Emit(StaticBody3D body, ArrayMesh? mesh, string name, bool isFloor)
    {
        if (mesh == null) return;
        var mi = new MeshInstance3D { Name = name, Mesh = mesh };
        mi.SetSurfaceOverrideMaterial(0, isFloor ? FloorMat() : StoneMat());
        body.AddChild(mi);
        body.AddChild(new CollisionShape3D { Name = name + "Col", Shape = mesh.CreateTrimeshShape() });
    }

    static SurfaceTool MakeST()
    { var st = new SurfaceTool(); st.Begin(Mesh.PrimitiveType.Triangles); return st; }

    static ArrayMesh? Commit(SurfaceTool st)
    { st.GenerateNormals(); var m = st.Commit(); return m.GetSurfaceCount() > 0 ? m : null; }

    static void Quad(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        st.SetUV(UV(a)); st.AddVertex(a); st.SetUV(UV(b)); st.AddVertex(b);
        st.SetUV(UV(c)); st.AddVertex(c); st.SetUV(UV(a)); st.AddVertex(a);
        st.SetUV(UV(c)); st.AddVertex(c); st.SetUV(UV(d)); st.AddVertex(d);
    }

    static Vector2 UV(Vector3 v) => new(v.X * 0.2f, v.Z * 0.2f);
}
