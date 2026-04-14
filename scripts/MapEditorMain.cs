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
        PieceType.THall, PieceType.Stairs,
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
    Button[]    _typeBtns  = new Button[PieceTypes.Length];
    Label       _rotLbl    = null!;
    Label       _floorLbl  = null!;
    Label       _goldLbl   = null!;
    Label       _statusLbl = null!;
    OptionButton _arenaSlotA = null!;
    OptionButton _arenaSlotB = null!;

    // ══════════════════════════════════════════════════════════════════════════
    //  LIFECYCLE
    // ══════════════════════════════════════════════════════════════════════════
    public override void _Ready()
    {
        _font = new SystemFont();
        var saved = MazeSerializer.Load(_slot);
        if (saved != null) _maze = saved;
        BuildUI();
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

        var rotBtn = new Button { Text = "Rotate  [R]", CustomMinimumSize = new Vector2(0, 36) };
        rotBtn.Pressed += OnRotate;
        vbox.AddChild(rotBtn);

        vbox.AddChild(new HSeparator());

        var floorHdr = new Label { Text = "FLOOR", HorizontalAlignment = HorizontalAlignment.Center };
        floorHdr.AddThemeColorOverride("font_color", new Color(0.65f, 0.65f, 0.65f));
        vbox.AddChild(floorHdr);

        var floorUp = new Button { Text = "Floor Up", CustomMinimumSize = new Vector2(0, 32) };
        floorUp.Pressed += OnFloorUp;
        vbox.AddChild(floorUp);

        _floorLbl = new Label { Text = "Floor 0", HorizontalAlignment = HorizontalAlignment.Center };
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

        // ── Row zone highlights (only on floor 0) ─────────────────────────────
        if (_floor == 0)
        {
            // Row 0 = Start zone (green tint)
            DrawRect(new Rect2(GridOffX, GridOffY, GridW * CellPx, CellPx),
                new Color(0.10f, 0.40f, 0.10f, 0.18f));
            DrawString(_font, new Vector2(GridOffX + 2, GridOffY + CellPx - 6),
                "START ZONE", HorizontalAlignment.Left, GridW * CellPx, 10,
                new Color(0.35f, 0.90f, 0.35f, 0.70f));

            // Last row = Exit zone (amber tint)
            DrawRect(new Rect2(GridOffX, GridOffY + (GridH - 1) * CellPx, GridW * CellPx, CellPx),
                new Color(0.50f, 0.30f, 0.05f, 0.22f));
            DrawString(_font, new Vector2(GridOffX + 2, GridOffY + GridH * CellPx - 6),
                "EXIT ZONE", HorizontalAlignment.Left, GridW * CellPx, 10,
                new Color(0.95f, 0.70f, 0.15f, 0.70f));
        }

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

    // Stair pieces on floor F-1 are shown as translucent ghosts on the current floor
    // so the player can see where stairs emerge and what they need to connect to.
    void DrawStairGhosts(Dictionary<(int, int, int), MazePiece> lookup)
    {
        if (_floor == 0) return;
        int belowFloor = _floor - 1;

        foreach (var stair in _maze.Pieces)
        {
            if (stair.Type != PieceType.Stairs || stair.Floor != belowFloor) continue;

            float px    = GridOffX + stair.X * CellPx;
            float py    = GridOffY + stair.Y * CellPx;
            Color base_ = PieceDB.Colors[PieceType.Stairs];
            Color ghost = new Color(base_.R * 0.7f, base_.G * 0.7f, base_.B * 0.7f, 0.30f);

            // Ghost: show corridor shape for the stair openings (both N and S at rot=0)
            Dir ghostOpen = PieceDB.GetOpenings(PieceType.Stairs, stair.Rotation);
            float gcx = px + CellPx * 0.5f, gcy = py + CellPx * 0.5f;
            float ghw = CellPx * 0.30f;
            DrawRect(new Rect2(px + 1, py + 1, CellPx - 2, CellPx - 2),
                new Color(base_.R * 0.3f, base_.G * 0.3f, base_.B * 0.3f, 0.28f));
            if ((ghostOpen & Dir.N) != 0) DrawRect(new Rect2(gcx-ghw, py,      ghw*2, CellPx*0.5f+ghw), ghost);
            if ((ghostOpen & Dir.S) != 0) DrawRect(new Rect2(gcx-ghw, gcy-ghw, ghw*2, CellPx*0.5f+ghw), ghost);
            if ((ghostOpen & Dir.E) != 0) DrawRect(new Rect2(gcx-ghw, gcy-ghw, CellPx*0.5f+ghw, ghw*2), ghost);
            if ((ghostOpen & Dir.W) != 0) DrawRect(new Rect2(px,      gcy-ghw, CellPx*0.5f+ghw, ghw*2), ghost);

            // Dashed border to distinguish from real pieces
            DrawRect(new Rect2(px, py, CellPx, CellPx),
                new Color(base_.R, base_.G, base_.B, 0.45f), filled: false, width: 1.5f);

            // The exit opening on this floor (upDir of the stair)
            Dir upDir = PieceDB.GetStairUpDir(stair.Rotation);
            int di = System.Array.IndexOf(AllDirs, upDir);
            var (dx, dy) = DirDelta[di];
            Dir opp = Opposite[di];

            bool connected = _maze.Pieces.Any(p =>
                p.Floor == _floor &&
                p.X == stair.X + dx && p.Y == stair.Y + dy &&
                (PieceDB.GetOpenings(p.Type, p.Rotation) & opp) != 0);

            // Draw the exit face — green if connected, red if not
            Color faceCol = connected
                ? new Color(0.2f, 0.9f, 0.2f, 0.8f)
                : new Color(0.9f, 0.15f, 0.15f, 0.9f);
            DrawOpeningMarker(px, py, upDir, faceCol);

            // Label
            Color textCol = new Color(1f, 1f, 1f, 0.55f);
            DrawString(_font, new Vector2(px + CellPx * 0.5f, py + CellPx * 0.5f + 5),
                "UP", HorizontalAlignment.Center, CellPx - 4, 11, textCol);
            DrawString(_font, new Vector2(px + CellPx * 0.5f, py + CellPx - 8),
                $"F{belowFloor}", HorizontalAlignment.Center, CellPx, 9,
                new Color(CGoldText.R, CGoldText.G, CGoldText.B, 0.6f));

            if (!connected)
                DrawString(_font, new Vector2(px + CellPx * 0.5f, py + 10),
                    "!", HorizontalAlignment.Center, CellPx, 14,
                    new Color(0.9f, 0.15f, 0.15f, 0.9f));
        }
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

            int  nFloor     = (piece.Type == PieceType.Stairs &&
                               dir == PieceDB.GetStairUpDir(piece.Rotation))
                              ? piece.Floor + 1 : piece.Floor;
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
        if (piece.Type == PieceType.Stairs)
        {
            Dir   upDir = PieceDB.GetStairUpDir(piece.Rotation);
            Color arrow = CGoldText.Lightened(0.1f);
            // Draw a diagonal slash indicating the ramp incline direction
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

            DrawString(_font, new Vector2(cx, py + CellPx - 7),
                "F" + (piece.Floor + 1), HorizontalAlignment.Center, CellPx, 9, CGoldText);
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
                // Move selected piece to this empty cell; auto-rotate to connect
                _picked.X        = cx;
                _picked.Y        = cy;
                _picked.Rotation = InferRotation(_picked.Type, cx, cy, _floor, exclude: _picked);
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

        // ── Row zone constraints ──────────────────────────────────────────────
        if (_selType == PieceType.Start && (y != 0 || _floor != 0))
        { _statusLbl.Text = "Start must be in the top row (row 0, floor 0)"; return; }
        if (_selType == PieceType.Exit && (y != GridH - 1 || _floor != 0))
        { _statusLbl.Text = "Exit must be in the bottom row (floor 0)"; return; }

        // Block placement on stair ghost cells (stairs from the floor below occupy this cell)
        if (_floor > 0 && _maze.Pieces.Any(p =>
                p.Type == PieceType.Stairs && p.Floor == _floor - 1 &&
                p.X == x && p.Y == y))
        { _statusLbl.Text = "Stairs from below occupy this cell"; return; }

        int rot = InferRotation(_selType, x, y, _floor, exclude: null);

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
        SyncRotLabelToSelected();
        QueueRedraw();
    }

    void OnRotate()
    {
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

    void OnFloorUp()
    {
        if (_floor >= 2) return;
        Deselect();
        _floor++;
        _floorLbl.Text = $"Floor {_floor}";
        QueueRedraw();
    }

    void OnFloorDown()
    {
        if (_floor <= 0) return;
        Deselect();
        _floor--;
        _floorLbl.Text = $"Floor {_floor}";
        QueueRedraw();
    }

    void OnLoadSlot(int slot)
    {
        _picked = null;
        var data = MazeSerializer.Load(slot);
        _maze  = data ?? new MazeData();
        _slot  = slot;
        _floor = 0;
        _floorLbl.Text = "Floor 0";
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

        // Validate all stair exits are connected
        foreach (var stair in _maze.Pieces.Where(p => p.Type == PieceType.Stairs))
        {
            Dir upDir = PieceDB.GetStairUpDir(stair.Rotation);
            int di    = System.Array.IndexOf(AllDirs, upDir);
            var (dx, dy) = DirDelta[di];
            bool connected = _maze.Pieces.Any(p =>
                p.Floor == stair.Floor + 1 &&
                p.X == stair.X + dx && p.Y == stair.Y + dy &&
                (PieceDB.GetOpenings(p.Type, p.Rotation) & Opposite[di]) != 0);
            if (!connected)
            {
                _statusLbl.Text = $"Stairs at ({stair.X},{stair.Y}) F{stair.Floor} has no exit above!";
                _floor = stair.Floor + 1;
                _floorLbl.Text = $"Floor {_floor}";
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
        int deg = _picked != null ? _picked.Rotation * 90 : _rotation * 90;
        _rotLbl.Text = $"{deg}°";
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

        // Check stair connections
        foreach (var stair in _maze.Pieces.Where(p => p.Type == PieceType.Stairs))
        {
            Dir upDir = PieceDB.GetStairUpDir(stair.Rotation);
            int di    = System.Array.IndexOf(AllDirs, upDir);
            var (dx, dy) = DirDelta[di];
            bool ok = _maze.Pieces.Any(p =>
                p.Floor == stair.Floor + 1 &&
                p.X == stair.X + dx && p.Y == stair.Y + dy &&
                (PieceDB.GetOpenings(p.Type, p.Rotation) & Opposite[di]) != 0);
            if (!ok) { _statusLbl.Text = $"Stairs at ({stair.X},{stair.Y}) needs exit"; return; }
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
