using Godot;
using System.Collections.Generic;
using System.Linq;

/// res://scripts/DungeonBuilder.cs
/// Generates a StaticBody3D dungeon mesh from MazeData.
///
/// Aesthetic: Gothic vaulted tunnels. Flat wall = half of total height, then
/// a parabolic barrel vault (or groin vault at corners) takes the ceiling to
/// double the wall height.  Corridor openings are wide so the arch is the
/// dominant visual element, not a door frame.
public partial class DungeonBuilder : Node3D
{
    // ── Cell dimensions ────────────────────────────────────────────────────────
    public const float CellSize    = 10f;   // footprint (m)
    public const float CellHeight  = 8.4f;  // flat wall section (+20% taller)
    public const float ArchRise    = 4.0f;  // arch above spring → total peak ~11 m
    public const float FloorHeight = CellHeight + ArchRise; // 11 m floor-to-floor
    public const float OpeningW    = 6.0f;  // corridor width (wall-to-wall)

    const int StairSteps = 20;             // individual steps per staircase

    // ── Procedural stone brick shader (world-space triplanar, no texture files needed) ──
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
        uv  = r;
        bid = floor(vec2(r.x + mod(floor(r.y), 2.0) * 0.5, r.y));
    } else {
        vec2 r = wpos.xy * brick_scale * vec2(0.6, 1.0);
        uv  = r;
        bid = floor(vec2(r.x + mod(floor(r.y), 2.0) * 0.5, r.y));
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

    [Signal] public delegate void DungeonReadyEventHandler();

    // ══════════════════════════════════════════════════════════════════════════
    //  PUBLIC API
    // ══════════════════════════════════════════════════════════════════════════
    public void Build(MazeData data)
    {
        foreach (var child in GetChildren()) child.QueueFree();

        var lookup = new Dictionary<(int, int, int), MazePiece>();
        foreach (var p in data.Pieces) lookup[(p.X, p.Y, p.Floor)] = p;

        foreach (var group in data.Pieces.GroupBy(p => p.Floor))
            BuildFloor(group.Key, group.ToList(), lookup);

        AddTorches(data.Pieces);
        EmitSignal(SignalName.DungeonReady);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  FLOOR MESH — one StaticBody3D per cell
    // ══════════════════════════════════════════════════════════════════════════
    // Splitting per-cell instead of per-floor fixes the GL lighting cap:
    // GL selects the closest max_lights_per_object lights relative to each
    // mesh object's bounding-box center. A 100m×100m merged floor mesh has
    // only its CENTER lit; corner cells go dark. A 10m×10m cell mesh only
    // pulls 4-9 neighboring torches — well under the 32-light limit.
    void BuildFloor(int floor, List<MazePiece> pieces,
        Dictionary<(int, int, int), MazePiece> lookup)
    {
        float yBase = floor * FloorHeight;

        foreach (var p in pieces)
        {
            var wallST  = MakeST();
            var floorST = MakeST();
            var ceilST  = MakeST();

            AddPiece(p, lookup, yBase, wallST, floorST, ceilST);

            var body = new StaticBody3D { Name = $"Cell_{p.X}_{p.Y}_{floor}" };
            AddChild(body);
            AddMesh(body, Commit(wallST),  $"Wall_{p.X}_{p.Y}",  default);
            AddMesh(body, Commit(floorST), $"Floor_{p.X}_{p.Y}", default);
            AddMesh(body, Commit(ceilST),  $"Ceil_{p.X}_{p.Y}",  default);

            // Smooth ramp collision for stairs — added to the same per-cell body
            if (p.Type == PieceType.Stairs)
                AddStairRamp(body, p, yBase);
        }
    }

    // Invisible ramp collision for one stair cell so CharacterBody3D can climb
    // without hitting individual riser faces.
    void AddStairRamp(StaticBody3D body, MazePiece p, float yBase)
    {
        float x0 = p.X * CellSize, z0 = p.Y * CellSize;
        float x1 = x0 + CellSize,  z1 = z0 + CellSize;
        float cx = x0 + CellSize * 0.5f, cz = z0 + CellSize * 0.5f;
        float hw = OpeningW * 0.5f;
        float yLo = yBase, yHi = yBase + FloorHeight;
        Dir upDir = PieceDB.GetStairUpDir(p.Rotation);

        var rampST = MakeST();
        switch (upDir)
        {
            case Dir.N:
                Quad(rampST, new(cx-hw, yLo, z1), new(cx+hw, yLo, z1),
                             new(cx+hw, yHi, z0), new(cx-hw, yHi, z0));
                break;
            case Dir.S:
                Quad(rampST, new(cx-hw, yHi, z1), new(cx+hw, yHi, z1),
                             new(cx+hw, yLo, z0), new(cx-hw, yLo, z0));
                break;
            case Dir.E:
                Quad(rampST, new(x0, yLo, cz+hw), new(x0, yLo, cz-hw),
                             new(x1, yHi, cz-hw), new(x1, yHi, cz+hw));
                break;
            case Dir.W:
                Quad(rampST, new(x1, yLo, cz-hw), new(x1, yLo, cz+hw),
                             new(x0, yHi, cz+hw), new(x0, yHi, cz-hw));
                break;
        }
        var rampMesh = Commit(rampST);
        if (rampMesh != null)
            body.AddChild(new CollisionShape3D { Name = "StairRamp", Shape = rampMesh.CreateTrimeshShape() });
    }

    // Instance fields — NOT static. Static Godot resources get silently freed when
    // a scene unloads; the C# null check still passes on the dead handle, causing
    // meshes to render black. Per-instance materials are recreated each dungeon build.
    ShaderMaterial? _stoneMat;
    ShaderMaterial? _floorMat;

    ShaderMaterial GetStoneMat()
    {
        if (_stoneMat != null && GodotObject.IsInstanceValid(_stoneMat)) return _stoneMat;
        var sh = new Shader(); sh.Code = StoneShaderSrc;
        _stoneMat = new ShaderMaterial { Shader = sh };
        return _stoneMat;
    }
    ShaderMaterial GetFloorMat()
    {
        if (_floorMat != null && GodotObject.IsInstanceValid(_floorMat)) return _floorMat;
        var sh = new Shader(); sh.Code = FloorShaderSrc;
        _floorMat = new ShaderMaterial { Shader = sh };
        return _floorMat;
    }

    void AddMesh(StaticBody3D body, ArrayMesh? mesh, string name, Color _unused)
    {
        if (mesh == null) return;
        var mi = new MeshInstance3D { Name = name, Mesh = mesh };
        bool isFloor = name.StartsWith("Floor") || name.StartsWith("Stair");
        mi.SetSurfaceOverrideMaterial(0, isFloor ? GetFloorMat() : GetStoneMat());
        body.AddChild(mi);
        var cs = new CollisionShape3D { Name = name + "Col", Shape = mesh.CreateTrimeshShape() };
        body.AddChild(cs);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  PIECE GEOMETRY
    //  Corridor geometry: floor/ceiling/walls span only OpeningW (6 m) wide.
    //  Each open direction contributes an arm strip; a center square connects them.
    // ══════════════════════════════════════════════════════════════════════════
    void AddPiece(MazePiece piece, Dictionary<(int, int, int), MazePiece> lookup,
        float yBase,
        SurfaceTool wallST, SurfaceTool floorST, SurfaceTool ceilST)
    {
        float x0 = piece.X * CellSize, z0 = piece.Y * CellSize;
        float x1 = x0 + CellSize,      z1 = z0 + CellSize;
        float cx = x0 + CellSize * 0.5f, cz = z0 + CellSize * 0.5f;
        float hw = OpeningW * 0.5f;          // 3 m — half corridor width
        float y0 = yBase, y1 = yBase + CellHeight;

        Dir op = PieceDB.GetOpenings(piece.Type, piece.Rotation);
        bool hasN = (op & Dir.N) != 0, hasS = (op & Dir.S) != 0;
        bool hasE = (op & Dir.E) != 0, hasW = (op & Dir.W) != 0;

        if (piece.Type == PieceType.Stairs)
        {
            AddStairGeometry(piece, wallST, floorST, ceilST, x0, z0, yBase);
            return;
        }

        // ── Floor ─────────────────────────────────────────────────────────────
        // Center square always present
        Quad(floorST, new(cx-hw,y0,cz-hw), new(cx+hw,y0,cz-hw),
                      new(cx+hw,y0,cz+hw), new(cx-hw,y0,cz+hw));
        // Arm strips (from center-square edge to cell face)
        if (hasN) Quad(floorST, new(cx-hw,y0,z0),    new(cx+hw,y0,z0),
                                new(cx+hw,y0,cz-hw),  new(cx-hw,y0,cz-hw));
        if (hasS) Quad(floorST, new(cx-hw,y0,cz+hw), new(cx+hw,y0,cz+hw),
                                new(cx+hw,y0,z1),     new(cx-hw,y0,z1));
        if (hasE) Quad(floorST, new(cx+hw,y0,cz-hw), new(x1,y0,cz-hw),
                                new(x1,y0,cz+hw),     new(cx+hw,y0,cz+hw));
        if (hasW) Quad(floorST, new(x0,y0,cz-hw),    new(cx-hw,y0,cz-hw),
                                new(cx-hw,y0,cz+hw),  new(x0,y0,cz+hw));

        // ── Vault ceiling ──────────────────────────────────────────────────────
        // Center square vault
        AddVaultCeiling(ceilST, cx-hw, cz-hw, cx+hw, cz+hw, y1, op);
        // Arm barrel vaults
        if (hasN) BarrelX(ceilST, cx-hw, z0,    cx+hw, cz-hw, y1);
        if (hasS) BarrelX(ceilST, cx-hw, cz+hw, cx+hw, z1,    y1);
        if (hasE) BarrelZ(ceilST, cx+hw, cz-hw, x1,   cz+hw,  y1);
        if (hasW) BarrelZ(ceilST, x0,   cz-hw, cx-hw,  cz+hw, y1);

        // ── Corridor side walls ────────────────────────────────────────────────
        // Arm side walls (run along the arm length, outside the corridor)
        if (hasN) { NSWall(wallST, cx-hw, z0,    cz-hw, y0, y1, +1);   // west side of N arm
                    NSWall(wallST, cx+hw, z0,    cz-hw, y0, y1, -1); } // east side
        if (hasS) { NSWall(wallST, cx-hw, cz+hw, z1,    y0, y1, +1);
                    NSWall(wallST, cx+hw, cz+hw, z1,    y0, y1, -1); }
        if (hasE) { EWWall(wallST, cz-hw, cx+hw, x1,    y0, y1, +1);   // north side of E arm
                    EWWall(wallST, cz+hw, cx+hw, x1,    y0, y1, -1); } // south side
        if (hasW) { EWWall(wallST, cz-hw, x0,   cx-hw,  y0, y1, +1);
                    EWWall(wallST, cz+hw, x0,   cx-hw,  y0, y1, -1); }

        // Center-square side walls on CLOSED directions (cap dead ends)
        float yTop = y1 + ArchRise; // cap walls extend to arch peak to seal void
        if (!hasN) CapWallNS(wallST, cx-hw, cx+hw, cz-hw, y0, yTop, +1); // faces +Z (inward)
        if (!hasS) CapWallNS(wallST, cx-hw, cx+hw, cz+hw, y0, yTop, -1); // faces -Z (inward)
        if (!hasE) CapWallEW(wallST, cz-hw, cz+hw, cx+hw, y0, yTop, -1); // faces -X (inward)
        if (!hasW) CapWallEW(wallST, cz-hw, cz+hw, cx-hw, y0, yTop, +1); // faces +X (inward)

        // Dead-end walls: open arms with no valid neighbor get a sealed face at the cell boundary
        if (hasN && !IsConnected(Dir.N, piece, lookup)) CapWallNS(wallST, cx-hw, cx+hw, z0,  y0, yTop, +1);
        if (hasS && !IsConnected(Dir.S, piece, lookup)) CapWallNS(wallST, cx-hw, cx+hw, z1,  y0, yTop, -1);
        if (hasE && !IsConnected(Dir.E, piece, lookup)) CapWallEW(wallST, cz-hw, cz+hw, x1,  y0, yTop, -1);
        if (hasW && !IsConnected(Dir.W, piece, lookup)) CapWallEW(wallST, cz-hw, cz+hw, x0,  y0, yTop, +1);
    }

    // Returns true when the adjacent cell exists and its openings face back toward us.
    static bool IsConnected(Dir dir, MazePiece piece, Dictionary<(int, int, int), MazePiece> lookup)
    {
        Dir opposite = dir switch { Dir.N => Dir.S, Dir.S => Dir.N, Dir.E => Dir.W, _ => Dir.E };
        (int nx, int ny) = dir switch
        {
            Dir.N => (piece.X, piece.Y - 1),
            Dir.S => (piece.X, piece.Y + 1),
            Dir.E => (piece.X + 1, piece.Y),
            _     => (piece.X - 1, piece.Y),
        };
        if (!lookup.TryGetValue((nx, ny, piece.Floor), out var neighbor)) return false;
        return (PieceDB.GetOpenings(neighbor.Type, neighbor.Rotation) & opposite) != 0;
    }

    // ── Corridor wall helpers ─────────────────────────────────────────────────
    // NSWall: vertical panel at constant x, running along Z. normalSign: +1=faces +X (west wall), -1=faces -X (east wall)
    static void NSWall(SurfaceTool st, float xPos, float zFrom, float zTo,
        float y0, float y1, int normalSign)
    {
        if (normalSign > 0) // normal → +X (west wall, player sees from right)
            Quad(st, new(xPos,y0,zFrom), new(xPos,y0,zTo),
                     new(xPos,y1,zTo),   new(xPos,y1,zFrom));
        else                // normal → -X (east wall)
            Quad(st, new(xPos,y0,zTo),  new(xPos,y0,zFrom),
                     new(xPos,y1,zFrom), new(xPos,y1,zTo));
    }

    // EWWall: vertical panel at constant z, running along X. normalSign: +1=faces +Z (north wall), -1=faces -Z (south wall)
    static void EWWall(SurfaceTool st, float zPos, float xFrom, float xTo,
        float y0, float y1, int normalSign)
    {
        if (normalSign > 0) // normal → +Z
            Quad(st, new(xTo,y0,zPos),  new(xFrom,y0,zPos),
                     new(xFrom,y1,zPos), new(xTo,y1,zPos));
        else                // normal → -Z
            Quad(st, new(xFrom,y0,zPos), new(xTo,y0,zPos),
                     new(xTo,y1,zPos),   new(xFrom,y1,zPos));
    }

    // CapWallNS: end-cap wall at a Z position (spans X). normalSign: +1=faces +Z, -1=faces -Z
    static void CapWallNS(SurfaceTool st, float xFrom, float xTo, float zPos,
        float y0, float y1, int normalSign)
        => EWWall(st, zPos, xFrom, xTo, y0, y1, normalSign);

    // CapWallEW: end-cap wall at an X position (spans Z). normalSign: +1=faces +X, -1=faces -X
    static void CapWallEW(SurfaceTool st, float zFrom, float zTo, float xPos,
        float y0, float y1, int normalSign)
        => NSWall(st, xPos, zFrom, zTo, y0, y1, normalSign);

    // ── Stairs ────────────────────────────────────────────────────────────────
    // Treads + risers (visible), plus clean full-height side walls and vault ceiling.
    // Smooth ramp collision is added separately in AddStairRamps so CharacterBody3D
    // can walk up without hitting the individual riser faces.
    void AddStairGeometry(MazePiece piece, SurfaceTool wallST,
        SurfaceTool floorST, SurfaceTool ceilST, float x0, float z0, float yBase)
    {
        float x1      = x0 + CellSize, z1 = z0 + CellSize;
        float cx      = x0 + CellSize * 0.5f, cz = z0 + CellSize * 0.5f;
        float hw      = OpeningW * 0.5f;
        float yLo      = yBase, yHi = yBase + FloorHeight;
        float springLo = yBase + CellHeight;          // vault spring at low end of stair
        float springHi = yBase + FloorHeight + CellHeight; // vault spring at high end
        Dir   upDir    = PieceDB.GetStairUpDir(piece.Rotation);
        int   N        = StairSteps;
        float rise     = FloorHeight / N;

        switch (upDir)
        {
            case Dir.N: // low at south (z1), high at north (z0)
            {
                float run = CellSize / N;
                // Bottom approach floor (first half of cell, before first step)
                Quad(floorST, new(cx-hw,yLo,z1), new(cx+hw,yLo,z1),
                              new(cx+hw,yLo,cz), new(cx-hw,yLo,cz));
                for (int i = 0; i < N; i++)
                {
                    float zBack  = z1 - i * run;
                    float zFront = z1 - (i + 1) * run;
                    float yTread = yLo + (i + 1) * rise;
                    float yPrev  = yLo + i * rise;
                    // Tread (flat top, normal up)
                    Quad(floorST,
                        new(cx-hw, yTread, zBack),  new(cx+hw, yTread, zBack),
                        new(cx+hw, yTread, zFront), new(cx-hw, yTread, zFront));
                    // Riser (vertical face, faces south +Z toward approaching player)
                    EWWall(wallST, zFront, cx-hw, cx+hw, yPrev, yTread, +1);
                }
                // Full-height side walls (simple rectangles — no winding ambiguity)
                NSWall(wallST, cx-hw, z0, z1, yLo, yHi, +1);  // west wall, faces +X
                NSWall(wallST, cx+hw, z0, z1, yLo, yHi, -1);  // east wall, faces -X
                // Cap wall at top exit (north face, faces south into dungeon)
                EWWall(wallST, z0, cx-hw, cx+hw, yLo, yHi, +1);
                // Rising vault: spring grows from springLo (south/bottom) to springHi (north/top)
                SlantedBarrelX(ceilST, cx-hw, z0, cx+hw, z1, springHi, springLo);
                break;
            }
            case Dir.S: // low at north (z0), high at south (z1)
            {
                float run = CellSize / N;
                Quad(floorST, new(cx-hw,yLo,z0), new(cx+hw,yLo,z0),
                              new(cx+hw,yLo,cz), new(cx-hw,yLo,cz));
                for (int i = 0; i < N; i++)
                {
                    float zBack  = z0 + i * run;
                    float zFront = z0 + (i + 1) * run;
                    float yTread = yLo + (i + 1) * rise;
                    float yPrev  = yLo + i * rise;
                    Quad(floorST,
                        new(cx-hw, yTread, zFront), new(cx+hw, yTread, zFront),
                        new(cx+hw, yTread, zBack),  new(cx-hw, yTread, zBack));
                    EWWall(wallST, zFront, cx-hw, cx+hw, yPrev, yTread, -1);
                }
                NSWall(wallST, cx-hw, z0, z1, yLo, yHi, +1);
                NSWall(wallST, cx+hw, z0, z1, yLo, yHi, -1);
                EWWall(wallST, z1, cx-hw, cx+hw, yLo, yHi, -1);
                // Rising vault: spring grows from springLo (north/bottom) to springHi (south/top)
                SlantedBarrelX(ceilST, cx-hw, z0, cx+hw, z1, springLo, springHi);
                break;
            }
            case Dir.E: // low at west (x0), high at east (x1)
            {
                float run = CellSize / N;
                Quad(floorST, new(x0,yLo,cz-hw), new(x0,yLo,cz+hw),
                              new(cx,yLo,cz+hw), new(cx,yLo,cz-hw));
                for (int i = 0; i < N; i++)
                {
                    float xBack  = x0 + i * run;
                    float xFront = x0 + (i + 1) * run;
                    float yTread = yLo + (i + 1) * rise;
                    float yPrev  = yLo + i * rise;
                    Quad(floorST,
                        new(xBack,  yTread, cz+hw), new(xBack,  yTread, cz-hw),
                        new(xFront, yTread, cz-hw), new(xFront, yTread, cz+hw));
                    NSWall(wallST, xFront, cz-hw, cz+hw, yPrev, yTread, -1);
                }
                EWWall(wallST, cz-hw, x0, x1, yLo, yHi, +1);  // north wall
                EWWall(wallST, cz+hw, x0, x1, yLo, yHi, -1);  // south wall
                NSWall(wallST, x1, cz-hw, cz+hw, yLo, yHi, -1);  // east cap
                // Rising vault: spring grows from springLo (west/bottom) to springHi (east/top)
                SlantedBarrelZ(ceilST, x0, cz-hw, x1, cz+hw, springLo, springHi);
                break;
            }
            case Dir.W: // low at east (x1), high at west (x0)
            {
                float run = CellSize / N;
                Quad(floorST, new(cx,yLo,cz-hw), new(cx,yLo,cz+hw),
                              new(x1,yLo,cz+hw), new(x1,yLo,cz-hw));
                for (int i = 0; i < N; i++)
                {
                    float xBack  = x1 - i * run;
                    float xFront = x1 - (i + 1) * run;
                    float yTread = yLo + (i + 1) * rise;
                    float yPrev  = yLo + i * rise;
                    Quad(floorST,
                        new(xFront, yTread, cz-hw), new(xFront, yTread, cz+hw),
                        new(xBack,  yTread, cz+hw), new(xBack,  yTread, cz-hw));
                    NSWall(wallST, xFront, cz-hw, cz+hw, yPrev, yTread, +1);
                }
                EWWall(wallST, cz-hw, x0, x1, yLo, yHi, +1);
                EWWall(wallST, cz+hw, x0, x1, yLo, yHi, -1);
                NSWall(wallST, x0, cz-hw, cz+hw, yLo, yHi, +1);  // west cap
                // Rising vault: spring grows from springHi (west/top) to springLo (east/bottom)
                SlantedBarrelZ(ceilST, x0, cz-hw, x1, cz+hw, springHi, springLo);
                break;
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  VAULT CEILING
    // Barrel vault for straight corridors; groin vault for corners / junctions.
    // The groin vault is two perpendicular parabolic barrels MAX-composited,
    // which naturally produces diagonal "groin" ribs at 90-degree turns.
    // ══════════════════════════════════════════════════════════════════════════
    static void AddVaultCeiling(SurfaceTool st,
        float x0, float z0, float x1, float z1, float springY, Dir openings)
    {
        bool ns = (openings & (Dir.N | Dir.S)) != 0;
        bool ew = (openings & (Dir.E | Dir.W)) != 0;

        if (ns && !ew)       BarrelX(st, x0, z0, x1, z1, springY);
        else if (ew && !ns)  BarrelZ(st, x0, z0, x1, z1, springY);
        else                 GroinVault(st, x0, z0, x1, z1, springY);
    }

    /// Barrel vault spanning X (N-S corridors).
    static void BarrelX(SurfaceTool st,
        float x0, float z0, float x1, float z1, float springY)
    {
        const int N = 12;
        for (int i = 0; i < N; i++)
        {
            float t0 = (float)i / N, t1 = (float)(i + 1) / N;
            float xi0 = x0 + (x1 - x0) * t0, xi1 = x0 + (x1 - x0) * t1;
            float yi0 = VaultY(t0, springY), yi1 = VaultY(t1, springY);
            // Z-reversed winding → normals face downward into corridor
            Quad(st, new(xi0,yi0,z1), new(xi1,yi1,z1),
                     new(xi1,yi1,z0), new(xi0,yi0,z0));
        }
    }

    /// Barrel vault spanning Z (E-W corridors).
    static void BarrelZ(SurfaceTool st,
        float x0, float z0, float x1, float z1, float springY)
    {
        const int N = 12;
        for (int i = 0; i < N; i++)
        {
            float t0 = (float)i / N, t1 = (float)(i + 1) / N;
            float zi0 = z0 + (z1 - z0) * t0, zi1 = z0 + (z1 - z0) * t1;
            float yi0 = VaultY(t0, springY), yi1 = VaultY(t1, springY);
            // X-reversed winding → normals face downward
            Quad(st, new(x1,yi0,zi0), new(x0,yi0,zi0),
                     new(x0,yi1,zi1), new(x1,yi1,zi1));
        }
    }

    /// Groin vault — two perpendicular barrel vaults composited via MAX.
    /// Creates natural diagonal groins at 45° across corner and junction cells.
    static void GroinVault(SurfaceTool st,
        float x0, float z0, float x1, float z1, float springY)
    {
        const int N = 10; // 10×10 = 100 quads per cell
        for (int ix = 0; ix < N; ix++)
        for (int iz = 0; iz < N; iz++)
        {
            float tx0 = (float)ix / N,     tx1 = (float)(ix + 1) / N;
            float tz0 = (float)iz / N,     tz1 = (float)(iz + 1) / N;
            float xi0 = x0+(x1-x0)*tx0,   xi1 = x0+(x1-x0)*tx1;
            float zi0 = z0+(z1-z0)*tz0,   zi1 = z0+(z1-z0)*tz1;

            float y00 = GroinY(tx0, tz0, springY);
            float y10 = GroinY(tx1, tz0, springY);
            float y01 = GroinY(tx0, tz1, springY);
            float y11 = GroinY(tx1, tz1, springY);

            // Same Z-reversed winding as BarrelX for consistent inward normals
            Quad(st, new(xi0,y01,zi1), new(xi1,y11,zi1),
                     new(xi1,y10,zi0), new(xi0,y00,zi0));
        }
    }

    static float VaultY(float t, float springY) =>
        springY + ArchRise * 4f * t * (1f - t);

    static float GroinY(float tx, float tz, float springY) =>
        springY + Mathf.Max(ArchRise * 4f * tx * (1f - tx),
                            ArchRise * 4f * tz * (1f - tz));

    // ── Slanted barrel vaults for stairwells ─────────────────────────────────
    // The spring height interpolates linearly from one end to the other, so the
    // vault follows the rising stair floor rather than arching symmetrically.
    //
    // SlantedBarrelX: vault arcs across X, rises along Z.
    //   Used for N-S stairs. springYAt_z0 = top end, springYAt_z1 = bottom end.
    static void SlantedBarrelX(SurfaceTool st,
        float x0, float z0, float x1, float z1,
        float springYAt_z0, float springYAt_z1)
    {
        const int Nx = 8, Nz = 10;
        for (int ix = 0; ix < Nx; ix++)
        for (int iz = 0; iz < Nz; iz++)
        {
            float tx0 = (float)ix / Nx, tx1 = (float)(ix + 1) / Nx;
            float tz0 = (float)iz / Nz, tz1 = (float)(iz + 1) / Nz;
            float xi0 = x0 + (x1 - x0) * tx0, xi1 = x0 + (x1 - x0) * tx1;
            float zi0 = z0 + (z1 - z0) * tz0, zi1 = z0 + (z1 - z0) * tz1;

            // Spring interpolates between z0 (top) and z1 (bottom)
            float sY0 = Mathf.Lerp(springYAt_z0, springYAt_z1, tz0);
            float sY1 = Mathf.Lerp(springYAt_z0, springYAt_z1, tz1);

            float archX0 = ArchRise * 4f * tx0 * (1f - tx0);
            float archX1 = ArchRise * 4f * tx1 * (1f - tx1);

            float y00 = sY0 + archX0; float y10 = sY0 + archX1;
            float y01 = sY1 + archX0; float y11 = sY1 + archX1;

            // Same winding as BarrelX (normals face inward/down)
            Quad(st, new(xi0,y01,zi1), new(xi1,y11,zi1),
                     new(xi1,y10,zi0), new(xi0,y00,zi0));
        }
    }

    // SlantedBarrelZ: vault arcs across Z, rises along X.
    //   Used for E-W stairs. springYAt_x0 = west-end spring, springYAt_x1 = east-end spring.
    static void SlantedBarrelZ(SurfaceTool st,
        float x0, float z0, float x1, float z1,
        float springYAt_x0, float springYAt_x1)
    {
        const int Nx = 10, Nz = 8;
        for (int ix = 0; ix < Nx; ix++)
        for (int iz = 0; iz < Nz; iz++)
        {
            float tx0 = (float)ix / Nx, tx1 = (float)(ix + 1) / Nx;
            float tz0 = (float)iz / Nz, tz1 = (float)(iz + 1) / Nz;
            float xi0 = x0 + (x1 - x0) * tx0, xi1 = x0 + (x1 - x0) * tx1;
            float zi0 = z0 + (z1 - z0) * tz0, zi1 = z0 + (z1 - z0) * tz1;

            // Spring interpolates along X
            float sX0 = Mathf.Lerp(springYAt_x0, springYAt_x1, tx0);
            float sX1 = Mathf.Lerp(springYAt_x0, springYAt_x1, tx1);

            float archZ0 = ArchRise * 4f * tz0 * (1f - tz0);
            float archZ1 = ArchRise * 4f * tz1 * (1f - tz1);

            float y00 = sX0 + archZ0; float y10 = sX1 + archZ0;
            float y01 = sX0 + archZ1; float y11 = sX1 + archZ1;

            // Same winding as BarrelZ (normals face inward/down)
            Quad(st, new(xi1,y10,zi0), new(xi0,y00,zi0),
                     new(xi0,y01,zi1), new(xi1,y11,zi1));
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  SURFACE TOOL PRIMITIVES
    // ══════════════════════════════════════════════════════════════════════════
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

    // ══════════════════════════════════════════════════════════════════════════
    //  LIGHTING
    // ══════════════════════════════════════════════════════════════════════════
    // One torch per cell, centred in the corridor.
    // Keeping count at 1-per-cell prevents the GL per-object light cap (32)
    // from darkening large maps where hundreds of wide-range lights previously
    // all competed to influence the same merged wall/floor mesh.
    // Start tiles get a green ambient glow; Exit tiles get a red one.
    void AddTorches(List<MazePiece> pieces)
    {
        int idx = 0;
        foreach (var piece in pieces)
        {
            if (piece.Type == PieceType.Stairs) continue;

            float cx    = piece.X * CellSize + CellSize * 0.5f;
            float cz    = piece.Y * CellSize + CellSize * 0.5f;
            float yBase = piece.Floor * FloorHeight;
            float ty    = yBase + CellHeight * 0.50f;  // mid-wall height

            // Colour-coded landmark lights for Start (green) and Exit (red)
            if (piece.Type == PieceType.Start)
            {
                AddChild(new OmniLight3D
                {
                    Name            = $"StartGlow{idx}",
                    Position        = new(cx, ty, cz),
                    LightColor      = new(0.15f, 1.0f, 0.25f),
                    LightEnergy     = 3.5f,
                    OmniRange       = 16f,
                    OmniAttenuation = 0.6f,
                    ShadowEnabled   = false,
                });
                idx++;
                continue;
            }
            if (piece.Type == PieceType.Exit)
            {
                AddChild(new OmniLight3D
                {
                    Name            = $"ExitGlow{idx}",
                    Position        = new(cx, ty, cz),
                    LightColor      = new(1.0f, 0.10f, 0.08f),
                    LightEnergy     = 3.5f,
                    OmniRange       = 16f,
                    OmniAttenuation = 0.6f,
                    ShadowEnabled   = false,
                });
                idx++;
                continue;
            }

            // Regular warm torch for all other corridor pieces
            AddChild(new OmniLight3D
            {
                Name            = $"Torch{idx}",
                Position        = new(cx, ty, cz),
                LightColor      = new(1.0f, 0.68f, 0.22f),
                LightEnergy     = 2.8f,
                OmniRange       = 14f,
                OmniAttenuation = 0.6f,
                ShadowEnabled   = false,
            });
            idx++;
        }
    }
}
