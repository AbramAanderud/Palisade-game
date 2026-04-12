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
    Font     _font      = null!;
    Button[] _slotBtns  = new Button[5];
    Button[] _typeBtns  = new Button[PieceTypes.Length];
    Label    _rotLbl    = null!;
    Label    _floorLbl  = null!;
    Label    _goldLbl   = null!;
    Label    _statusLbl = null!;

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
            Color ghost = new Color(base_.R * 0.7f, base_.G * 0.7f, base_.B * 0.7f, 0.35f);

            // Ghost fill
            DrawRect(new Rect2(px + 2, py + 2, CellPx - 4, CellPx - 4), ghost);

            // Dashed border to distinguish from real pieces
            DrawRect(new Rect2(px, py, CellPx, CellPx),
                new Color(base_.R, base_.G, base_.B, 0.55f), filled: false, width: 2f);

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

        DrawRect(new Rect2(px + 2, py + 2, CellPx - 4, CellPx - 4), col.Darkened(0.30f));

        float wallThick = 7f;
        float gapPx     = CellPx * 0.36f;
        float gapOff    = (CellPx - gapPx) / 2f;

        DrawFace(px, py, Dir.N, open, lookup, piece, wallThick, gapOff, gapPx, col);
        DrawFace(px, py, Dir.E, open, lookup, piece, wallThick, gapOff, gapPx, col);
        DrawFace(px, py, Dir.S, open, lookup, piece, wallThick, gapOff, gapPx, col);
        DrawFace(px, py, Dir.W, open, lookup, piece, wallThick, gapOff, gapPx, col);

        DrawString(_font, new Vector2(px + CellPx * 0.5f, py + CellPx * 0.5f + 5),
            PieceDB.ShortLabels[piece.Type], HorizontalAlignment.Center, CellPx - 4, 14, Colors.White);

        if (piece.Type == PieceType.Stairs)
            DrawString(_font, new Vector2(px + CellPx * 0.5f, py + CellPx - 8),
                "F" + (piece.Floor + 1), HorizontalAlignment.Center, CellPx, 9, CGoldText);

        // Selection highlight
        if (piece == _picked)
        {
            DrawRect(new Rect2(px, py, CellPx, CellPx), CSelFill);
            DrawRect(new Rect2(px, py, CellPx, CellPx), CSelBorder, filled: false, width: 3f);
        }
    }

    void DrawFace(float px, float py, Dir dir, Dir openings,
        Dictionary<(int, int, int), MazePiece> lookup,
        MazePiece piece, float w, float gapOff, float gapPx, Color pieceColor)
    {
        bool isOpen = (openings & dir) != 0;
        int  di     = System.Array.IndexOf(AllDirs, dir);
        var (dx, dy) = DirDelta[di];
        Dir  opp    = Opposite[di];

        int nFloor = (piece.Type == PieceType.Stairs && dir == Dir.N)
            ? piece.Floor + 1 : piece.Floor;
        bool nbExists   = lookup.ContainsKey((piece.X + dx, piece.Y + dy, nFloor));
        bool nbConnects = nbExists &&
            (PieceDB.GetOpenings(lookup[(piece.X + dx, piece.Y + dy, nFloor)].Type,
                                 lookup[(piece.X + dx, piece.Y + dy, nFloor)].Rotation) & opp) != 0;

        bool connError = isOpen && nbExists && !nbConnects;
        Color openCol  = connError ? CInvalid : pieceColor.Lightened(0.25f);

        if (isOpen)
        {
            switch (dir)
            {
                case Dir.N:
                    DrawRect(new Rect2(px,                   py, gapOff,             w        ), CWall);
                    DrawRect(new Rect2(px + gapOff,          py, gapPx,              w * 0.5f ), openCol);
                    DrawRect(new Rect2(px + gapOff + gapPx,  py, CellPx-gapOff-gapPx, w      ), CWall);
                    break;
                case Dir.S:
                    DrawRect(new Rect2(px,                   py+CellPx-w, gapOff,             w        ), CWall);
                    DrawRect(new Rect2(px + gapOff,          py+CellPx-w*0.5f, gapPx,         w * 0.5f ), openCol);
                    DrawRect(new Rect2(px + gapOff + gapPx,  py+CellPx-w, CellPx-gapOff-gapPx, w      ), CWall);
                    break;
                case Dir.E:
                    DrawRect(new Rect2(px+CellPx-w, py,                  w,        gapOff             ), CWall);
                    DrawRect(new Rect2(px+CellPx-w*0.5f, py+gapOff,      w * 0.5f, gapPx             ), openCol);
                    DrawRect(new Rect2(px+CellPx-w, py+gapOff+gapPx,     w,        CellPx-gapOff-gapPx), CWall);
                    break;
                case Dir.W:
                    DrawRect(new Rect2(px, py,                w,        gapOff             ), CWall);
                    DrawRect(new Rect2(px, py+gapOff,         w * 0.5f, gapPx             ), openCol);
                    DrawRect(new Rect2(px, py+gapOff+gapPx,   w,        CellPx-gapOff-gapPx), CWall);
                    break;
            }
        }
        else
        {
            switch (dir)
            {
                case Dir.N: DrawRect(new Rect2(px, py,              CellPx, w     ), CWall); break;
                case Dir.S: DrawRect(new Rect2(px, py+CellPx-w,     CellPx, w     ), CWall); break;
                case Dir.E: DrawRect(new Rect2(px+CellPx-w, py,     w,      CellPx), CWall); break;
                case Dir.W: DrawRect(new Rect2(px, py,              w,      CellPx), CWall); break;
            }
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
        OnSaveSlot(_slot);
        GD.Print("[MapEditor] DungeonGame scene not yet implemented.");
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
        if (!hasStart && !hasExit)      _statusLbl.Text = "Place a Start and Exit";
        else if (!hasStart)             _statusLbl.Text = "Missing Start piece";
        else if (!hasExit)              _statusLbl.Text = "Missing Exit piece";
        else                            _statusLbl.Text = "";
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
