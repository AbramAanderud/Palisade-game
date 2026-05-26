using Godot;
using System.Collections.Generic;
using System.Linq;

/// res://scripts/Editor3DFloorPreview.cs
/// Editor-only 3D visualisation: one floor platform per placed piece, stair ramps
/// between adjacent floors. Owns its camera; supports left-drag orbit.
public partial class Editor3DFloorPreview : Node3D
{
    const float CS = DungeonBuilder.CellSize;    // 10 m per grid cell
    const float FH = DungeonBuilder.FloorHeight; // 12.4 m floor-to-floor

    static readonly Color CFloor = new(0.55f, 0.55f, 0.58f);
    static readonly Color CStair = new(0.90f, 0.75f, 0.15f);
    static readonly Color CStart = new(0.20f, 0.80f, 0.20f);
    static readonly Color CExit  = new(0.70f, 0.20f, 0.80f);

    // ── Camera orbit state ─────────────────────────────────────────────────────
    Camera3D? _cam;
    float  _orbitYaw    = 30f;    // degrees, horizontal rotation
    float  _orbitPitch  = 42f;    // degrees, vertical tilt (5..85)
    float  _orbitRadius = 190f;
    Vector3 _pivot      = new(50f, 0f, 50f);

    // ── Camera setup — call once after adding this node to the viewport ────────
    public void InitCamera(SubViewport viewport)
    {
        _cam = new Camera3D { Fov = 48f };
        viewport.AddChild(_cam);
        ApplyOrbit();
    }

    // ── Drag delta from mouse input ────────────────────────────────────────────
    public void Drag(float dx, float dy)
    {
        _orbitYaw   -= dx * 0.38f;
        _orbitPitch  = Mathf.Clamp(_orbitPitch + dy * 0.28f, 5f, 85f);
        ApplyOrbit();
    }

    void ApplyOrbit()
    {
        if (_cam == null) return;
        float yr  = Mathf.DegToRad(_orbitYaw);
        float pr  = Mathf.DegToRad(_orbitPitch);
        float cosP = Mathf.Cos(pr);
        var camPos = _pivot + new Vector3(
            _orbitRadius * Mathf.Sin(yr) * cosP,
            _orbitRadius * Mathf.Sin(pr),
            _orbitRadius * Mathf.Cos(yr) * cosP
        );
        _cam.Position = camPos;
        _cam.LookAt(_pivot, Vector3.Up);
    }

    // ── Rebuild from piece list ────────────────────────────────────────────────
    public void Rebuild(List<MazePiece> pieces)
    {
        foreach (var child in GetChildren())
            child.QueueFree();

        if (pieces.Count > 0)
            RecomputePivot(pieces);

        var matFloor = MakeMat(CFloor);
        var matStair = MakeMat(CStair);
        var matStart = MakeMat(CStart);
        var matExit  = MakeMat(CExit);

        foreach (var p in pieces)
        {
            if (PieceDB.IsStair(p.Type))
                AddRamp(p, matStair);
            else
            {
                var mat = p.Type == PieceType.Start ? matStart
                        : p.Type == PieceType.Exit  ? matExit
                        : matFloor;
                AddFloor(p, mat);
            }
        }
    }

    // ── Pivot + radius auto-fit ────────────────────────────────────────────────
    void RecomputePivot(List<MazePiece> pieces)
    {
        float minX = pieces.Min(p => p.X) * CS;
        float maxX = (pieces.Max(p => p.X) + 1) * CS;
        float minZ = pieces.Min(p => p.Y) * CS;
        float maxZ = (pieces.Max(p => p.Y) + 1) * CS;
        float minY = pieces.Min(p => p.Floor) * FH;
        float maxY = (pieces.Max(p => p.Floor) + 1) * FH;

        _pivot = new Vector3(
            (minX + maxX) * 0.5f,
            (minY + maxY) * 0.5f,
            (minZ + maxZ) * 0.5f
        );

        float extentH = Mathf.Max(maxX - minX, maxZ - minZ);
        float extentV  = maxY - minY;
        _orbitRadius = Mathf.Max(extentH, extentV) * 1.5f + 50f;

        ApplyOrbit();
    }

    // ── Floor platform ─────────────────────────────────────────────────────────
    void AddFloor(MazePiece p, StandardMaterial3D mat)
    {
        float cx = p.X * CS + CS * 0.5f;
        float cy = p.Floor * FH;
        float cz = p.Y * CS + CS * 0.5f;

        var mi = new MeshInstance3D
        {
            Mesh     = new BoxMesh { Size = new Vector3(CS, 0.30f, CS) },
            Position = new Vector3(cx, cy, cz),
        };
        mi.SetSurfaceOverrideMaterial(0, mat);
        AddChild(mi);
    }

    // ── Stair ramp — quad from flat end (same floor) to cross end (±1 floor) ───
    void AddRamp(MazePiece p, StandardMaterial3D mat)
    {
        var info = PieceDB.GetStairInfo(p.Type, p.Rotation);

        float flatY  = p.Floor * FH;
        float crossY = (p.Floor + info.FloorDelta) * FH;

        float x0 = p.X * CS, x1 = (p.X + 1) * CS;
        float z0 = p.Y * CS, z1 = (p.Y + 1) * CS;

        float flatEdgeZ  = EdgeZ(info.FlatDir,  z0, z1);
        float flatEdgeX  = EdgeX(info.FlatDir,  x0, x1);
        float crossEdgeZ = EdgeZ(info.CrossDir, z0, z1);
        float crossEdgeX = EdgeX(info.CrossDir, x0, x1);

        bool isNS = info.FlatDir == Dir.N || info.FlatDir == Dir.S;

        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        if (isNS)
        {
            var fl0 = new Vector3(x0, flatY,  flatEdgeZ);
            var fl1 = new Vector3(x1, flatY,  flatEdgeZ);
            var cr0 = new Vector3(x0, crossY, crossEdgeZ);
            var cr1 = new Vector3(x1, crossY, crossEdgeZ);
            Quad(st, fl0, fl1, cr1, cr0);
        }
        else
        {
            var fl0 = new Vector3(flatEdgeX,  flatY,  z0);
            var fl1 = new Vector3(flatEdgeX,  flatY,  z1);
            var cr0 = new Vector3(crossEdgeX, crossY, z0);
            var cr1 = new Vector3(crossEdgeX, crossY, z1);
            Quad(st, fl0, fl1, cr1, cr0);
        }

        st.SetMaterial(mat);
        var mi = new MeshInstance3D { Mesh = st.Commit() };
        AddChild(mi);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    static float EdgeZ(Dir d, float z0, float z1)
        => (d & Dir.N) != 0 ? z0 : (d & Dir.S) != 0 ? z1 : (z0 + z1) * 0.5f;

    static float EdgeX(Dir d, float x0, float x1)
        => (d & Dir.W) != 0 ? x0 : (d & Dir.E) != 0 ? x1 : (x0 + x1) * 0.5f;

    static void Quad(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        var n = (b - a).Cross(c - a).Normalized();
        st.SetNormal(n); st.AddVertex(a);
        st.SetNormal(n); st.AddVertex(b);
        st.SetNormal(n); st.AddVertex(c);
        st.SetNormal(n); st.AddVertex(a);
        st.SetNormal(n); st.AddVertex(c);
        st.SetNormal(n); st.AddVertex(d);
        // back face for double-sided visibility
        n = -n;
        st.SetNormal(n); st.AddVertex(a);
        st.SetNormal(n); st.AddVertex(d);
        st.SetNormal(n); st.AddVertex(c);
        st.SetNormal(n); st.AddVertex(a);
        st.SetNormal(n); st.AddVertex(c);
        st.SetNormal(n); st.AddVertex(b);
    }

    static StandardMaterial3D MakeMat(Color c) => new()
    {
        AlbedoColor = c,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        CullMode    = BaseMaterial3D.CullModeEnum.Disabled,
    };
}
