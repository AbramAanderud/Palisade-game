using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// New 3D-first maze editor ("Online Maze" from the title screen).
/// Black world, white geometry, 45°-isometric camera with right-click orbit.
/// Per-floor grids; lower floors dimmed.  3D thumbnail piece palette.
/// 5 save slots (uses MazeSerializer slots 0–4).
public partial class MazeEditor3D : Control
{
    // ── World constants ────────────────────────────────────────────────────────
    const float CS    = DungeonBuilder.CellSize;      // 10 m per cell
    const float FH    = DungeonBuilder.FloorHeight;   // 18 m floor-to-floor
    const float WallH = 5.5f;                         // visual wall height in editor
    const float WallT = 0.28f;
    const float FlrT  = 0.22f;
    const int   GW    = 10;
    const int   GH    = 10;
    const int   FlMin = -3;
    const int   FlMax =  3;
    const int   MaxSlots  = 5;
    const int   PanW     = 220;
    const int   ThSz     = 96;
    const int   PalBarH  = 128;

    const float CentW  = CS * 0.50f;                // 5m central room square
    const float ArmW   = CS * 0.40f;                // 4m corridor arm width
    const float ArmLen = (CS - CentW) * 0.5f;       // 2.5m arm length
    const float EdFH   = 8f;                        // editor display floor spacing (visual only)

    static readonly Vector3 GridCentre = new(GW * CS * 0.5f, 0f, GH * CS * 0.5f);

    // ── Palette order ──────────────────────────────────────────────────────────
    static readonly PieceType[] Palette =
    {
        PieceType.Straight, PieceType.LHall,
        PieceType.THall, PieceType.Cross,
        PieceType.StairsUp, PieceType.StairsDown,
    };

    // Start is always on the bottom row (cy = GH-1), Exit always on the top row (cy = 0)
    const int StartRow = GH - 1;
    const int ExitRow  = 0;

    // ── Colors ─────────────────────────────────────────────────────────────────
    static readonly Color CFloor   = new(0.82f, 0.82f, 0.82f);
    static readonly Color CWall    = new(1.00f, 1.00f, 1.00f);
    static readonly Color CDimFloor= new(0.28f, 0.28f, 0.28f, 0.45f);
    static readonly Color CDimWall = new(0.50f, 0.50f, 0.50f, 0.16f);
    static readonly Color CStart   = new(0.20f, 0.88f, 0.28f);
    static readonly Color CExit    = new(0.88f, 0.20f, 0.20f);
    static readonly Color CStair   = new(0.88f, 0.62f, 0.12f);
    static readonly Color CSel     = new(1.00f, 0.80f, 0.15f);
    static readonly Color CText    = new(0.85f, 0.85f, 0.85f);
    static readonly Color CDim     = new(0.48f, 0.48f, 0.48f);
    static readonly Color CPan     = new(0.04f, 0.04f, 0.04f);

    // ── Editor state ───────────────────────────────────────────────────────────
    int        _slot     = 0;
    int        _floor    = 0;
    PieceType? _selType  = null;
    int        _rotation = 0;
    MazeData   _maze     = new() { Name = "New Maze", Pieces = new() };

    // Camera / orbit
    float   _camYaw   = -0.55f;   // ≈NW start
    float   _camPitch = 0.60f;
    float   _camDist  = 145f;
    bool    _rmb      = false;
    bool    _rmbDrag  = false;
    Vector2 _rmbStart = Vector2.Zero;

    // Pick-up / move state
    bool      _holding    = false;
    PieceType _heldType   = default;
    int       _heldRot    = 0;
    int       _heldOX     = -1;
    int       _heldOY     = -1;
    int       _heldOFloor = 0;
    Node3D?   _heldGeo    = null;

    // Palette preview ghost (shown when a palette type is selected and mouse is over grid)
    Node3D?   _previewGeo = null;

    // Hover highlight
    int       _hoverCx = -1;
    int       _hoverCy = -1;
    readonly List<MeshInstance3D> _hoverBorder = new();

    // ── Nodes ──────────────────────────────────────────────────────────────────
    FontFile?            _font;
    SubViewport          _mainVp = null!;
    Camera3D             _cam    = null!;
    Node3D               _world  = null!;
    SubViewportContainer _vpCont = null!;

    // Geometry keyed by cell
    record struct CK(int X, int Y, int F);
    readonly Dictionary<CK, Node3D>        _cellGeo     = new();
    readonly Dictionary<int, MeshInstance3D> _gridMi    = new();
    readonly List<MeshInstance3D>            _edgeWalls  = new();
    readonly List<MeshInstance3D>            _borderHints = new();
    readonly List<Node3D>                    _stairShadowGeo = new();

    // Palette
    readonly Dictionary<PieceType, Panel>       _palPan  = new();
    readonly Dictionary<PieceType, SubViewport> _thumbVp = new();

    // UI refs
    Label    _floorLbl  = null!;
    Label    _statusLbl = null!;
    LineEdit _nameEdit  = null!;
    Button[] _slotBtns  = new Button[MaxSlots];

    // ── _Ready ─────────────────────────────────────────────────────────────────
    public override void _Ready()
    {
        _font = GD.Load<FontFile>("res://assets/fonts/Agmena Pro Book.ttf");
        BuildUI();
        SetupMainVp();
        SetupThumbs();
        BuildFloorGrids();
        LoadSlot(0);
    }

    // ── _Process ───────────────────────────────────────────────────────────────
    public override void _Process(double _dt) => UpdateCamera();

    // ── Keyboard shortcuts ─────────────────────────────────────────────────────
    public override void _Input(InputEvent ev)
    {
        if (ev is not InputEventKey k || !k.Pressed) return;
        switch (k.Keycode)
        {
            case Key.R:      RotateOnce(); break;
            case Key.Escape:
                if (_holding) CancelHolding();
                else { _selType = null; RefreshPalette(); SetStatus(); RebuildBorderHints(); RebuildPreviewGeo(); }
                break;
        }
    }

    // ── UI construction ────────────────────────────────────────────────────────
    void BuildUI()
    {
        var bg = new ColorRect { Color = Colors.Black };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        // ── Left panel ─────────────────────────────────────────────────────────
        var left = new Panel();
        left.SetAnchorsPreset(LayoutPreset.LeftWide);
        left.OffsetRight = PanW;
        left.AddThemeStyleboxOverride("panel", Flat(CPan));
        AddChild(left);

        var lv = MakeVBox(8);
        lv.SetAnchorsPreset(LayoutPreset.FullRect);
        left.AddChild(lv);

        lv.AddChild(Lbl("MAZE EDITOR", 14, Colors.White, HorizontalAlignment.Center));
        lv.AddChild(new HSeparator());

        lv.AddChild(Lbl("NAME", 10, CDim));
        _nameEdit = new LineEdit { PlaceholderText = "Maze name…" };
        StyleLineEdit(_nameEdit);
        _nameEdit.TextChanged += t => { _maze.Name = t; RefreshSlotButtons(); };
        lv.AddChild(_nameEdit);
        lv.AddChild(new HSeparator());

        lv.AddChild(Lbl("SAVE SLOTS", 10, CDim));
        for (int i = 0; i < MaxSlots; i++)
        {
            int idx = i;
            _slotBtns[i] = Btn($"  {i + 1}", 11, () => { SaveCurrentSlot(); LoadSlot(idx); });
            _slotBtns[i].SizeFlagsHorizontal = SizeFlags.ExpandFill;
            _slotBtns[i].CustomMinimumSize   = new Vector2(0, 34);
            lv.AddChild(_slotBtns[i]);
        }
        lv.AddChild(new HSeparator());

        // Floor navigation
        var fRow = new HBoxContainer();
        fRow.AddThemeConstantOverride("separation", 4);
        var fUp   = Btn("▲", 14, FloorUp);
        var fDn   = Btn("▼", 14, FloorDown);
        fUp.CustomMinimumSize = fDn.CustomMinimumSize = new Vector2(40, 36);
        _floorLbl = Lbl($"Floor {_floor}", 12, Colors.White, HorizontalAlignment.Center);
        _floorLbl.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        fRow.AddChild(fUp);
        fRow.AddChild(_floorLbl);
        fRow.AddChild(fDn);
        lv.AddChild(fRow);
        lv.AddChild(new HSeparator());

        var rotBtn = Btn("↻  R", 12, RotateOnce);
        rotBtn.CustomMinimumSize = new Vector2(0, 32);
        lv.AddChild(rotBtn);
        lv.AddChild(new HSeparator());

        _statusLbl = Lbl("", 10, CDim);
        _statusLbl.AutowrapMode = TextServer.AutowrapMode.Word;
        lv.AddChild(_statusLbl);
        lv.AddChild(new HSeparator());

        var saveBtn  = Btn("SAVE",          12, SaveCurrentSlot);
        var clearBtn = Btn("CLEAR MAP",     12, ClearCurrentMap);
        var enterBtn = Btn("ENTER DUNGEON", 12, OnEnterDungeon);
        var backBtn  = Btn("< Main Menu",   11, () => { SaveCurrentSlot(); GetTree().ChangeSceneToFile("res://scenes/TitleScreen.tscn"); });
        saveBtn .CustomMinimumSize = clearBtn.CustomMinimumSize =
        enterBtn.CustomMinimumSize = backBtn .CustomMinimumSize = new Vector2(0, 36);
        lv.AddChild(saveBtn);
        lv.AddChild(clearBtn);
        lv.AddChild(enterBtn);
        lv.AddChild(backBtn);

        // ── Centre viewport container ───────────────────────────────────────────
        _vpCont = new SubViewportContainer { Stretch = true };
        _vpCont.SetAnchorsPreset(LayoutPreset.FullRect);
        _vpCont.OffsetLeft   =  PanW;
        _vpCont.OffsetRight  =  0;
        _vpCont.OffsetBottom = -PalBarH;
        _vpCont.GuiInput += HandleVpInput;
        AddChild(_vpCont);

        // ── Bottom piece palette bar ────────────────────────────────────────────
        var bot = new Panel();
        bot.AnchorLeft   = 0f; bot.AnchorRight  = 1f;
        bot.AnchorTop    = 1f; bot.AnchorBottom = 1f;
        bot.GrowVertical = GrowDirection.Begin;
        bot.OffsetLeft   = PanW;
        bot.OffsetTop    = -PalBarH;
        bot.OffsetBottom = 0f;
        bot.AddThemeStyleboxOverride("panel", Flat(CPan));
        AddChild(bot);

        var hbox = new HBoxContainer();
        hbox.SetAnchorsPreset(LayoutPreset.FullRect);
        hbox.AddThemeConstantOverride("separation", 10);
        hbox.Alignment = BoxContainer.AlignmentMode.Center;
        bot.AddChild(hbox);

        foreach (var pt in Palette)
            hbox.AddChild(BuildPaletteCell(pt));

        RefreshSlotButtons();
    }

    Control BuildPaletteCell(PieceType pt)
    {
        // Transparent panel — selection is shown by tinting the background only
        var outer = new Panel();
        outer.CustomMinimumSize = new Vector2(ThSz + 8, ThSz + 20);
        outer.AddThemeStyleboxOverride("panel", Flat(new Color(0, 0, 0, 0)));
        _palPan[pt] = outer;

        var vb = new VBoxContainer();
        vb.SetAnchorsPreset(LayoutPreset.FullRect);
        vb.AddThemeConstantOverride("separation", 3);
        outer.AddChild(vb);

        var tr = new TextureRect
        {
            Name             = "Thumb",
            CustomMinimumSize = new Vector2(ThSz, ThSz),
            ExpandMode       = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode      = TextureRect.StretchModeEnum.KeepAspectCentered,
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
        };
        vb.AddChild(tr);

        var lbl = Lbl(PieceDB.ShortLabels.TryGetValue(pt, out var sl) ? sl : pt.ToString(),
                      9, CDim, HorizontalAlignment.Center);
        lbl.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        vb.AddChild(lbl);

        outer.GuiInput += ev =>
        {
            if (ev is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left && mb.Pressed)
                SelectType(pt);
        };
        return outer;
    }

    // ── Main SubViewport ───────────────────────────────────────────────────────
    void SetupMainVp()
    {
        _mainVp = new SubViewport
        {
            Size                   = new Vector2I(1280, 720),
            RenderTargetClearMode  = SubViewport.ClearMode.Always,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            HandleInputLocally     = false,
            OwnWorld3D             = true,   // isolate from thumbnail worlds
        };
        _vpCont.AddChild(_mainVp);

        _world = new Node3D { Name = "World" };
        _mainVp.AddChild(_world);

        _cam = new Camera3D { Fov = 48f, Current = true };
        _mainVp.AddChild(_cam);

        var wenv = new WorldEnvironment();
        var env  = new Godot.Environment
        {
            BackgroundMode     = Godot.Environment.BGMode.Color,
            BackgroundColor    = Colors.Black,
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor  = new Color(0.28f, 0.28f, 0.28f),
            AmbientLightEnergy = 0.65f,
        };
        wenv.Environment = env;
        _mainVp.AddChild(wenv);

        var dlight = new DirectionalLight3D
        {
            Rotation     = new Vector3(-0.75f, 0.55f, 0f),
            LightEnergy  = 0.85f,
            LightColor   = new Color(0.95f, 0.95f, 1f),
        };
        _mainVp.AddChild(dlight);

        UpdateCamera();
    }

    // ── Thumbnail SubViewports ─────────────────────────────────────────────────
    void SetupThumbs()
    {
        // Standalone SubViewports (no SubViewportContainer needed) with unique names
        // so each GetTexture() returns a distinct ViewportTexture path.
        // Invisible-parent wrappers break SubViewport layout; plain Node avoids that.
        var holder = new Node { Name = "ThumbHolder" };
        AddChild(holder);

        foreach (var pt in Palette)
        {
            var svp = new SubViewport
            {
                Name                   = $"Vp_{pt}",
                Size                   = new Vector2I(ThSz, ThSz),
                RenderTargetClearMode  = SubViewport.ClearMode.Always,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
                HandleInputLocally     = false,
                OwnWorld3D             = true,   // each thumbnail gets its own 3D world
            };
            holder.AddChild(svp);
            _thumbVp[pt] = svp;

            var wenv = new WorldEnvironment();
            var env  = new Godot.Environment
            {
                BackgroundMode     = Godot.Environment.BGMode.Color,
                BackgroundColor    = new Color(0.07f, 0.07f, 0.09f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor  = new Color(0.45f, 0.45f, 0.45f),
                AmbientLightEnergy = 0.75f,
            };
            wenv.Environment = env;
            svp.AddChild(wenv);

            // Orthographic top-down: exactly matches what the editor looks like
            var cam = new Camera3D
            {
                Projection = Camera3D.ProjectionType.Orthogonal,
                Size       = CS * 1.35f,   // fits cell with a small margin
                Position   = new Vector3(CS * 0.5f, 60f, CS * 0.5f),
            };
            svp.AddChild(cam);   // must be in tree before LookAt
            cam.LookAt(new Vector3(CS * 0.5f, 0f, CS * 0.5f), new Vector3(0, 0, -1));

            var light = new DirectionalLight3D
            {
                Rotation    = new Vector3(-0.85f, 0.65f, 0f),
                LightEnergy = 1.1f,
            };
            svp.AddChild(light);

            var root = new Node3D();
            svp.AddChild(root);
            BuildPieceInto(root, pt, 0, 0f, 0f, 0f, isActive: true);

            WireThumbTexture(pt);
        }
    }

    void WireThumbTexture(PieceType pt)
    {
        if (!_palPan.TryGetValue(pt, out var panel)) return;
        var tr = panel.GetNodeOrNull<TextureRect>("VBoxContainer/Thumb");
        if (tr == null)
        {
            // BuildPaletteCell adds a VBoxContainer as first child
            foreach (var child in panel.GetChildren())
            {
                if (child is VBoxContainer vb)
                {
                    tr = vb.GetNodeOrNull<TextureRect>("Thumb");
                    break;
                }
            }
        }
        if (tr != null && _thumbVp.TryGetValue(pt, out var svp))
            tr.Texture = svp.GetTexture();
    }

    // ── Floor grids ────────────────────────────────────────────────────────────
    void BuildFloorGrids()
    {
        for (int f = FlMin; f <= FlMax; f++)
        {
            var mi = BuildGridMesh(f, GridAlpha(f));
            _world.AddChild(mi);
            _gridMi[f] = mi;
        }
    }

    MeshInstance3D BuildGridMesh(int floor, float alpha)
    {
        float y    = floor * EdFH + FlrT + 0.05f;
        float barW = 0.40f;   // visible at 145 m camera distance
        float barH = 0.12f;

        var mat = new StandardMaterial3D
        {
            AlbedoColor  = new Color(0.55f, 0.55f, 0.55f, alpha),
            Transparency = alpha < 0.999f
                ? BaseMaterial3D.TransparencyEnum.Alpha
                : BaseMaterial3D.TransparencyEnum.Disabled,
            ShadingMode  = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };

        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        void AddBar(Vector3 centre, Vector3 size)
        {
            float hx = size.X * 0.5f, hy = size.Y * 0.5f, hz = size.Z * 0.5f;
            Vector3[] c =
            {
                centre + new Vector3(-hx, -hy, -hz), centre + new Vector3( hx, -hy, -hz),
                centre + new Vector3( hx,  hy, -hz), centre + new Vector3(-hx,  hy, -hz),
                centre + new Vector3(-hx, -hy,  hz), centre + new Vector3( hx, -hy,  hz),
                centre + new Vector3( hx,  hy,  hz), centre + new Vector3(-hx,  hy,  hz),
            };
            void Tri(int a, int b, int d) { st.AddVertex(c[a]); st.AddVertex(c[b]); st.AddVertex(c[d]); }
            Tri(0,2,1); Tri(0,3,2);  // -Z
            Tri(4,5,6); Tri(4,6,7);  // +Z
            Tri(0,4,7); Tri(0,7,3);  // -X
            Tri(1,2,6); Tri(1,6,5);  // +X
            Tri(3,7,6); Tri(3,6,2);  // +Y
            Tri(0,1,5); Tri(0,5,4);  // -Y
        }

        for (int i = 0; i <= GW; i++)
        {
            float x = i * CS, lenZ = GH * CS;
            AddBar(new Vector3(x, y, lenZ * 0.5f), new Vector3(barW, barH, lenZ));
        }
        for (int j = 0; j <= GH; j++)
        {
            float z = j * CS, lenX = GW * CS;
            AddBar(new Vector3(lenX * 0.5f, y, z), new Vector3(lenX, barH, barW));
        }

        st.GenerateNormals();
        return new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = mat };
    }

    float GridAlpha(int f)
        => f == _floor ? 0.80f : f < _floor ? 0.14f : 0.04f;

    void RefreshGridOpacity()
    {
        foreach (var (f, mi) in _gridMi)
        {
            if (mi.MaterialOverride is StandardMaterial3D m)
                m.AlbedoColor = new Color(0.55f, 0.55f, 0.55f, GridAlpha(f));
        }
    }

    // ── Piece geometry ─────────────────────────────────────────────────────────
    void RebuildGeometry()
    {
        foreach (var n in _cellGeo.Values) n.QueueFree();
        _cellGeo.Clear();
        foreach (var n in _stairShadowGeo) { if (IsInstanceValid(n)) n.QueueFree(); }
        _stairShadowGeo.Clear();

        foreach (var p in _maze.Pieces)
        {
            var  key = new CK(p.X, p.Y, p.Floor);
            bool act = p.Floor == _floor;
            var  geo = new Node3D();
            _world.AddChild(geo);
            BuildPieceInto(geo, p.Type, p.Rotation,
                           p.X * CS, p.Floor * EdFH, p.Y * CS, act);
            _cellGeo[key] = geo;
        }

        // Stair landings: when this stair connects to the currently-viewed floor,
        // draw an inverse stair at the cross-exit cell so the connection is visible
        // from both floors.
        foreach (var p in _maze.Pieces)
        {
            if (!PieceDB.IsStair(p.Type)) continue;
            int connectedFloor = p.Floor + PieceDB.StairFloorDelta(p.Type);
            if (connectedFloor != _floor) continue;

            Dir crossDir = PieceDB.GetStairCrossDir(p.Type, p.Rotation);
            var (cdx, cdy) = DirToOffset(crossDir);
            int exitX = p.X + cdx;
            int exitY = p.Y + cdy;
            if (exitX < 0 || exitX >= GW || exitY < 0 || exitY >= GH) continue;

            // Skip if a real piece already occupies the landing cell on this floor
            bool occupied = _maze.Pieces.Any(q =>
                q.X == exitX && q.Y == exitY && q.Floor == _floor);
            if (occupied) continue;

            PieceType invType = p.Type == PieceType.StairsUp
                              ? PieceType.StairsDown : PieceType.StairsUp;
            int invRot = FindRotationForCrossDir(invType, PieceDB.Opposite(crossDir));

            var shadow = new Node3D();
            _world.AddChild(shadow);
            BuildPieceInto(shadow, invType, invRot,
                           exitX * CS, _floor * EdFH, exitY * CS, isActive: true);
            _stairShadowGeo.Add(shadow);
        }

        RebuildEdgeWalls();
    }

    static int FindRotationForCrossDir(PieceType stairType, Dir desiredCross)
    {
        for (int r = 0; r < 4; r++)
            if (PieceDB.GetStairCrossDir(stairType, r) == desiredCross) return r;
        return 0;
    }

    void BuildPieceInto(Node3D parent, PieceType type, int rot,
                        float wx, float wy, float wz, bool isActive)
    {
        Color floorCol = type switch
        {
            PieceType.Start                             => CStart,
            PieceType.Exit                              => CExit,
            PieceType.StairsUp or PieceType.StairsDown => CStair,
            _                                           => CFloor,
        };
        if (!isActive) floorCol = CDimFloor;

        float cx   = wx + CS * 0.5f;
        float cy   = wy + FlrT * 0.5f;
        float cz   = wz + CS * 0.5f;
        float flrA = isActive ? 1f : 0.5f;

        // Central square
        parent.AddChild(MakeMeshBox(
            new Vector3(cx, cy, cz),
            new Vector3(CentW - 0.1f, FlrT, CentW - 0.1f),
            floorCol, flrA));

        Dir open = PieceDB.GetOpenings(type, rot);

        void ArmFloor(float ax, float az, float aw, float ad)
            => parent.AddChild(MakeMeshBox(
                   new Vector3(ax, cy, az),
                   new Vector3(aw - 0.1f, FlrT, ad - 0.1f),
                   floorCol, flrA));

        if ((open & Dir.N) != 0) ArmFloor(cx,              wz + ArmLen * 0.5f,      ArmW, ArmLen);
        if ((open & Dir.S) != 0) ArmFloor(cx,              wz + CS - ArmLen * 0.5f, ArmW, ArmLen);
        if ((open & Dir.E) != 0) ArmFloor(wx + CS - ArmLen * 0.5f, cz,              ArmLen, ArmW);
        if ((open & Dir.W) != 0) ArmFloor(wx + ArmLen * 0.5f,      cz,              ArmLen, ArmW);

        if (type == PieceType.StairsUp || type == PieceType.StairsDown)
            BuildStairGeo(parent, type, rot, wx, wy, wz, isActive);
    }

    void BuildStairGeo(Node3D parent, PieceType type, int rot,
                       float wx, float wy, float wz, bool isActive)
    {
        int  delta      = PieceDB.StairFloorDelta(type);
        Dir  highEndDir = delta > 0
            ? PieceDB.GetStairCrossDir(type, rot)
            : PieceDB.GetStairFlatDir(type, rot);

        // Unit vector pointing from the low end of the stair toward the high end
        float fwdX = highEndDir == Dir.E ? 1f : highEndDir == Dir.W ? -1f : 0f;
        float fwdZ = highEndDir == Dir.S ? 1f : highEndDir == Dir.N ? -1f : 0f;

        // lowY = floor the stairs sit on; highY = connected floor above/below
        float lowY  = delta > 0 ? wy          : wy + delta * EdFH;
        float highY = delta > 0 ? wy + EdFH   : wy;

        // 8 steps spanning the full EdFH → each step is 1 m tall × 1.25 m deep
        const int NS = 8;
        float stepH = CS / NS;
        float stepV = EdFH / NS;

        float lx = wx + CS * 0.5f - fwdX * CS * 0.5f;
        float lz = wz + CS * 0.5f - fwdZ * CS * 0.5f;

        Color col   = isActive ? CStair : CDimFloor;
        float alpha = isActive ? 0.95f  : 0.45f;

        for (int i = 0; i < NS; i++)
        {
            float scx = lx + fwdX * (i + 0.5f) * stepH;
            float scy = lowY + (i + 1) * stepV + FlrT * 0.5f;
            float scz = lz + fwdZ * (i + 0.5f) * stepH;

            float tw = fwdZ != 0 ? CentW - 0.1f : stepH * 0.88f;
            float td = fwdX != 0 ? CentW - 0.1f : stepH * 0.88f;

            parent.AddChild(MakeMeshBox(new Vector3(scx, scy, scz),
                                        new Vector3(tw, FlrT, td), col, alpha));
        }

        // Shaft connecting the two floor levels — shows the vertical link clearly
        float sx = wx + CS * 0.5f + fwdX * CS * 0.38f;
        float sz = wz + CS * 0.5f + fwdZ * CS * 0.38f;
        parent.AddChild(MakeMeshBox(
            new Vector3(sx, (lowY + highY) * 0.5f, sz),
            new Vector3(0.9f, highY - lowY, 0.9f),
            CStair, isActive ? 0.22f : 0.07f));
    }

    // ── Camera update ──────────────────────────────────────────────────────────
    void UpdateCamera()
    {
        float cp = Mathf.Cos(_camPitch), sp = Mathf.Sin(_camPitch);
        float cy = Mathf.Cos(_camYaw),   sy = Mathf.Sin(_camYaw);

        float targetFloorY = _floor * EdFH;
        var   target  = new Vector3(GridCentre.X, targetFloorY + WallH * 0.35f, GridCentre.Z);
        var   offset  = new Vector3(sy * cp, sp, cy * cp) * _camDist;

        _cam.Position = target + offset;
        // Near-vertical pitch: use -Z as up so North stays at screen-top
        var up = _camPitch > 1.50f ? new Vector3(0, 0, -1) : Vector3.Up;
        _cam.LookAt(target, up);
    }

    // ── Viewport input handling ────────────────────────────────────────────────
    void HandleVpInput(InputEvent ev)
    {
        if (ev is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Right)
            {
                if (mb.Pressed)
                {
                    _rmb      = true;
                    _rmbDrag  = false;
                    _rmbStart = mb.Position;
                }
                else
                {
                    bool wasDrag = _rmbDrag;
                    _rmb = _rmbDrag = false;
                    // Short right-click with no drag = delete (Start/Exit are permanent)
                    if (!wasDrag && TryGetCell(mb.Position, out int cx, out int cy))
                    {
                        if (TryGetPlacedPiece(cx, cy, out var rp) &&
                            (rp.Type == PieceType.Start || rp.Type == PieceType.Exit))
                        {
                            SetStatus($"{PieceDB.Labels[rp.Type]} cannot be removed — move it instead.");
                        }
                        else
                        {
                            RemovePiece(cx, cy);
                            RebuildGeometry();   // also refreshes stair shadows
                        }
                    }
                }
                GetViewport().SetInputAsHandled();
            }
            else if (mb.ButtonIndex == MouseButton.Left && mb.Pressed)
            {
                if (TryGetCell(mb.Position, out int cx, out int cy))
                {
                    if (_holding)
                        DropPiece(cx, cy);
                    else if (_cellGeo.ContainsKey(new CK(cx, cy, _floor)))
                        PickUpPiece(cx, cy);   // existing piece always takes priority
                    else if (_selType.HasValue)
                        PlacePiece(cx, cy);
                }
                else if (_holding)
                {
                    // Clicked outside the grid while holding a piece → discard it
                    // (Start/Exit can't be deleted, so they return to their origin instead)
                    AbandonHolding();
                }
                GetViewport().SetInputAsHandled();
            }
            else if (mb.ButtonIndex == MouseButton.WheelUp)
            {
                _camDist = Mathf.Max(_camDist - 10f, 30f);
                GetViewport().SetInputAsHandled();
            }
            else if (mb.ButtonIndex == MouseButton.WheelDown)
            {
                _camDist = Mathf.Min(_camDist + 10f, 400f);
                GetViewport().SetInputAsHandled();
            }
        }
        else if (ev is InputEventMouseMotion mm)
        {
            if (_rmb)
            {
                if ((mm.Position - _rmbStart).Length() > 4f) _rmbDrag = true;
                if (_rmbDrag)
                {
                    _camYaw   -= mm.Relative.X * 0.005f;
                    _camPitch += mm.Relative.Y * 0.005f;
                    _camPitch  = Mathf.Clamp(_camPitch, 0.05f, 1.55f);
                }
                GetViewport().SetInputAsHandled();
            }
            else
            {
                if (TryGetCell(mm.Position, out int hcx, out int hcy))
                    SetHover(hcx, hcy);
                else
                    SetHover(-1, -1);
            }
        }
    }

    bool TryGetCell(Vector2 evPos, out int cx, out int cy)
    {
        cx = cy = -1;
        var vpSize = new Vector2(_mainVp.Size.X, _mainVp.Size.Y);
        var contSz = _vpCont.Size;
        if (contSz.X <= 0 || contSz.Y <= 0) return false;

        var vpPos = evPos / contSz * vpSize;
        var origin = _cam.ProjectRayOrigin(vpPos);
        var dir    = _cam.ProjectRayNormal(vpPos);

        float planeY = _floor * EdFH;
        if (Mathf.Abs(dir.Y) < 0.001f) return false;
        float t = (planeY - origin.Y) / dir.Y;
        if (t < 0f) return false;

        var hit = origin + dir * t;
        cx = (int)Mathf.Floor(hit.X / CS);
        cy = (int)Mathf.Floor(hit.Z / CS);
        return cx >= 0 && cx < GW && cy >= 0 && cy < GH;
    }

    // ── Hover highlight ────────────────────────────────────────────────────────
    void SetHover(int cx, int cy)
    {
        if (cx == _hoverCx && cy == _hoverCy) return;
        _hoverCx = cx;
        _hoverCy = cy;
        UpdateHoverHighlight();
        if (_holding && _heldGeo != null)
        {
            _heldGeo.Visible = (cx >= 0);
            if (cx >= 0)
                _heldGeo.Position = new Vector3(cx * CS, _floor * EdFH, cy * CS);
        }
        if (_previewGeo != null)
        {
            _previewGeo.Visible = (cx >= 0 && _selType.HasValue);
            if (cx >= 0)
                _previewGeo.Position = new Vector3(cx * CS, _floor * EdFH, cy * CS);
        }
    }

    void UpdateHoverHighlight()
    {
        foreach (var mi in _hoverBorder) { if (IsInstanceValid(mi)) mi.QueueFree(); }
        _hoverBorder.Clear();

        if (_hoverCx < 0 || _hoverCy < 0) return;
        if (!_holding && !_selType.HasValue) return;

        // Determine if this hover cell is a valid target
        PieceType? activeType = _holding ? _heldType : _selType;
        bool invalid = activeType == PieceType.Start && _hoverCy != StartRow
                    || activeType == PieceType.Exit  && _hoverCy != ExitRow;

        float y  = _floor * EdFH + FlrT + 0.1f;
        float x0 = _hoverCx * CS;
        float z0 = _hoverCy * CS;
        const float bW = 0.28f, bH = 0.1f;

        var hoverColor = invalid
            ? new Color(0.9f, 0.15f, 0.15f, 0.95f)   // red = invalid
            : new Color(CSel.R, CSel.G, CSel.B, 0.95f); // yellow = valid

        var mat = new StandardMaterial3D
        {
            AlbedoColor = hoverColor,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        void Bar(Vector3 pos, Vector3 sz)
        {
            var mi = new MeshInstance3D { Mesh = new BoxMesh { Size = sz }, MaterialOverride = mat, Position = pos };
            _world.AddChild(mi);
            _hoverBorder.Add(mi);
        }
        // 4 bars forming a cell border (corners overlap intentionally)
        Bar(new Vector3(x0 + CS * 0.5f, y, z0      ), new Vector3(CS, bH, bW));
        Bar(new Vector3(x0 + CS * 0.5f, y, z0 + CS ), new Vector3(CS, bH, bW));
        Bar(new Vector3(x0,             y, z0 + CS * 0.5f), new Vector3(bW, bH, CS));
        Bar(new Vector3(x0 + CS,        y, z0 + CS * 0.5f), new Vector3(bW, bH, CS));
    }

    // ── Pick-up / move ─────────────────────────────────────────────────────────
    void PickUpPiece(int cx, int cy)
    {
        int idx = _maze.Pieces.FindIndex(p => p.X == cx && p.Y == cy && p.Floor == _floor);
        if (idx < 0) return;

        var p = _maze.Pieces[idx];
        _heldType   = p.Type;
        _heldRot    = p.Rotation;
        _heldOX     = p.X;
        _heldOY     = p.Y;
        _heldOFloor = p.Floor;

        RemovePiece(cx, cy);

        // Yellow ghost at the same cell; parent node moves with mouse
        _heldGeo = new Node3D();
        _world.AddChild(_heldGeo);
        BuildPieceInto(_heldGeo, _heldType, _heldRot, 0f, 0f, 0f, isActive: true);
        YellowTint(_heldGeo);
        _heldGeo.Position = new Vector3(cx * CS, _floor * EdFH, cy * CS);

        _holding = true;
        _selType = null;
        RefreshPalette();
        RebuildPreviewGeo();   // clears palette preview while holding
        RebuildGeometry();     // also refreshes stair shadows after removal
        RebuildBorderHints();
        SetStatus($"Moving {PieceDB.Labels[_heldType]}  —  R to rotate  |  click to place  |  Esc to cancel");
    }

    void DropPiece(int cx, int cy)
    {
        if (!_holding) return;
        if (!IsValidPlacement(cx, cy, _heldType, _heldRot)) return;

        _heldGeo?.QueueFree();
        _heldGeo = null;

        RemovePiece(cx, cy);   // clear any existing piece at destination

        var piece = new MazePiece { X = cx, Y = cy, Floor = _floor,
                                    Type = _heldType, Rotation = _heldRot };
        _maze.Pieces.Add(piece);

        var key = new CK(cx, cy, _floor);
        var geo = new Node3D();
        _world.AddChild(geo);
        BuildPieceInto(geo, _heldType, _heldRot, cx * CS, _floor * EdFH, cy * CS, isActive: true);
        _cellGeo[key] = geo;

        _holding = false;
        RebuildGeometry();   // also refreshes stair shadows
        RebuildBorderHints();
        UpdateHoverHighlight();
        SetStatus($"Placed {PieceDB.Labels[_heldType]}");
    }

    void CancelHolding()
    {
        if (!_holding) return;

        _heldGeo?.QueueFree();
        _heldGeo = null;

        // Restore to original position
        var piece = new MazePiece { X = _heldOX, Y = _heldOY, Floor = _heldOFloor,
                                    Type = _heldType, Rotation = _heldRot };
        _maze.Pieces.Add(piece);

        var key = new CK(_heldOX, _heldOY, _heldOFloor);
        var geo = new Node3D();
        _world.AddChild(geo);
        BuildPieceInto(geo, _heldType, _heldRot,
                       _heldOX * CS, _heldOFloor * EdFH, _heldOY * CS,
                       isActive: _heldOFloor == _floor);
        _cellGeo[key] = geo;

        _holding = false;
        RebuildGeometry();   // also refreshes stair shadows
        RebuildBorderHints();
        UpdateHoverHighlight();
        SetStatus();
    }

    static void YellowTint(Node3D node)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is MeshInstance3D mi && mi.MaterialOverride is StandardMaterial3D mat)
                mat.AlbedoColor = new Color(CSel.R, CSel.G, CSel.B, mat.AlbedoColor.A * 0.75f);
            if (child is Node3D n) YellowTint(n);
        }
    }

    // ── Place / remove ─────────────────────────────────────────────────────────
    void PlacePiece(int cx, int cy)
    {
        if (!_selType.HasValue) return;
        var pt = _selType.Value;

        if (pt == PieceType.StairsUp   && _floor >= FlMax) { SetStatus("Can't place: top floor.");    return; }
        if (pt == PieceType.StairsDown && _floor <= FlMin) { SetStatus("Can't place: bottom floor."); return; }

        // Auto-rotate: snap rotation to connect with neighbors / stair landings
        int useRot = PieceDB.IsStair(pt)
            ? InferStairRotation(pt, cx, cy, _floor, fallback: _rotation)
            : InferRotation(pt, cx, cy, _floor, fallback: _rotation);

        if (!IsValidPlacement(cx, cy, pt, useRot)) return;

        RemovePiece(cx, cy);   // clear existing

        var piece = new MazePiece { X = cx, Y = cy, Floor = _floor, Type = pt, Rotation = useRot };
        _maze.Pieces.Add(piece);

        var key = new CK(cx, cy, _floor);
        var geo = new Node3D();
        _world.AddChild(geo);
        BuildPieceInto(geo, pt, useRot, cx * CS, _floor * EdFH, cy * CS, isActive: true);
        _cellGeo[key] = geo;

        RebuildGeometry();   // refresh stair-shadow rendering
        SetStatus($"Placed {PieceDB.Labels[pt]}");
    }

    void RemovePiece(int cx, int cy)
    {
        var key = new CK(cx, cy, _floor);
        _maze.Pieces.RemoveAll(p => p.X == cx && p.Y == cy && p.Floor == _floor);
        if (_cellGeo.TryGetValue(key, out var geo))
        {
            geo.QueueFree();
            _cellGeo.Remove(key);
        }
    }

    // Generic "you let go of the piece" — Start/Exit can't be deleted, so they snap back
    // to their original cell; every other type is discarded.
    void AbandonHolding()
    {
        if (!_holding) return;
        if (_heldType == PieceType.Start || _heldType == PieceType.Exit)
            CancelHolding();
        else
            DiscardHolding();
    }

    void DiscardHolding()
    {
        if (!_holding) return;
        _heldGeo?.QueueFree();
        _heldGeo = null;
        _holding = false;
        RebuildGeometry();   // also refreshes stair shadows
        RebuildBorderHints();
        UpdateHoverHighlight();
        SetStatus($"Discarded {PieceDB.Labels[_heldType]}");
    }

    // ── Floor navigation ───────────────────────────────────────────────────────
    void FloorUp()
    {
        if (_floor >= FlMax) return;
        _floor++;
        OnFloorChanged();
    }

    void FloorDown()
    {
        if (_floor <= FlMin) return;
        _floor--;
        OnFloorChanged();
    }

    void OnFloorChanged()
    {
        _floorLbl.Text = $"Floor {_floor}";
        RefreshGridOpacity();
        RebuildGeometry();
    }

    // ── Piece type selection ───────────────────────────────────────────────────
    void SelectType(PieceType pt)
    {
        // Picking from the palette while holding a piece discards it (Start/Exit return to origin)
        if (_holding) AbandonHolding();

        bool newType = _selType != pt;
        _selType = pt;
        RefreshPalette();
        RebuildBorderHints();
        RebuildPreviewGeo();
        SetStatus($"{PieceDB.Labels[pt]}  [{PieceDB.GoldCosts[pt]}g]");
        // Snap to top-down only when switching to a different piece type
        if (newType)
        {
            _camPitch = 1.55f;
            _camYaw   = -0.55f;
        }
    }

    void RotateOnce()
    {
        if (_holding)
        {
            _heldRot = (_heldRot + 1) % 4;
            _heldGeo?.QueueFree();
            _heldGeo = new Node3D();
            _world.AddChild(_heldGeo);
            BuildPieceInto(_heldGeo, _heldType, _heldRot, 0f, 0f, 0f, isActive: true);
            YellowTint(_heldGeo);
            if (_hoverCx >= 0)
                _heldGeo.Position = new Vector3(_hoverCx * CS, _floor * EdFH, _hoverCy * CS);
            SetStatus($"Moving {PieceDB.Labels[_heldType]}  rot {_heldRot * 90}°  —  click to place  |  Esc to cancel");
        }
        else
        {
            _rotation = (_rotation + 1) % 4;
            RebuildPreviewGeo();
            SetStatus(_selType.HasValue
                ? $"{PieceDB.Labels[_selType.Value]}  rot {_rotation * 90}°"
                : $"Rotation: {_rotation * 90}°");
        }
    }

    void RefreshPalette()
    {
        foreach (var (pt, panel) in _palPan)
        {
            bool sel = _selType == pt;
            // Selected: subtle warm tint; unselected: fully transparent (floating look)
            panel.AddThemeStyleboxOverride("panel",
                Flat(sel ? new Color(0.20f, 0.17f, 0.03f, 0.75f) : new Color(0, 0, 0, 0)));
        }
    }

    // ── Clear ──────────────────────────────────────────────────────────────────
    void ClearCurrentMap()
    {
        _maze.Pieces.Clear();
        EnsureStartExit();
        RebuildGeometry();
        SetStatus("Map cleared.");
    }

    // ── Save / load ────────────────────────────────────────────────────────────
    public void SaveCurrentSlot()
    {
        if (_nameEdit != null && _nameEdit.Text.Length > 0)
            _maze.Name = _nameEdit.Text;
        _maze.IsOnline    = true;
        _maze.IsGameReady = MazeValidator.IsGameReady(_maze, out string readyErr);
        MazeSerializer.Save(_slot, _maze);
        RefreshSlotButtons();
        SetStatus(_maze.IsGameReady
            ? "Saved — maze is game-ready."
            : $"Saved — not game-ready: {readyErr}");
    }

    void LoadSlot(int s)
    {
        _slot = s;
        var data = MazeSerializer.Load(s);
        _maze = data ?? new MazeData { Name = $"Maze {s + 1}", Pieces = new() };
        if (_nameEdit != null) _nameEdit.Text = _maze.Name;
        EnsureStartExit();
        RebuildGeometry();
        RefreshSlotButtons();
        SetStatus("Slot loaded.");
    }

    void RefreshSlotButtons()
    {
        for (int i = 0; i < MaxSlots; i++)
        {
            var  data   = MazeSerializer.Load(i);
            string name = data?.Name ?? "(empty)";
            int pieces  = data?.Pieces?.Count ?? 0;
            _slotBtns[i].Text = $"  {i + 1}.  {name}  [{pieces}p]";

            bool isCur = i == _slot;
            _slotBtns[i].AddThemeStyleboxOverride("normal",
                Flat(isCur ? new Color(0.14f, 0.12f, 0.04f) : new Color(0.08f, 0.08f, 0.08f),
                     isCur ? CSel : new Color(0.18f, 0.18f, 0.18f)));
        }
    }

    // ── Enter dungeon ──────────────────────────────────────────────────────────
    void OnEnterDungeon()
    {
        bool hasStart = _maze.Pieces.Any(p => p.Type == PieceType.Start);
        bool hasExit  = _maze.Pieces.Any(p => p.Type == PieceType.Exit);
        if (!hasStart || !hasExit)
        {
            SetStatus(!hasStart ? "Need a Start piece." : "Need an Exit piece.");
            return;
        }
        SaveCurrentSlot();
        GameState.ActiveSlot        = _slot;
        GameState.EditorReturnScene = "res://scenes/MazeEditor3D.tscn";
        GetTree().ChangeSceneToFile("res://scenes/DungeonGame.tscn");
    }

    void SetStatus(string msg = "")
    {
        if (_statusLbl == null) return;
        _statusLbl.Text = msg.Length > 0 ? msg
            : _selType.HasValue ? $"{PieceDB.Labels[_selType.Value]}  {_rotation * 90}°"
            : "Select a piece →";
    }

    // ── Mesh helpers ───────────────────────────────────────────────────────────
    static MeshInstance3D MakeMeshBox(Vector3 pos, Vector3 size, Color col, float alpha = 1f)
    {
        var mat = new StandardMaterial3D
        {
            AlbedoColor  = new Color(col.R, col.G, col.B, alpha),
            ShadingMode  = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = alpha < 0.999f
                ? BaseMaterial3D.TransparencyEnum.Alpha
                : BaseMaterial3D.TransparencyEnum.Disabled,
        };
        return new MeshInstance3D
        {
            Mesh             = new BoxMesh { Size = size },
            MaterialOverride = mat,
            Position         = pos,
        };
    }

    // ── UI helpers ─────────────────────────────────────────────────────────────
    Label Lbl(string text, int size, Color col,
              HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var l = new Label { Text = text, HorizontalAlignment = align };
        if (_font != null) l.AddThemeFontOverride("font", _font);
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", col);
        return l;
    }

    Button Btn(string text, int size, Action pressed)
    {
        var b = new Button { Text = text };
        if (_font != null) b.AddThemeFontOverride("font", _font);
        b.AddThemeFontSizeOverride("font_size", size);
        b.Pressed += pressed;
        return b;
    }

    VBoxContainer MakeVBox(int margin)
    {
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 4);
        v.AddThemeConstantOverride("margin_left",   margin);
        v.AddThemeConstantOverride("margin_right",  margin);
        v.AddThemeConstantOverride("margin_top",    margin);
        v.AddThemeConstantOverride("margin_bottom", margin);
        return v;
    }

    void StyleLineEdit(LineEdit le)
    {
        if (_font != null) le.AddThemeFontOverride("font", _font);
        le.AddThemeFontSizeOverride("font_size", 12);
    }

    static StyleBoxFlat Flat(Color bg, Color? border = null)
    {
        var s = new StyleBoxFlat { BgColor = bg };
        if (border.HasValue) { s.BorderColor = border.Value; s.SetBorderWidthAll(1); }
        return s;
    }

    // ── Palette preview ghost ─────────────────────────────────────────────────
    void RebuildPreviewGeo()
    {
        _previewGeo?.QueueFree();
        _previewGeo = null;
        if (!_selType.HasValue) return;

        _previewGeo = new Node3D { Visible = false };
        _world.AddChild(_previewGeo);
        BuildPieceInto(_previewGeo, _selType.Value, _rotation, 0f, 0f, 0f, isActive: true);
        YellowTint(_previewGeo);

        // Position at current hover cell if already over the grid
        if (_hoverCx >= 0)
        {
            _previewGeo.Visible  = true;
            _previewGeo.Position = new Vector3(_hoverCx * CS, _floor * EdFH, _hoverCy * CS);
        }
    }

    // ── Permanent Start / Exit pieces ─────────────────────────────────────────
    void EnsureStartExit()
    {
        if (!_maze.Pieces.Any(p => p.Type == PieceType.Start))
            _maze.Pieces.Add(new MazePiece { X = GW / 2, Y = StartRow, Floor = 0,
                                             Type = PieceType.Start, Rotation = 0 });
        if (!_maze.Pieces.Any(p => p.Type == PieceType.Exit))
            _maze.Pieces.Add(new MazePiece { X = GW / 2, Y = ExitRow,  Floor = 0,
                                             Type = PieceType.Exit,  Rotation = 0 });
    }

    // ── Edge wall system ───────────────────────────────────────────────────────
    // Draws full-width translucent walls at every closed cell boundary on the current floor.
    void RebuildEdgeWalls()
    {
        foreach (var mi in _edgeWalls) { if (IsInstanceValid(mi)) mi.QueueFree(); }
        _edgeWalls.Clear();

        float wallMidY = _floor * EdFH + FlrT + WallH * 0.5f;
        const float alpha = 0.38f;

        var placed = new System.Collections.Generic.Dictionary<(int, int), MazePiece>();
        foreach (var p in _maze.Pieces)
            if (p.Floor == _floor)
                placed[(p.X, p.Y)] = p;

        static Dir OpenOf(MazePiece? p) => p == null ? Dir.None : PieceDB.GetOpenings(p.Type, p.Rotation);

        // Horizontal edges: wall panel at z = cy*CS, running along X (N face of row cy / S face of row cy-1)
        for (int cy = 0; cy <= GH; cy++)
        for (int cx = 0; cx < GW;  cx++)
        {
            bool hasA = placed.TryGetValue((cx, cy - 1), out var above);
            bool hasB = placed.TryGetValue((cx, cy),     out var below);
            if (!hasA && !hasB) continue;

            bool aOpen = hasA && (OpenOf(above) & Dir.S) != 0;
            bool bOpen = hasB && (OpenOf(below) & Dir.N) != 0;

            bool draw = hasA && hasB ? !(aOpen && bOpen)
                      : hasA        ? !aOpen
                                    : !bOpen;
            if (!draw) continue;

            var mi = MakeMeshBox(
                new Vector3(cx * CS + CS * 0.5f, wallMidY, cy * CS),
                new Vector3(CS - 0.04f, WallH, WallT), CWall, alpha);
            _world.AddChild(mi);
            _edgeWalls.Add(mi);
        }

        // Vertical edges: wall panel at x = cx*CS, running along Z (W face of col cx / E face of col cx-1)
        for (int cx = 0; cx <= GW; cx++)
        for (int cy = 0; cy < GH;  cy++)
        {
            bool hasL = placed.TryGetValue((cx - 1, cy), out var left);
            bool hasR = placed.TryGetValue((cx,     cy), out var right);
            if (!hasL && !hasR) continue;

            bool lOpen = hasL && (OpenOf(left)  & Dir.E) != 0;
            bool rOpen = hasR && (OpenOf(right) & Dir.W) != 0;

            bool draw = hasL && hasR ? !(lOpen && rOpen)
                      : hasL        ? !lOpen
                                    : !rOpen;
            if (!draw) continue;

            var mi = MakeMeshBox(
                new Vector3(cx * CS, wallMidY, cy * CS + CS * 0.5f),
                new Vector3(WallT, WallH, CS - 0.04f), CWall, alpha);
            _world.AddChild(mi);
            _edgeWalls.Add(mi);
        }
    }

    // ── Border cell hints (for Exit/Start placement) ───────────────────────────
    void RebuildBorderHints()
    {
        foreach (var mi in _borderHints) { if (IsInstanceValid(mi)) mi.QueueFree(); }
        _borderHints.Clear();

        PieceType? activeType = _holding ? _heldType : _selType;
        if (activeType != PieceType.Exit && activeType != PieceType.Start) return;

        int validRow = activeType == PieceType.Start ? StartRow : ExitRow;
        float y = _floor * EdFH + FlrT + 0.05f;
        var mat = new StandardMaterial3D
        {
            AlbedoColor  = new Color(CSel.R, CSel.G, CSel.B, 0.18f),
            ShadingMode  = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };

        for (int x = 0; x < GW; x++)
        {
            // Skip the cell that currently holds this piece (it's being moved)
            if (_cellGeo.ContainsKey(new CK(x, validRow, _floor))) continue;
            var mi = new MeshInstance3D
            {
                Mesh             = new BoxMesh { Size = new Vector3(CS - 0.2f, 0.06f, CS - 0.2f) },
                MaterialOverride = mat,
                Position         = new Vector3(x * CS + CS * 0.5f, y, validRow * CS + CS * 0.5f),
            };
            _world.AddChild(mi);
            _borderHints.Add(mi);
        }
    }

    // ── Placement validation ───────────────────────────────────────────────────
    bool IsValidPlacement(int cx, int cy, PieceType type, int rot)
    {
        // Start stays in its row; Exit stays in its row
        if (type == PieceType.Start)
        {
            if (cy != StartRow) { SetStatus("Start must stay in the bottom row."); return false; }
        }
        else if (type == PieceType.Exit)
        {
            if (cy != ExitRow)  { SetStatus("Exit must stay in the top row."); return false; }
        }

        if (PieceDB.IsStair(type))
            return IsValidStairPlacement(cx, cy, type, rot);

        bool hasConnection = false;

        foreach (Dir dir in new[] { Dir.N, Dir.S, Dir.E, Dir.W })
        {
            var (dx, dz) = DirToOffset(dir);
            int nx = cx + dx, ny = cy + dz;
            if (nx < 0 || nx >= GW || ny < 0 || ny >= GH) continue;

            if (!TryGetPlacedPieceOnFloor(nx, ny, _floor, out var nbr)) continue;

            Dir  oppDir     = PieceDB.Opposite(dir);
            bool myHasOpen  = PieceDB.HasSameFloorOpening(type, rot, dir);
            bool nbrHasOpen = PieceDB.HasSameFloorOpening(nbr.Type, nbr.Rotation, oppDir);

            if (myHasOpen != nbrHasOpen)
            {
                SetStatus($"Opening mismatch with {PieceDB.Labels[nbr.Type]} at ({nx},{ny}).");
                return false;
            }
            if (myHasOpen) hasConnection = true;
        }

        // Stair landings count as connections too
        if (!hasConnection && HasStairLandingNeighbor(cx, cy, _floor, type, rot))
            hasConnection = true;

        bool freeMove   = type == PieceType.Start || type == PieceType.Exit;
        int  floorCount = _maze.Pieces.Count(p => p.Floor == _floor);
        if (!freeMove && floorCount > 0 && !hasConnection)
        {
            SetStatus("Piece must connect to an existing piece.");
            return false;
        }

        return true;
    }

    // Stairs must have their flat-face opening connect to a same-floor neighbor,
    // or their cross-face land on an existing piece on the destination floor that
    // points back toward us.
    bool IsValidStairPlacement(int cx, int cy, PieceType type, int rot)
    {
        Dir flatDir  = PieceDB.GetStairFlatDir(type, rot);
        Dir crossDir = PieceDB.GetStairCrossDir(type, rot);
        int destFloor = _floor + PieceDB.StairFloorDelta(type);

        // Flat side opening mismatch check (same floor)
        foreach (Dir dir in new[] { Dir.N, Dir.S, Dir.E, Dir.W })
        {
            if (dir == crossDir) continue;   // cross face only matters on the other floor
            var (dx, dz) = DirToOffset(dir);
            int nx = cx + dx, ny = cy + dz;
            if (nx < 0 || nx >= GW || ny < 0 || ny >= GH) continue;
            if (!TryGetPlacedPieceOnFloor(nx, ny, _floor, out var nbr)) continue;

            Dir  oppDir     = PieceDB.Opposite(dir);
            bool myHasOpen  = PieceDB.HasSameFloorOpening(type, rot, dir);
            bool nbrHasOpen = PieceDB.HasSameFloorOpening(nbr.Type, nbr.Rotation, oppDir);
            if (myHasOpen != nbrHasOpen)
            {
                SetStatus($"Stair opening mismatch with {PieceDB.Labels[nbr.Type]} at ({nx},{ny}).");
                return false;
            }
        }

        // Connection requirement: stair must have at least one valid connection,
        // either on its flat side or at its cross-floor landing cell.
        bool flatConnected = false;
        {
            var (fdx, fdy) = DirToOffset(flatDir);
            int nx = cx + fdx, ny = cy + fdy;
            if (nx >= 0 && nx < GW && ny >= 0 && ny < GH &&
                TryGetPlacedPieceOnFloor(nx, ny, _floor, out var nbr))
            {
                Dir opp = PieceDB.Opposite(flatDir);
                if (PieceDB.HasSameFloorOpening(nbr.Type, nbr.Rotation, opp))
                    flatConnected = true;
            }
        }

        bool crossConnected = false;
        {
            var (cdx, cdy) = DirToOffset(crossDir);
            int ex = cx + cdx, ey = cy + cdy;
            if (ex >= 0 && ex < GW && ey >= 0 && ey < GH &&
                TryGetPlacedPieceOnFloor(ex, ey, destFloor, out var landing))
            {
                Dir opp = PieceDB.Opposite(crossDir);
                if (PieceDB.HasSameFloorOpening(landing.Type, landing.Rotation, opp))
                    crossConnected = true;
            }
        }

        int floorCount = _maze.Pieces.Count(p => p.Floor == _floor);
        if (floorCount > 0 && !flatConnected && !crossConnected)
        {
            SetStatus("Stair must connect to a neighbor opening (flat side) or a piece on the destination floor.");
            return false;
        }

        return true;
    }

    // Treat a stair landing on this floor as a connection from the perspective of `type`/`rot` at (cx,cy).
    bool HasStairLandingNeighbor(int cx, int cy, int floor, PieceType type, int rot)
    {
        Dir myOpen = PieceDB.GetOpenings(type, rot);
        foreach (Dir dir in new[] { Dir.N, Dir.S, Dir.E, Dir.W })
        {
            if ((myOpen & dir) == 0) continue;
            var (dx, dz) = DirToOffset(dir);
            int nx = cx + dx, ny = cy + dz;
            // Look for a stair on adjacent floor whose cross-exit cell lands at (nx,ny,floor)
            foreach (var s in _maze.Pieces)
            {
                if (!PieceDB.IsStair(s.Type)) continue;
                int sf = s.Floor + PieceDB.StairFloorDelta(s.Type);
                if (sf != floor) continue;
                Dir scross = PieceDB.GetStairCrossDir(s.Type, s.Rotation);
                var (sdx, sdy) = DirToOffset(scross);
                if (s.X + sdx == nx && s.Y + sdy == ny) return true;
            }
        }
        return false;
    }

    // For non-stair pieces: pick the rotation that matches the most neighbor openings,
    // including same-floor neighbors AND adjacent-floor stair landings.
    int InferRotation(PieceType type, int x, int y, int floor, int fallback)
    {
        Dir required = Dir.None;
        Dir[] all = { Dir.N, Dir.E, Dir.S, Dir.W };

        for (int i = 0; i < 4; i++)
        {
            Dir incoming = all[i];           // direction from us toward neighbor
            Dir faceBack = PieceDB.Opposite(incoming);
            var (dx, dz) = DirToOffset(incoming);
            int nx = x + dx, ny = y + dz;

            if (nx >= 0 && nx < GW && ny >= 0 && ny < GH &&
                TryGetPlacedPieceOnFloor(nx, ny, floor, out var nb))
            {
                if (PieceDB.IsStair(nb.Type))
                {
                    Dir flat = PieceDB.GetStairFlatDir(nb.Type, nb.Rotation);
                    if (faceBack == flat) required |= incoming;
                }
                else if ((PieceDB.GetOpenings(nb.Type, nb.Rotation) & faceBack) != 0)
                {
                    required |= incoming;
                }
            }

            // Cross-floor stair whose exit lands at (x,y,floor) from direction `incoming`
            foreach (var s in _maze.Pieces)
            {
                if (!PieceDB.IsStair(s.Type)) continue;
                if (s.Floor + PieceDB.StairFloorDelta(s.Type) != floor) continue;
                Dir scross = PieceDB.GetStairCrossDir(s.Type, s.Rotation);
                var (cdx, cdy) = DirToOffset(scross);
                if (s.X + cdx != x || s.Y + cdy != y) continue;
                // Stair sits at (x - cdx, y - cdy). For its landing to reach us from `incoming`,
                // the negated cross-offset must equal the offset toward the neighbor.
                if (dx == -cdx && dz == -cdy) required |= incoming;
            }
        }

        if (required == Dir.None) return fallback;

        int bestRot   = fallback;
        int bestScore = -1;
        for (int r = 0; r < 4; r++)
        {
            Dir rOpen = PieceDB.GetOpenings(type, r);
            int score = PopCount((int)(rOpen & required));
            if (score > bestScore) { bestScore = score; bestRot = r; }
        }
        return bestRot;
    }

    // For stair pieces: pick the rotation that maximises connections at the flat
    // (same-floor) side and at the cross-floor landing cell.
    int InferStairRotation(PieceType type, int x, int y, int floor, int fallback)
    {
        int destFloor = floor + PieceDB.StairFloorDelta(type);

        int bestRot   = fallback;
        int bestScore = -1;

        for (int r = 0; r < 4; r++)
        {
            int score = 0;

            // Flat side connection (same floor)
            Dir flatDir = PieceDB.GetStairFlatDir(type, r);
            var (fdx, fdy) = DirToOffset(flatDir);
            int nx = x + fdx, ny = y + fdy;
            if (nx >= 0 && nx < GW && ny >= 0 && ny < GH &&
                TryGetPlacedPieceOnFloor(nx, ny, floor, out var nbr))
            {
                Dir opp = PieceDB.Opposite(flatDir);
                if (PieceDB.HasSameFloorOpening(nbr.Type, nbr.Rotation, opp))
                    score += 2;   // weight same-floor neighbor a bit higher
            }

            // Cross-floor landing connection
            Dir crossDir = PieceDB.GetStairCrossDir(type, r);
            var (cdx, cdy) = DirToOffset(crossDir);
            int ex = x + cdx, ey = y + cdy;
            if (ex >= 0 && ex < GW && ey >= 0 && ey < GH &&
                TryGetPlacedPieceOnFloor(ex, ey, destFloor, out var landing))
            {
                Dir opp = PieceDB.Opposite(crossDir);
                if (PieceDB.HasSameFloorOpening(landing.Type, landing.Rotation, opp))
                    score += 1;
            }

            if (score > bestScore) { bestScore = score; bestRot = r; }
        }

        return bestRot;
    }

    static int PopCount(int n)
    {
        int c = 0;
        while (n != 0) { c += n & 1; n >>= 1; }
        return c;
    }

    bool TryGetPlacedPiece(int cx, int cy, out MazePiece piece)
        => TryGetPlacedPieceOnFloor(cx, cy, _floor, out piece);

    bool TryGetPlacedPieceOnFloor(int cx, int cy, int floor, out MazePiece piece)
    {
        int idx = _maze.Pieces.FindIndex(p => p.X == cx && p.Y == cy && p.Floor == floor);
        if (idx >= 0) { piece = _maze.Pieces[idx]; return true; }
        piece = default!;
        return false;
    }

    static (int dx, int dz) DirToOffset(Dir dir) => dir switch
    {
        Dir.N => (0, -1),
        Dir.S => (0,  1),
        Dir.E => (1,  0),
        _     => (-1, 0),
    };
}
