using Godot;
using System.Collections.Generic;
using System.Linq;

/// res://scripts/MapEditorMain.cs — Piece-based maze editor with 5 save slots.
public partial class MapEditorMain : Node2D
{
    // ── Grid layout (pixels) ──────────────────────────────────────────────────
    const int CellPx   = 64;
    const int GridW    = 10;
    const int GridH    = 10;
    const int GridOffX = 280;
    const int GridOffY = 40;
    const int RightX   = GridOffX + GridW * CellPx;  // = 920

    // ── Floor system: floor 0 = Start floor, ±3 around it (7 total) ──────────
    const int FloorMin  = -3;   // 3 floors below start
    const int FloorMax  =  3;   // 3 floors above start
    const int StartFloor = 0;   // Start piece must be on this floor

    // ── Colors ────────────────────────────────────────────────────────────────
    static readonly Color CBackground = new(0.05f, 0.05f, 0.05f);
    static readonly Color CGridLine   = new(0.18f, 0.18f, 0.18f);
    static readonly Color CGridBorder = new(0.45f, 0.45f, 0.45f);
    static readonly Color CWall       = new(0.10f, 0.08f, 0.08f);
    static readonly Color CInvalid    = new(0.90f, 0.15f, 0.15f);
    static readonly Color CGoldText   = new(0.90f, 0.75f, 0.20f);
    static readonly Color CSelFill    = new(1.00f, 1.00f, 0.00f, 0.18f);
    static readonly Color CSelBorder  = new(1.00f, 0.90f, 0.10f, 1.00f);
    static readonly Color CMoveDst    = new(0.60f, 0.60f, 1.00f, 0.20f);

    // ── Piece type list (palette order) ──────────────────────────────────────
    static readonly PieceType[] PieceTypes =
    {
        PieceType.Start, PieceType.Exit,
        PieceType.Straight, PieceType.LHall,
        PieceType.THall, PieceType.StairsUp, PieceType.StairsDown,
    };

    // Direction helpers (parallel arrays)
    static readonly Dir[]      AllDirs   = { Dir.N, Dir.E, Dir.S, Dir.W };
    static readonly (int,int)[] DirDelta = { (0,-1),(1,0),(0,1),(-1,0) };
    static readonly Dir[]      Opposite  = { Dir.S, Dir.W, Dir.N, Dir.E };

    // ── Editor state ──────────────────────────────────────────────────────────
    MazeData  _maze     = new();
    int       _slot     = 0;
    int       _floor    = 0;
    PieceType _selType  = PieceType.Straight;
    int       _rotation = 0;        // cursor rotation (for new pieces)
    MazePiece? _picked  = null;     // currently selected placed piece

    // ── UI refs ───────────────────────────────────────────────────────────────
    Font        _font      = null!;
    Button[]    _slotBtns  = new Button[5];
    Button[]    _typeBtns  = null!;   // sized to PieceTypes.Length at runtime
    Label       _rotLbl    = null!;
    Button      _rotBtn    = null!;
    Label       _floorLbl  = null!;
    Label       _goldLbl   = null!;
    Label       _statusLbl = null!;
    OptionButton _arenaSlotA   = null!;
    OptionButton _arenaSlotB   = null!;
    OptionButton _arenaSpawn   = null!;

    // ══════════════════════════════════════════════════════════════════════════
    //  LIFECYCLE
    // ══════════════════════════════════════════════════════════════════════════
    public override void _Ready()
    {
        _font     = new SystemFont();
        _typeBtns = new Button[PieceTypes.Length];
        var saved = MazeSerializer.Load(_slot);
        if (saved != null) _maze = saved;
        BuildUI();
        UpdateRotateButtonState();
        RefreshGold();
        UpdateStatusLine();
        UpdateAllSlotLabels();
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  UI BUILD
    // ══════════════════════════════════════════════════════════════════════════
    void BuildUI()
    {
        var canvas = new CanvasLayer();
        AddChild(canvas);
        BuildLeftPanel(canvas);
        BuildRightPanel(canvas);
    }

    void BuildLeftPanel(CanvasLayer canvas)
    {
        var panel = MakePanel(new Color(0.07f, 0.07f, 0.10f, 0.97f));
        panel.SetAnchor(Side.Left, 0f);  panel.SetAnchor(Side.Right, 0f);
        panel.SetAnchor(Side.Top, 0f);   panel.SetAnchor(Side.Bottom, 1f);
        panel.SetOffset(Side.Left, 0);   panel.SetOffset(Side.Right, GridOffX - 8);
        panel.SetOffset(Side.Top, 0);    panel.SetOffset(Side.Bottom, 0);
        canvas.AddChild(panel);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 6);
        panel.AddChild(vbox);

        var title = new Label { Text = "PALISADE", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 20);
        title.AddThemeColorOverride("font_color", CGoldText);
        vbox.AddChild(title);

        vbox.AddChild(new HSeparator());

        var hdr = new Label { Text = "SAVE SLOTS", HorizontalAlignment = HorizontalAlignment.Center };
        hdr.AddThemeColorOverride("font_color", new Color(0.65f, 0.65f, 0.65f));
        vbox.AddChild(hdr);

        for (int i = 0; i < 5; i++)
        {
            int idx = i;
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 3);
            vbox.AddChild(row);

            var loadBtn = new Button
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                CustomMinimumSize   = new Vector2(0, 36),
            };
            StyleSlotBtn(loadBtn, idx == _slot);
            loadBtn.Pressed += () => OnLoadSlot(idx);
            row.AddChild(loadBtn);
            _slotBtns[i] = loadBtn;

            var saveBtn = new Button { Text = "Save", CustomMinimumSize = new Vector2(46, 36) };
            saveBtn.Pressed += () => OnSaveSlot(idx);
            row.AddChild(saveBtn);
        }

        vbox.AddChild(new HSeparator());

        _goldLbl = new Label { Text = "Gold spent: 0g", HorizontalAlignment = HorizontalAlignment.Center };
        _goldLbl.AddThemeColorOverride("font_color", CGoldText);
        vbox.AddChild(_goldLbl);

        _statusLbl = new Label
        {
            Text             = "",
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode     = TextServer.AutowrapMode.Word,
        };
        _statusLbl.AddThemeColorOverride("font_color", CInvalid);
        vbox.AddChild(_statusLbl);

        var spacer = new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        vbox.AddChild(spacer);

        vbox.AddChild(new HSeparator());

        var enterBtn = new Button { Text = "ENTER DUNGEON", CustomMinimumSize = new Vector2(0, 44) };
        enterBtn.AddThemeFontSizeOverride("font_size", 14);
        StyleColorBtn(enterBtn, new Color(0.15f, 0.55f, 0.15f));
        enterBtn.Pressed += OnEnterDungeon;
        vbox.AddChild(enterBtn);

        vbox.AddChild(new HSeparator());

        var arenaHdr = new Label { Text = "ARENA MODE", HorizontalAlignment = HorizontalAlignment.Center };
        arenaHdr.AddThemeFontSizeOverride("font_size", 12);
        arenaHdr.AddThemeColorOverride("font_color", new Color(0.85f, 0.65f, 0.20f));
        vbox.AddChild(arenaHdr);

        var rowA = new HBoxContainer();
        rowA.AddThemeConstantOverride("separation", 4);
        vbox.AddChild(rowA);
        var lblA = new Label { Text = "Maze A:", CustomMinimumSize = new Vector2(48, 0) };
        lblA.AddThemeColorOverride("font_color", new Color(0.75f, 0.75f, 0.75f));
        rowA.AddChild(lblA);
        _arenaSlotA = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        for (int i = 0; i < 5; i++) _arenaSlotA.AddItem($"Slot {i}", i);
        _arenaSlotA.Selected = 0;
        rowA.AddChild(_arenaSlotA);

        var rowB = new HBoxContainer();
        rowB.AddThemeConstantOverride("separation", 4);
        vbox.AddChild(rowB);
        var lblB = new Label { Text = "Maze B:", CustomMinimumSize = new Vector2(48, 0) };
        lblB.AddThemeColorOverride("font_color", new Color(0.75f, 0.75f, 0.75f));
        rowB.AddChild(lblB);
        _arenaSlotB = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        for (int i = 0; i < 5; i++) _arenaSlotB.AddItem($"Slot {i}", i);
        _arenaSlotB.Selected = 1;
        rowB.AddChild(_arenaSlotB);

        var spawnRow = new HBoxContainer();
        spawnRow.AddThemeConstantOverride("separation", 4);
        vbox.AddChild(spawnRow);
        var spawnLbl = new Label { Text = "Spawn:", CustomMinimumSize = new Vector2(48, 0) };
        spawnLbl.AddThemeColorOverride("font_color", new Color(0.75f, 0.75f, 0.75f));
        spawnRow.AddChild(spawnLbl);
        _arenaSpawn = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _arenaSpawn.AddItem("Maze A",  0);
        _arenaSpawn.AddItem("Maze B",  1);
        _arenaSpawn.AddItem("Arena",   2);
        _arenaSpawn.Selected = 0;
        spawnRow.AddChild(_arenaSpawn);

        var arenaBtn = new Button { Text = "PLAY ARENA", CustomMinimumSize = new Vector2(0, 40) };
        arenaBtn.AddThemeFontSizeOverride("font_size", 13);
        StyleColorBtn(arenaBtn, new Color(0.45f, 0.25f, 0.05f));
        arenaBtn.Pressed += OnPlayArena;
        vbox.AddChild(arenaBtn);
    }

    void BuildRightPanel(CanvasLayer canvas)
    {
        int panelW = 1280 - RightX;
        var panel = MakePanel(new Color(0.07f, 0.07f, 0.10f, 0.97f));
        panel.SetAnchor(Side.Left, 1f);  panel.SetAnchor(Side.Right, 1f);
        panel.SetAnchor(Side.Top, 0f);   panel.SetAnchor(Side.Bottom, 1f);
        panel.SetOffset(Side.Left, -panelW + 8); panel.SetOffset(Side.Right, 0);
        panel.SetOffset(Side.Top, 0);    panel.SetOffset(Side.Bottom, 0);
        canvas.AddChild(panel);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 5);
        panel.AddChild(vbox);

        var pHdr = new Label { Text = "PIECES", HorizontalAlignment = HorizontalAlignment.Center };
        pHdr.AddThemeFontSizeOverride("font_size", 15);
        pHdr.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f));
        vbox.AddChild(pHdr);

        for (int i = 0; i < PieceTypes.Length; i++)
        {
            int idx  = i;
            var pt   = PieceTypes[i];
            int cost = PieceDB.GoldCosts[pt];
            string label = PieceDB.Labels[pt] + (cost > 0 ? $"  ({cost}g)" : "");
            var btn = new Button { Text = label, CustomMinimumSize = new Vector2(0, 42) };
            StylePieceBtn(btn, pt, pt == _selType);
            btn.Pressed += () => OnSelectPieceType(idx);
            vbox.AddChild(btn);
            _typeBtns[i] = btn;
        }

        vbox.AddChild(new HSeparator());

        var rotHdr = new Label { Text = "ROTATION", HorizontalAlignment = HorizontalAlignment.Center };
        rotHdr.AddThemeColorOverride("font_color", new Color(0.65f, 0.65f, 0.65f));
        vbox.AddChild(rotHdr);

        _rotLbl = new Label { Text = "0°", HorizontalAlignment = HorizontalAlignment.Center };
        _rotLbl.AddThemeFontSizeOverride("font_size", 22);
        _rotLbl.AddThemeColorOverride("font_color", Colors.White);
        vbox.AddChild(_rotLbl);

        _rotBtn = new Button { Text = "Rotate  [R]", CustomMinimumSize = new Vector2(0, 36) };
        _rotBtn.Pressed += OnRotate;
        vbox.AddChild(_rotBtn);

        vbox.AddChild(new HSeparator());

        var floorHdr = new Label { Text = "FLOOR", HorizontalAlignment = HorizontalAlignment.Center };
        floorHdr.AddThemeColorOverride("font_color", new Color(0.65f, 0.65f, 0.65f));
        vbox.AddChild(floorHdr);

        var floorUp = new Button { Text = "Floor Up", CustomMinimumSize = new Vector2(0, 32) };
        floorUp.Pressed += OnFloorUp;
        vbox.AddChild(floorUp);

        _floorLbl = new Label { Text = FloorLabel(0), HorizontalAlignment = HorizontalAlignment.Center };
        _floorLbl.AddThemeColorOverride("font_color", Colors.White);
        vbox.AddChild(_floorLbl);

        var floorDn = new Button { Text = "Floor Down", CustomMinimumSize = new Vector2(0, 32) };
        floorDn.Pressed += OnFloorDown;
        vbox.AddChild(floorDn);

        vbox.AddChild(new HSeparator());

        var hint = new Label
        {
            Text = "Click piece: select\nClick empty: move\nR: rotate selected\nEsc: deselect\nDel: delete selected",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        hint.AddThemeColorOverride("font_color", new Color(0.40f, 0.40f, 0.40f));
        vbox.AddChild(hint);
    }

    // ── Style helpers ─────────────────────────────────────────────────────────
    static PanelContainer MakePanel(Color bg)
    {
        var panel = new PanelContainer();
        var style = new StyleBoxFlat { BgColor = bg };
        style.SetCornerRadiusAll(4);
        panel.AddThemeStyleboxOverride("panel", style);
        return panel;
    }

    static void StyleSlotBtn(Button btn, bool selected)
    {
        var s = new StyleBoxFlat
        {
            BgColor     = selected ? new Color(0.20f, 0.20f, 0.35f) : new Color(0.12f, 0.12f, 0.18f),
            BorderColor = selected ? new Color(0.55f, 0.55f, 0.95f) : new Color(0.28f, 0.28f, 0.38f),
        };
        s.SetBorderWidthAll(selected ? 2 : 1);
        s.SetCornerRadiusAll(3);
        btn.AddThemeStyleboxOverride("normal",  s);
        btn.AddThemeStyleboxOverride("hover",   s);
        btn.AddThemeStyleboxOverride("pressed", s);
        btn.AddThemeColorOverride("font_color", Colors.White);
    }

    static void StylePieceBtn(Button btn, PieceType pt, bool selected)
    {
        var c = PieceDB.Colors[pt];
        var s = new StyleBoxFlat
        {
            BgColor     = selected ? c : c.Darkened(0.45f),
            BorderColor = selected ? Colors.Yellow : c.Lightened(0.20f),
        };
        s.SetBorderWidthAll(selected ? 3 : 1);
        s.SetCornerRadiusAll(3);
        btn.AddThemeStyleboxOverride("normal",  s);
        btn.AddThemeStyleboxOverride("hover",   s);
        btn.AddThemeStyleboxOverride("pressed", s);
        btn.AddThemeColorOverride("font_color", Colors.White);
    }

    static void StyleColorBtn(Button btn, Color color)
    {
        var s = new StyleBoxFlat { BgColor = color, BorderColor = color.Lightened(0.3f) };
        s.SetBorderWidthAll(2);
        s.SetCornerRadiusAll(4);
        btn.AddThemeStyleboxOverride("normal",  s);
        btn.AddThemeStyleboxOverride("hover",   s);
        btn.AddThemeStyleboxOverride("pressed", s);
        btn.AddThemeColorOverride("font_color", Colors.White);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  DRAW
    // ══════════════════════════════════════════════════════════════════════════
    public override void _Draw()
    {
        DrawRect(new Rect2(0, 0, 1280, 720), CBackground);
        DrawGrid();
        DrawPieces();
        DrawMoveCursor();
        DrawFloorHint();
    }

    void DrawGrid()
    {
        DrawRect(new Rect2(GridOffX, GridOffY, GridW * CellPx, GridH * CellPx),
            new Color(0.08f, 0.07f, 0.07f));

        // ── Row zone highlights ────────────────────────────────────────────────
        // START ZONE: floor 0 only (Start piece can only go on floor 0, row 0)
        if (_floor == 0)
        {
            DrawRect(new Rect2(GridOffX, GridOffY, GridW * CellPx, CellPx),
                new Color(0.10f, 0.40f, 0.10f, 0.18f));
            DrawString(_font, new Vector2(GridOffX + 2, GridOffY + CellPx - 6),
                "START ZONE", HorizontalAlignment.Left, GridW * CellPx, 10,
                new Color(0.35f, 0.90f, 0.35f, 0.70f));
        }
        // EXIT ZONE: last row on every floor — Exit can be placed on any floor
        // as long as it is the row farthest from the Start row.
        DrawRect(new Rect2(GridOffX, GridOffY + (GridH - 1) * CellPx, GridW * CellPx, CellPx),
            new Color(0.50f, 0.30f, 0.05f, 0.22f));
        DrawString(_font, new Vector2(GridOffX + 2, GridOffY + GridH * CellPx - 6),
            "EXIT ZONE  (any floor)", HorizontalAlignment.Left, GridW * CellPx, 10,
            new Color(0.95f, 0.70f, 0.15f, 0.70f));

        for (int x = 1; x < GridW; x++)
            DrawLine(new Vector2(GridOffX + x * CellPx, GridOffY),
                     new Vector2(GridOffX + x * CellPx, GridOffY + GridH * CellPx),
                     CGridLine, 1f);
        for (int y = 1; y < GridH; y++)
            DrawLine(new Vector2(GridOffX, GridOffY + y * CellPx),
                     new Vector2(GridOffX + GridW * CellPx, GridOffY + y * CellPx),
                     CGridLine, 1f);
        DrawRect(new Rect2(GridOffX, GridOffY, GridW * CellPx, GridH * CellPx),
            CGridBorder, filled: false, width: 2f);
    }

    void DrawPieces()
    {
        var lookup = BuildLookup();
        // Ghosts first so real pieces draw on top
        DrawStairGhosts(lookup);
        foreach (var piece in _maze.Pieces)
            if (piece.Floor == _floor) DrawPiece(piece, lookup);
    }

    // Stair ghosts show where stairs from an adjacent floor project onto the current floor,
    // so the user knows what cell needs a connecting piece.
    //
    //  StairsUp / Stairs on floor F-1  → ghost on current floor (their high exit emerges here)
    //  StairsDown on floor F+1         → ghost on current floor (their low exit descends here)
    void DrawStairGhosts(Dictionary<(int, int, int), MazePiece> lookup)
    {
        // ── StairsUp ghosts from floor below ─────────────────────────────────
        if (_floor > FloorMin)
        {
            int belowFloor = _floor - 1;
            foreach (var stair in _maze.Pieces)
            {
                if (!PieceDB.IsStair(stair.Type) || stair.Type == PieceType.StairsDown) continue;
                if (stair.Floor != belowFloor) continue;

                DrawStairUpGhost(stair, belowFloor);
            }
        }

        // ── StairsDown ghosts from floor above ───────────────────────────────
        {
            int aboveFloor = _floor + 1;
            foreach (var stair in _maze.Pieces)
            {
                if (stair.Type != PieceType.StairsDown) continue;
                if (stair.Floor != aboveFloor) continue;

                DrawStairDownGhost(stair, aboveFloor);
            }
        }
    }

    void DrawStairUpGhost(MazePiece stair, int belowFloor)
    {
        float px    = GridOffX + stair.X * CellPx;
        float py    = GridOffY + stair.Y * CellPx;
        Color base_ = PieceDB.Colors[stair.Type];
        Color ghost = new Color(base_.R * 0.7f, base_.G * 0.7f, base_.B * 0.7f, 0.30f);

        Dir ghostOpen = PieceDB.GetOpenings(stair.Type, stair.Rotation);
        float gcx = px + CellPx * 0.5f, gcy = py + CellPx * 0.5f;
        float ghw = CellPx * 0.30f;
        DrawRect(new Rect2(px + 1, py + 1, CellPx - 2, CellPx - 2),
            new Color(base_.R * 0.3f, base_.G * 0.3f, base_.B * 0.3f, 0.28f));
        if ((ghostOpen & Dir.N) != 0) DrawRect(new Rect2(gcx-ghw, py,      ghw*2, CellPx*0.5f+ghw), ghost);
        if ((ghostOpen & Dir.S) != 0) DrawRect(new Rect2(gcx-ghw, gcy-ghw, ghw*2, CellPx*0.5f+ghw), ghost);
        if ((ghostOpen & Dir.E) != 0) DrawRect(new Rect2(gcx-ghw, gcy-ghw, CellPx*0.5f+ghw, ghw*2), ghost);
        if ((ghostOpen & Dir.W) != 0) DrawRect(new Rect2(px,      gcy-ghw, CellPx*0.5f+ghw, ghw*2), ghost);
        DrawRect(new Rect2(px, py, CellPx, CellPx),
            new Color(base_.R, base_.G, base_.B, 0.45f), filled: false, width: 1.5f);

        // Exit face toward current floor
        Dir upDir = PieceDB.GetStairCrossDir(stair.Type, stair.Rotation);
        int di = System.Array.IndexOf(AllDirs, upDir);
        var (dx, dy) = DirDelta[di];
        Dir opp = Opposite[di];

        bool connected = _maze.Pieces.Any(p =>
            p.Floor == _floor &&
            p.X == stair.X + dx && p.Y == stair.Y + dy &&
            (PieceDB.GetOpenings(p.Type, p.Rotation) & opp) != 0);

        DrawOpeningMarker(px, py, upDir,
            connected ? new Color(0.2f, 0.9f, 0.2f, 0.8f) : new Color(0.9f, 0.15f, 0.15f, 0.9f));

        DrawString(_font, new Vector2(px + CellPx * 0.5f, py + CellPx * 0.5f + 5),
            "UP", HorizontalAlignment.Center, CellPx - 4, 11, new Color(1f, 1f, 1f, 0.55f));
        DrawString(_font, new Vector2(px + CellPx * 0.5f, py + CellPx - 8),
            $"F{belowFloor}", HorizontalAlignment.Center, CellPx, 9,
            new Color(CGoldText.R, CGoldText.G, CGoldText.B, 0.6f));
        if (!connected)
            DrawString(_font, new Vector2(px + CellPx * 0.5f, py + 10),
                "!", HorizontalAlignment.Center, CellPx, 14, new Color(0.9f, 0.15f, 0.15f, 0.9f));
    }

    void DrawStairDownGhost(MazePiece stair, int aboveFloor)
    {
        // StairsDown is at aboveFloor. Its S face connects down to _floor at (stair.X, stair.Y+1).
        // Show ghost at the stair's own cell; highlight the S face connection.
        float px    = GridOffX + stair.X * CellPx;
        float py    = GridOffY + stair.Y * CellPx;
        Color base_ = PieceDB.Colors[PieceType.StairsDown];
        Color ghost = new Color(base_.R * 0.7f, base_.G * 0.7f, base_.B * 0.7f, 0.30f);

        Dir ghostOpen = PieceDB.GetOpenings(PieceType.StairsDown, 0);
        float gcx = px + CellPx * 0.5f, gcy = py + CellPx * 0.5f;
        float ghw = CellPx * 0.30f;
        DrawRect(new Rect2(px + 1, py + 1, CellPx - 2, CellPx - 2),
            new Color(base_.R * 0.3f, base_.G * 0.3f, base_.B * 0.3f, 0.28f));
        if ((ghostOpen & Dir.N) != 0) DrawRect(new Rect2(gcx-ghw, py,      ghw*2, CellPx*0.5f+ghw), ghost);
        if ((ghostOpen & Dir.S) != 0) DrawRect(new Rect2(gcx-ghw, gcy-ghw, ghw*2, CellPx*0.5f+ghw), ghost);
        DrawRect(new Rect2(px, py, CellPx, CellPx),
            new Color(base_.R, base_.G, base_.B, 0.45f), filled: false, width: 1.5f);

        // S face is the cross-floor exit; check if (stair.X, stair.Y+1, _floor) connects back
        int diS = System.Array.IndexOf(AllDirs, Dir.S);
        var (dxS, dyS) = DirDelta[diS];
        bool connected = _maze.Pieces.Any(p =>
            p.Floor == _floor &&
            p.X == stair.X + dxS && p.Y == stair.Y + dyS &&
            (PieceDB.GetOpenings(p.Type, p.Rotation) & Dir.N) != 0);

        DrawOpeningMarker(px, py, Dir.S,
            connected ? new Color(0.2f, 0.9f, 0.2f, 0.8f) : new Color(0.9f, 0.15f, 0.15f, 0.9f));

        DrawString(_font, new Vector2(px + CellPx * 0.5f, py + CellPx * 0.5f + 5),
            "DN", HorizontalAlignment.Center, CellPx - 4, 11, new Color(1f, 1f, 1f, 0.55f));
        DrawString(_font, new Vector2(px + CellPx * 0.5f, py + CellPx - 8),
            $"F{aboveFloor}", HorizontalAlignment.Center, CellPx, 9,
            new Color(CGoldText.R, CGoldText.G, CGoldText.B, 0.6f));
        if (!connected)
            DrawString(_font, new Vector2(px + CellPx * 0.5f, py + 10),
                "!", HorizontalAlignment.Center, CellPx, 14, new Color(0.9f, 0.15f, 0.15f, 0.9f));
    }

    // Draw a coloured band on one face of a cell to show an opening direction.
    void DrawOpeningMarker(float px, float py, Dir dir, Color col)
    {
        float w   = 6f;
        float gapPx  = CellPx * 0.5f;
        float gapOff = (CellPx - gapPx) / 2f;
        switch (dir)
        {
            case Dir.N: DrawRect(new Rect2(px + gapOff, py,              gapPx, w),      col); break;
            case Dir.S: DrawRect(new Rect2(px + gapOff, py + CellPx - w, gapPx, w),      col); break;
            case Dir.E: DrawRect(new Rect2(px + CellPx - w, py + gapOff, w, gapPx),      col); break;
            case Dir.W: DrawRect(new Rect2(px,           py + gapOff,    w, gapPx),      col); break;
        }
    }

    // While a piece is selected, hover the mouse over an empty cell to preview the destination
    void DrawMoveCursor()
    {
        if (_picked == null) return;
        var pos = GetGlobalMousePosition();
        int cx = (int)((pos.X - GridOffX) / CellPx);
        int cy = (int)((pos.Y - GridOffY) / CellPx);
        if (cx < 0 || cx >= GridW || cy < 0 || cy >= GridH) return;
        bool occupied = _maze.Pieces.Any(p => p.X == cx && p.Y == cy && p.Floor == _floor && p != _picked);
        if (!occupied)
            DrawRect(new Rect2(GridOffX + cx * CellPx + 1, GridOffY + cy * CellPx + 1, CellPx - 2, CellPx - 2),
                CMoveDst);
    }

    void DrawPiece(MazePiece piece, Dictionary<(int, int, int), MazePiece> lookup)
    {
        float px  = GridOffX + piece.X * CellPx;
        float py  = GridOffY + piece.Y * CellPx;
        Color col = PieceDB.Colors[piece.Type];
        Dir   open = PieceDB.GetOpenings(piece.Type, piece.Rotation);

        float cx = px + CellPx * 0.5f;
        float cy = py + CellPx * 0.5f;
        // hw: corridor half-width — slightly wider than a visual "slot" but shows shape clearly
        float hw = CellPx * 0.30f;

        // ── Wall fill (background stone) ──────────────────────────────────────
        DrawRect(new Rect2(px + 1, py + 1, CellPx - 2, CellPx - 2), col.Darkened(0.68f));

        // ── Corridor area — union of open-direction strips ────────────────────
        Color floorCol = col.Darkened(0.20f);
        // Each strip extends from the cell edge to just past the centre, so strips
        // overlap at the centre and their union forms the corridor silhouette.
        if ((open & Dir.N) != 0)
            DrawRect(new Rect2(cx - hw, py,      hw * 2, CellPx * 0.5f + hw), floorCol);
        if ((open & Dir.S) != 0)
            DrawRect(new Rect2(cx - hw, cy - hw, hw * 2, CellPx * 0.5f + hw), floorCol);
        if ((open & Dir.E) != 0)
            DrawRect(new Rect2(cx - hw, cy - hw, CellPx * 0.5f + hw, hw * 2), floorCol);
        if ((open & Dir.W) != 0)
            DrawRect(new Rect2(px,      cy - hw, CellPx * 0.5f + hw, hw * 2), floorCol);

        // ── Opening edge accents (connection-state indicator) ─────────────────
        const float ew = 3f;
        foreach (Dir dir in AllDirs)
        {
            if ((open & dir) == 0) continue;
            int  di      = System.Array.IndexOf(AllDirs, dir);
            var (dx, dy) = DirDelta[di];
            Dir  opp     = Opposite[di];

            // Determine which floor this opening connects to (cross-floor for stair types)
            int nFloor = piece.Floor;
            if (PieceDB.IsStair(piece.Type) && dir == PieceDB.GetStairCrossDir(piece.Type, piece.Rotation))
                nFloor = piece.Floor + PieceDB.StairFloorDelta(piece.Type);
            bool nbExists   = lookup.ContainsKey((piece.X + dx, piece.Y + dy, nFloor));
            bool nbConnects = nbExists &&
                (PieceDB.GetOpenings(lookup[(piece.X + dx, piece.Y + dy, nFloor)].Type,
                                     lookup[(piece.X + dx, piece.Y + dy, nFloor)].Rotation)
                 & opp) != 0;

            Color accent = (nbExists && !nbConnects) ? CInvalid : col.Lightened(0.35f);
            switch (dir)
            {
                case Dir.N: DrawRect(new Rect2(cx - hw, py,              hw * 2, ew), accent); break;
                case Dir.S: DrawRect(new Rect2(cx - hw, py + CellPx - ew, hw * 2, ew), accent); break;
                case Dir.E: DrawRect(new Rect2(px + CellPx - ew, cy - hw, ew, hw * 2), accent); break;
                case Dir.W: DrawRect(new Rect2(px,               cy - hw, ew, hw * 2), accent); break;
            }
        }

        // ── Stairs ramp arrow ─────────────────────────────────────────────────
        if (PieceDB.IsStair(piece.Type))
        {
            // For StairsUp/Stairs: arrow points toward the "up" (high) exit.
            // For StairsDown: arrow points toward the "down" (low S) exit.
            Dir arrowDir = piece.Type == PieceType.StairsDown
                ? Dir.S
                : PieceDB.GetStairUpDir(piece.Rotation);
            Color arrow = CGoldText.Lightened(0.1f);
            Dir   upDir = arrowDir;   // keep same variable name for the arrow-head code below
            (float ax, float ay, float bx, float by) = upDir switch
            {
                Dir.N => (cx, cy + hw * 0.8f,  cx, cy - hw * 0.8f),  // low → high going north
                Dir.S => (cx, cy - hw * 0.8f,  cx, cy + hw * 0.8f),
                Dir.E => (cx - hw * 0.8f, cy,  cx + hw * 0.8f, cy),
                Dir.W => (cx + hw * 0.8f, cy,  cx - hw * 0.8f, cy),
                _     => (cx - hw, cy, cx + hw, cy),
            };
            DrawLine(new Vector2(ax, ay), new Vector2(bx, by), arrow, 2f);
            // Arrowhead at high end
            float aLen = 5f;
            Vector2 tip = new(bx, by);
            Vector2 dir2 = (tip - new Vector2(ax, ay)).Normalized();
            Vector2 perp = new(-dir2.Y, dir2.X);
            DrawLine(tip, tip - dir2 * aLen + perp * aLen * 0.5f, arrow, 2f);
            DrawLine(tip, tip - dir2 * aLen - perp * aLen * 0.5f, arrow, 2f);

            int targetFloor = piece.Floor + PieceDB.StairFloorDelta(piece.Type);
            DrawString(_font, new Vector2(cx, py + CellPx - 7),
                "F" + targetFloor, HorizontalAlignment.Center, CellPx, 9, CGoldText);
        }

        // ── Label (small, faint, inside corridor area) ────────────────────────
        DrawString(_font, new Vector2(cx, cy + 4),
            PieceDB.ShortLabels[piece.Type],
            HorizontalAlignment.Center, CellPx - 4, 11,
            new Color(1f, 1f, 1f, 0.45f));

        // ── Selection highlight ───────────────────────────────────────────────
        if (piece == _picked)
        {
            DrawRect(new Rect2(px, py, CellPx, CellPx), CSelFill);
            DrawRect(new Rect2(px, py, CellPx, CellPx), CSelBorder, filled: false, width: 3f);
        }
    }

    void DrawFloorHint()
    {
        int count = _maze.Pieces.Count(p => p.Floor == _floor);
        DrawString(_font,
            new Vector2(GridOffX + GridW * CellPx * 0.5f, GridOffY - 16),
            $"Floor {_floor}  ({count} piece{(count == 1 ? "" : "s")})",
            HorizontalAlignment.Center, GridW * CellPx, 13, new Color(0.7f, 0.7f, 0.7f));
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  INPUT
    // ══════════════════════════════════════════════════════════════════════════
    public override void _UnhandledInput(InputEvent ev)
    {
        if (ev is InputEventKey key && key.Pressed && !key.Echo)
        {
            switch (key.PhysicalKeycode)
            {
                case Key.R:      OnRotate(); return;
                case Key.Escape: Deselect(); return;
                case Key.Delete: DeleteSelected(); return;
            }
        }

        if (ev is InputEventMouseMotion && _picked != null)
        {
            QueueRedraw();  // keep move-cursor preview fresh
            return;
        }

        if (ev is InputEventMouseButton mb && mb.Pressed)
        {
            var pos = GetGlobalMousePosition();
            int cx = (int)((pos.X - GridOffX) / CellPx);
            int cy = (int)((pos.Y - GridOffY) / CellPx);
            if (cx < 0 || cx >= GridW || cy < 0 || cy >= GridH) return;

            if (mb.ButtonIndex == MouseButton.Left)
                HandleLeftClick(cx, cy);
            else if (mb.ButtonIndex == MouseButton.Right)
                HandleRightClick(cx, cy);
        }
    }

    void HandleLeftClick(int cx, int cy)
    {
        var hit = _maze.Pieces.FirstOrDefault(p => p.X == cx && p.Y == cy && p.Floor == _floor);

        if (_picked != null)
        {
            if (hit == null)
            {
                // Move selected piece to this empty cell; auto-rotate to connect.
                // Stair types are fixed-orientation — keep rotation 0.
                _picked.X        = cx;
                _picked.Y        = cy;
                _picked.Rotation = (_picked.Type == PieceType.StairsUp || _picked.Type == PieceType.StairsDown)
                    ? 0
                    : InferRotation(_picked.Type, cx, cy, _floor, exclude: _picked);
                _picked = null;
                RefreshGold();
                UpdateStatusLine();
            }
            else if (hit == _picked)
            {
                Deselect();   // tap same piece again → deselect
            }
            else
            {
                _picked = hit;  // select a different piece
            }
        }
        else
        {
            if (hit != null)
            {
                _picked = hit;                // select existing piece
                SyncRotLabelToSelected();
            }
            else
            {
                PlaceNew(cx, cy);             // place a new piece
            }
        }
        QueueRedraw();
    }

    void HandleRightClick(int cx, int cy)
    {
        if (_picked != null) { Deselect(); return; }   // right-click deselects
        RemovePiece(cx, cy);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  PLACE / REMOVE
    // ══════════════════════════════════════════════════════════════════════════
    void PlaceNew(int x, int y)
    {
        if (_selType == PieceType.Start && _maze.Pieces.Any(p => p.Type == PieceType.Start))
        { _statusLbl.Text = "Only one Start allowed"; return; }
        if (_selType == PieceType.Exit && _maze.Pieces.Any(p => p.Type == PieceType.Exit))
        { _statusLbl.Text = "Only one Exit allowed"; return; }

        // ── Row / floor constraints ───────────────────────────────────────────
        if (_selType == PieceType.Start && (y != 0 || _floor != 0))
        { _statusLbl.Text = "Start must be in the top row (row 0, floor 0)"; return; }
        if (_selType == PieceType.Exit && y != GridH - 1)
        { _statusLbl.Text = "Exit must be in the last row (row 9, any floor)"; return; }
        if (_selType == PieceType.StairsDown && _floor <= FloorMin)
        { _statusLbl.Text = $"Stairs Down needs a floor below — use floor {FloorMin + 1}+"; return; }

        // Block placement on stair ghost cells (upward stairs from the floor below)
        if (_maze.Pieces.Any(p =>
                PieceDB.IsStair(p.Type) && p.Type != PieceType.StairsDown &&
                p.Floor == _floor - 1 && p.X == x && p.Y == y))
        { _statusLbl.Text = "Stairs from below occupy this cell"; return; }

        // StairsUp/StairsDown are always rotation-0 (no rotation needed)
        int rot = PieceDB.IsStair(_selType) ? 0 : InferRotation(_selType, x, y, _floor, exclude: null);

        _maze.Pieces.RemoveAll(p => p.X == x && p.Y == y && p.Floor == _floor);
        _maze.Pieces.Add(new MazePiece { Type = _selType, X = x, Y = y, Floor = _floor, Rotation = rot });
        RefreshGold();
        UpdateStatusLine();
        QueueRedraw();
    }

    void RemovePiece(int x, int y)
    {
        _maze.Pieces.RemoveAll(p => p.X == x && p.Y == y && p.Floor == _floor);
        RefreshGold();
        UpdateStatusLine();
        QueueRedraw();
    }

    void Deselect()
    {
        _picked = null;
        SyncRotLabelToSelected();
        QueueRedraw();
    }

    void DeleteSelected()
    {
        if (_picked == null) return;
        _maze.Pieces.Remove(_picked);
        _picked = null;
        RefreshGold();
        UpdateStatusLine();
        QueueRedraw();
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  AUTO-ROTATION INFERENCE
    // ══════════════════════════════════════════════════════════════════════════
    /// <summary>
    /// Returns the rotation (0-3) that connects to the most neighbors that have
    /// openings pointing toward (x, y).  Falls back to _rotation if no neighbors
    /// have any relevant openings.
    /// </summary>
    int InferRotation(PieceType type, int x, int y, int floor, MazePiece? exclude)
    {
        // Collect which directions REQUIRE an opening (neighbor has opening toward us)
        Dir required = Dir.None;
        for (int i = 0; i < 4; i++)
        {
            var (dx, dy) = DirDelta[i];
            int nf = (type == PieceType.Stairs && AllDirs[i] == Dir.N) ? floor + 1 : floor;
            if (!_maze.Pieces.Any(p => p != exclude && p.X == x+dx && p.Y == y+dy && p.Floor == nf))
                continue;
            var nb = _maze.Pieces.First(p => p != exclude && p.X == x+dx && p.Y == y+dy && p.Floor == nf);
            if ((PieceDB.GetOpenings(nb.Type, nb.Rotation) & Opposite[i]) != 0)
                required |= AllDirs[i];
        }

        if (required == Dir.None) return _rotation;  // no neighbors, keep manual rotation

        int bestRot   = _rotation;
        int bestScore = -1;
        for (int r = 0; r < 4; r++)
        {
            Dir rOpen = PieceDB.GetOpenings(type, r);
            int score = PopCount((int)(rOpen & required));
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

    // ══════════════════════════════════════════════════════════════════════════
    //  PANEL ACTIONS
    // ══════════════════════════════════════════════════════════════════════════
    void OnSelectPieceType(int idx)
    {
        _selType = PieceTypes[idx];
        _picked  = null;   // switching type clears any selection
        for (int i = 0; i < _typeBtns.Length; i++)
            StylePieceBtn(_typeBtns[i], PieceTypes[i], i == idx);
        UpdateRotateButtonState();
        SyncRotLabelToSelected();
        QueueRedraw();
    }

    void UpdateRotateButtonState()
    {
        // Disable rotate when the active type (palette or selected piece) is a fixed-rotation stair
        PieceType active = _picked != null ? _picked.Type : _selType;
        bool noRot = active == PieceType.StairsUp || active == PieceType.StairsDown;
        _rotBtn.Disabled = noRot;
        _rotBtn.Modulate  = noRot ? new Color(1f, 1f, 1f, 0.35f) : new Color(1f, 1f, 1f, 1f);
    }

    void OnRotate()
    {
        // Stair types can't be rotated
        if (_picked != null && PieceDB.IsStair(_picked.Type) &&
            (_picked.Type == PieceType.StairsUp || _picked.Type == PieceType.StairsDown))
            return;
        if (_picked == null && (_selType == PieceType.StairsUp || _selType == PieceType.StairsDown))
            return;

        if (_picked != null)
        {
            _picked.Rotation = (_picked.Rotation + 1) % 4;
            SyncRotLabelToSelected();
        }
        else
        {
            _rotation = (_rotation + 1) % 4;
            _rotLbl.Text = $"{_rotation * 90}°";
        }
        QueueRedraw();
    }

    string FloorLabel(int f) => f == StartFloor ? $"Floor {f}  (start)" : $"Floor {f}";

    void OnFloorUp()
    {
        if (_floor >= FloorMax) return;
        Deselect();
        _floor++;
        _floorLbl.Text = FloorLabel(_floor);
        QueueRedraw();
    }

    void OnFloorDown()
    {
        if (_floor <= FloorMin) return;
        Deselect();
        _floor--;
        _floorLbl.Text = FloorLabel(_floor);
        QueueRedraw();
    }

    void OnLoadSlot(int slot)
    {
        // Auto-save any unsaved changes to the current slot before switching
        MazeSerializer.Save(_slot, _maze);

        _picked = null;
        var data = MazeSerializer.Load(slot);
        _maze  = data ?? new MazeData();
        _slot  = slot;
        _floor = 0;
        _floorLbl.Text = FloorLabel(0);
        UpdateAllSlotLabels();
        RefreshGold();
        UpdateStatusLine();
        QueueRedraw();
    }

    void OnSaveSlot(int slot)
    {
        _slot = slot;
        MazeSerializer.Save(slot, _maze);
        UpdateAllSlotLabels();
        GD.Print($"[MapEditor] Saved slot {slot}: {_maze.Pieces.Count} pieces, {_maze.GoldSpent}g");
    }

    void OnEnterDungeon()
    {
        bool hasStart = _maze.Pieces.Any(p => p.Type == PieceType.Start);
        bool hasExit  = _maze.Pieces.Any(p => p.Type == PieceType.Exit);
        if (!hasStart || !hasExit)
        { _statusLbl.Text = "Need a Start and Exit piece!"; return; }

        // Validate all stair cross-floor connections
        foreach (var stair in _maze.Pieces.Where(p => PieceDB.IsStair(p.Type)))
        {
            Dir  crossDir  = PieceDB.GetStairCrossDir(stair.Type, stair.Rotation);
            int  di        = System.Array.IndexOf(AllDirs, crossDir);
            var (dx, dy)   = DirDelta[di];
            int  delta     = PieceDB.StairFloorDelta(stair.Type);
            int  crossFloor = stair.Floor + delta;

            if (crossFloor < FloorMin || crossFloor > FloorMax)
            {
                _statusLbl.Text = $"Stairs at ({stair.X},{stair.Y}) F{stair.Floor} goes out of range!";
                QueueRedraw();
                return;
            }

            bool connected = _maze.Pieces.Any(p =>
                p.Floor == crossFloor &&
                p.X == stair.X + dx && p.Y == stair.Y + dy &&
                (PieceDB.GetOpenings(p.Type, p.Rotation) & Opposite[di]) != 0);
            if (!connected)
            {
                string label = stair.Type == PieceType.StairsDown ? "below" : "above";
                _statusLbl.Text = $"Stairs at ({stair.X},{stair.Y}) F{stair.Floor} has no exit {label}!";
                _floor = crossFloor;
                _floorLbl.Text = FloorLabel(_floor);
                QueueRedraw();
                return;
            }
        }

        OnSaveSlot(_slot);
        GameState.ActiveSlot = _slot;
        GetTree().ChangeSceneToFile("res://scenes/DungeonGame.tscn");
    }

    void OnPlayArena()
    {
        int a = _arenaSlotA.Selected;
        int b = _arenaSlotB.Selected;
        if (a == b)
        { _statusLbl.Text = "Arena needs two different slots"; return; }
        if (!MazeSerializer.Exists(a))
        { _statusLbl.Text = $"Slot {a} is empty"; return; }
        if (!MazeSerializer.Exists(b))
        { _statusLbl.Text = $"Slot {b} is empty"; return; }

        GameState.IsArenaMode = true;
        GameState.ArenaSlotA  = a;
        GameState.ArenaSlotB  = b;
        DungeonArena.ChosenSpawn = _arenaSpawn.Selected switch
        {
            1 => DungeonArena.SpawnPoint.MazeB,
            2 => DungeonArena.SpawnPoint.Arena,
            _ => DungeonArena.SpawnPoint.MazeA,
        };
        GetTree().ChangeSceneToFile("res://scenes/DungeonArena.tscn");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  HELPERS
    // ══════════════════════════════════════════════════════════════════════════
    Dictionary<(int, int, int), MazePiece> BuildLookup()
    {
        var d = new Dictionary<(int, int, int), MazePiece>();
        foreach (var p in _maze.Pieces) d[(p.X, p.Y, p.Floor)] = p;
        return d;
    }

    void SyncRotLabelToSelected()
    {
        // StairsUp/StairsDown have no rotation
        bool fixedRot = (_picked != null && (_picked.Type == PieceType.StairsUp || _picked.Type == PieceType.StairsDown))
                     || (_picked == null  && (_selType    == PieceType.StairsUp || _selType    == PieceType.StairsDown));
        int deg = _picked != null ? _picked.Rotation * 90 : _rotation * 90;
        _rotLbl.Text = fixedRot ? "–" : $"{deg}°";
        UpdateRotateButtonState();
        if (_picked != null)
        {
            _statusLbl.Text = $"Selected: {PieceDB.Labels[_picked.Type]}";
            _statusLbl.AddThemeColorOverride("font_color", PieceDB.Colors[_picked.Type].Lightened(0.2f));
        }
        else
        {
            _statusLbl.AddThemeColorOverride("font_color", CInvalid);
            UpdateStatusLine();
        }
    }

    void RefreshGold()
    {
        int g = _maze.Pieces.Sum(p => PieceDB.GoldCosts[p.Type]);
        _maze.GoldSpent = g;
        _goldLbl.Text   = $"Gold spent: {g}g";
    }

    void UpdateStatusLine()
    {
        bool hasStart = _maze.Pieces.Any(p => p.Type == PieceType.Start);
        bool hasExit  = _maze.Pieces.Any(p => p.Type == PieceType.Exit);
        if (!hasStart && !hasExit)      { _statusLbl.Text = "Place a Start and Exit"; return; }
        if (!hasStart)                  { _statusLbl.Text = "Missing Start piece"; return; }
        if (!hasExit)                   { _statusLbl.Text = "Missing Exit piece"; return; }

        // Check stair cross-floor connections
        foreach (var stair in _maze.Pieces.Where(p => PieceDB.IsStair(p.Type)))
        {
            Dir  crossDir   = PieceDB.GetStairCrossDir(stair.Type, stair.Rotation);
            int  di         = System.Array.IndexOf(AllDirs, crossDir);
            var (dx, dy)    = DirDelta[di];
            int  crossFloor = stair.Floor + PieceDB.StairFloorDelta(stair.Type);
            bool ok = crossFloor >= FloorMin && _maze.Pieces.Any(p =>
                p.Floor == crossFloor &&
                p.X == stair.X + dx && p.Y == stair.Y + dy &&
                (PieceDB.GetOpenings(p.Type, p.Rotation) & Opposite[di]) != 0);
            if (!ok)
            {
                string dir2 = stair.Type == PieceType.StairsDown ? "below" : "above";
                _statusLbl.Text = $"Stairs at ({stair.X},{stair.Y}) needs exit {dir2}";
                return;
            }
        }

        _statusLbl.Text = "";
    }

    void UpdateAllSlotLabels()
    {
        for (int i = 0; i < 5; i++)
        {
            var data = MazeSerializer.Load(i);
            _slotBtns[i].Text = data != null
                ? $"Slot {i}: {data.Name}  {data.GoldSpent}g"
                : $"Slot {i}: (empty)";
            StyleSlotBtn(_slotBtns[i], i == _slot);
        }
    }
}
