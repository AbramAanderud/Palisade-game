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

    // Start is on the far-side row (cy = 0, the top of the screen — furthest from the
    // default camera view). Exit is on the near-side row (cy = GH-1, the bottom).
    // At these positions, both pieces' natural rotations (Start opens S, Exit opens N)
    // already point inward into the maze — no rotation needed by default.
    const int StartRow = 0;
    const int ExitRow  = GH - 1;

    // ── Colors ─────────────────────────────────────────────────────────────────
    static readonly Color CFloor   = new(0.82f, 0.82f, 0.82f);
    static readonly Color CWall    = new(1.00f, 1.00f, 1.00f);
    static readonly Color CDimFloor= new(0.28f, 0.28f, 0.28f, 0.45f);
    static readonly Color CDimWall = new(0.50f, 0.50f, 0.50f, 0.16f);
    static readonly Color CStart   = new(0.20f, 0.88f, 0.28f);
    static readonly Color CExit    = new(0.88f, 0.20f, 0.20f);
    static readonly Color CStair       = new(0.88f, 0.62f, 0.12f);   // orange — stair on another floor
    static readonly Color CStairActive = new(0.25f, 0.70f, 1.0f);    // bright blue — stair on the floor you're on
    static readonly Color CSel     = new(1.00f, 0.80f, 0.15f);
    static readonly Color CText    = new(0.85f, 0.85f, 0.85f);
    static readonly Color CDim     = new(0.48f, 0.48f, 0.48f);
    static readonly Color CPan     = new(0f, 0f, 0f);   // bottom palette bar — pure black to match the rest of the editor backdrop

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

    // Pulsing red error indicator on the conflicting piece (opening-mismatch validations)
    MeshInstance3D?     _errorPulse;
    StandardMaterial3D? _errorPulseMat;
    float               _errorPulseTimer = 0f;
    float               _errorPulsePhase = 0f;
    int                 _errorPulseFloor = 0;
    int                 _errorPulseCx    = -1;
    int                 _errorPulseCy    = -1;

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

    // Palette
    readonly Dictionary<PieceType, Panel>       _palPan  = new();
    readonly Dictionary<PieceType, SubViewport> _thumbVp = new();

    // UI refs
    Label    _floorLbl  = null!;
    Label    _statusLbl = null!;
    LineEdit _nameEdit  = null!;
    Button[] _slotBtns  = new Button[MaxSlots];

    // Audio
    AudioStreamPlayer? _clickSfx;

    // ── _Ready ─────────────────────────────────────────────────────────────────
    public override void _Ready()
    {
        _font = GD.Load<FontFile>("res://assets/fonts/Agmena Pro Book.ttf");

        // Reuse the menu click sound for piece pick-up / place / drop feedback
        _clickSfx = new AudioStreamPlayer();
        var clickStream = GD.Load<AudioStream>("res://assets/audio/ui/MenuButtonClick.wav");
        if (clickStream != null) _clickSfx.Stream = clickStream;
        AddChild(_clickSfx);

        BuildUI();
        SetupMainVp();
        SetupBackgroundSmoke();
        SetupThumbs();
        BuildFloorGrids();
        // Restore the slot the user was last editing / playtesting so coming back
        // from "Test Maze" doesn't kick them off whatever they were working on.
        int startSlot = Mathf.Clamp(GameState.ActiveSlot, 0, MaxSlots - 1);
        LoadSlot(startSlot);
    }

    // ── _Process ───────────────────────────────────────────────────────────────
    public override void _Process(double _dt)
    {
        UpdateCamera();
        UpdateErrorPulse((float)_dt);
    }

    // ── Keyboard shortcuts ─────────────────────────────────────────────────────
    public override void _Input(InputEvent ev)
    {
        if (ev is not InputEventKey k || !k.Pressed) return;
        // Don't fire editor shortcuts while the user is typing in a text field
        if (GetViewport().GuiGetFocusOwner() is LineEdit) return;

        switch (k.Keycode)
        {
            case Key.R: RotateOnce(); break;
            case Key.W:
                FloorUp();
                GetViewport().SetInputAsHandled();
                break;
            case Key.S:
                FloorDown();
                GetViewport().SetInputAsHandled();
                break;
            case Key.D:
                // Delete the held piece — Start/Exit return to origin, others are discarded.
                if (_holding)
                {
                    AbandonHolding();
                    GetViewport().SetInputAsHandled();
                }
                break;
            case Key.Escape:
                // Only consume Escape if it actually clears something here — otherwise
                // let PauseMenu open as it does in every other scene.
                if (_holding)
                {
                    CancelHolding();
                    GetViewport().SetInputAsHandled();
                }
                else if (_selType.HasValue)
                {
                    _selType = null;
                    RefreshPalette();
                    SetStatus();
                    RebuildBorderHints();
                    RebuildPreviewGeo();
                    GetViewport().SetInputAsHandled();
                }
                break;
        }
    }

    // ── UI construction ────────────────────────────────────────────────────────
    void BuildUI()
    {
        var bg = new ColorRect { Color = Colors.Black };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        // ── Floating left-side column (no panel background) ─────────────────────
        // The maze editor's side controls used to live in a dark panel; now every
        // button is a translucent borderless overlay, matching the floor up/down
        // buttons on the right. Save and "Exit to main" are pulled out into the
        // bottom corners (built further down).
        var lv = new VBoxContainer();
        lv.AnchorLeft   = 0f; lv.AnchorRight  = 0f;
        lv.AnchorTop    = 0f; lv.AnchorBottom = 1f;
        lv.OffsetLeft   = 18;
        lv.OffsetRight  = 18 + PanW - 36;
        lv.OffsetTop    = 78;   // leave room for the top-left ← BACK button (height 40 + padding)
        lv.OffsetBottom = -100; // leave room for the bottom-left Save button
        lv.AddThemeConstantOverride("separation", 10);
        AddChild(lv);

        var title = Lbl("MAZE EDITOR", 22, Colors.White, HorizontalAlignment.Center);
        title.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.95f));
        title.AddThemeConstantOverride("outline_size", 5);
        lv.AddChild(title);

        // NAME label and the maze-name input share a single row.
        var nameRow = new HBoxContainer();
        nameRow.AddThemeConstantOverride("separation", 10);
        lv.AddChild(nameRow);

        var nameHeader = Lbl("NAME:", 14, CDim);
        nameHeader.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.95f));
        nameHeader.AddThemeConstantOverride("outline_size", 4);
        nameHeader.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        nameRow.AddChild(nameHeader);

        _nameEdit = new LineEdit { PlaceholderText = "Maze name…", CustomMinimumSize = new Vector2(0, 40) };
        if (_font != null) _nameEdit.AddThemeFontOverride("font", _font);
        _nameEdit.AddThemeFontSizeOverride("font_size", 16);
        _nameEdit.AddThemeStyleboxOverride("normal",
            Flat(new Color(0.04f, 0.04f, 0.04f, 0.55f)));
        _nameEdit.AddThemeStyleboxOverride("focus",
            Flat(new Color(0.10f, 0.10f, 0.10f, 0.75f)));
        _nameEdit.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _nameEdit.TextChanged += t => { _maze.Name = t; RefreshSlotButtons(); };
        nameRow.AddChild(_nameEdit);

        var slotsHeader = Lbl("SAVE SLOTS", 14, CDim);
        slotsHeader.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.95f));
        slotsHeader.AddThemeConstantOverride("outline_size", 4);
        lv.AddChild(slotsHeader);

        for (int i = 0; i < MaxSlots; i++)
        {
            int idx = i;
            _slotBtns[i] = MakeFloatBtn($"  {i + 1}", 16, () => { SaveCurrentSlot(); LoadSlot(idx); },
                                        new Vector2(0, 44));
            _slotBtns[i].SizeFlagsHorizontal = SizeFlags.ExpandFill;
            lv.AddChild(_slotBtns[i]);
        }

        var clearBtn = MakeFloatBtn("CLEAR MAP", 16, ConfirmClearMap, new Vector2(0, 48));
        clearBtn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        lv.AddChild(clearBtn);

        // PLAY TEST button moved to a floating slot in the top-right corner — see below.

        // ── Bottom-left: Save ──────────────────────────────────────────────────
        var saveBtn = MakeFloatBtn("SAVE", 20, SaveCurrentSlot, new Vector2(160, 56));
        saveBtn.AnchorLeft     = 0f; saveBtn.AnchorRight  = 0f;
        saveBtn.AnchorTop      = 1f; saveBtn.AnchorBottom = 1f;
        saveBtn.GrowHorizontal = GrowDirection.End;
        saveBtn.GrowVertical   = GrowDirection.Begin;
        saveBtn.OffsetLeft     = 24;
        saveBtn.OffsetRight    = 24 + 160;
        saveBtn.OffsetTop      = -(56 + 24);
        saveBtn.OffsetBottom   = -24;
        AddChild(saveBtn);

        // ── Bottom-right: PLAY TEST ────────────────────────────────────────────
        // (← BACK in the top-left still routes to the main menu, so the previous
        // bottom-right "EXIT TO MAIN" button is gone — this corner is the play-test action now.)
        var playTestBtnBR = MakeFloatBtn("TEST MAZE", 20, OnEnterDungeon, new Vector2(200, 68));
        playTestBtnBR.AnchorLeft     = 1f; playTestBtnBR.AnchorRight  = 1f;
        playTestBtnBR.AnchorTop      = 1f; playTestBtnBR.AnchorBottom = 1f;
        playTestBtnBR.GrowHorizontal = GrowDirection.Begin;
        playTestBtnBR.GrowVertical   = GrowDirection.Begin;
        playTestBtnBR.OffsetLeft     = -(200 + 24);
        playTestBtnBR.OffsetRight    = -24;
        playTestBtnBR.OffsetTop      = -(68 + 24);
        playTestBtnBR.OffsetBottom   = -24;
        playTestBtnBR.AddThemeColorOverride("font_color", new Color(0.85f, 1f, 0.7f));
        AddChild(playTestBtnBR);

        // ── Top-left: ← BACK shortcut to main menu (mirrors EXIT TO MAIN but matches
        // the conventional location of a back affordance) ──────────────────────────
        var topBack = MakeFloatBtn("← BACK", 16,
            () => { SaveCurrentSlot(); GetTree().ChangeSceneToFile("res://scenes/TitleScreen.tscn"); },
            new Vector2(120, 40));
        topBack.AnchorLeft     = 0f; topBack.AnchorRight  = 0f;
        topBack.AnchorTop      = 0f; topBack.AnchorBottom = 0f;
        topBack.OffsetLeft     = 18;
        topBack.OffsetRight    = 18 + 120;
        topBack.OffsetTop      = 18;
        topBack.OffsetBottom   = 18 + 40;
        AddChild(topBack);

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
        bot.OffsetRight  = -(200 + 48);   // shift palette pieces left to clear room for TEST MAZE in the bottom-right corner
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

        // ── Floating "click a palette piece" prompt above the palette bar ─────
        // Only the palette callout lives down here — every other shortcut is in the
        // top-right keybind legend, since this hint physically points to the bar.
        var hintLbl = new Label
        {
            Text                = "Click a palette piece below to select",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        if (_font != null) hintLbl.AddThemeFontOverride("font", _font);
        hintLbl.AddThemeFontSizeOverride("font_size", 14);
        hintLbl.AddThemeColorOverride("font_color",         new Color(0.62f, 0.62f, 0.62f));
        hintLbl.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.95f));
        hintLbl.AddThemeConstantOverride("outline_size", 4);
        hintLbl.AnchorLeft     = 0f; hintLbl.AnchorRight  = 1f;
        hintLbl.AnchorTop      = 1f; hintLbl.AnchorBottom = 1f;
        hintLbl.GrowVertical   = GrowDirection.Begin;
        hintLbl.OffsetLeft     = PanW + 20;
        hintLbl.OffsetRight    = -20;
        hintLbl.OffsetTop      = -(PalBarH + 28);
        hintLbl.OffsetBottom   = -(PalBarH + 4);
        hintLbl.MouseFilter    = MouseFilterEnum.Ignore;
        AddChild(hintLbl);

        // ── Floating status banner at the top of the viewport ────────────────
        _statusLbl = new Label
        {
            Text                = "",
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode        = TextServer.AutowrapMode.Word,
        };
        if (_font != null) _statusLbl.AddThemeFontOverride("font", _font);
        _statusLbl.AddThemeFontSizeOverride("font_size", 26);
        _statusLbl.AddThemeColorOverride("font_color",         Colors.White);
        _statusLbl.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.95f));
        _statusLbl.AddThemeConstantOverride("outline_size", 6);
        _statusLbl.AnchorLeft   = 0f; _statusLbl.AnchorRight  = 1f;
        _statusLbl.AnchorTop    = 0f; _statusLbl.AnchorBottom = 0f;
        _statusLbl.OffsetLeft   = PanW + 20;
        _statusLbl.OffsetRight  = -20;
        _statusLbl.OffsetTop    = 24;
        _statusLbl.OffsetBottom = 96;
        _statusLbl.MouseFilter  = MouseFilterEnum.Ignore;
        AddChild(_statusLbl);

        // ── Top-right keybind legend ─────────────────────────────────────────
        // Single home for every editor control (mouse + keyboard). The bottom strip
        // only carries the "click palette piece" prompt now.
        var keyLegend = new VBoxContainer();
        keyLegend.AnchorLeft     = 1f; keyLegend.AnchorRight  = 1f;
        keyLegend.AnchorTop      = 0f; keyLegend.AnchorBottom = 0f;
        keyLegend.GrowHorizontal = GrowDirection.Begin;
        keyLegend.OffsetLeft     = -230;
        keyLegend.OffsetRight    = -20;
        keyLegend.OffsetTop      = 18;
        keyLegend.AddThemeConstantOverride("separation", 4);
        keyLegend.MouseFilter    = MouseFilterEnum.Ignore;
        AddChild(keyLegend);

        var legendBg = new Panel();
        legendBg.AnchorLeft = 0f; legendBg.AnchorRight = 1f;
        legendBg.AnchorTop  = 0f; legendBg.AnchorBottom = 0f;
        legendBg.OffsetTop = -8; legendBg.OffsetBottom = 4;
        legendBg.OffsetLeft = -8; legendBg.OffsetRight = 8;
        legendBg.AddThemeStyleboxOverride("panel",
            Flat(new Color(0.05f, 0.05f, 0.05f, 0.65f), new Color(0.7f, 0.7f, 0.7f, 0.4f)));
        legendBg.MouseFilter = MouseFilterEnum.Ignore;
        keyLegend.AddChild(legendBg);

        void AddRow(string key, string action)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 10);
            row.MouseFilter = MouseFilterEnum.Ignore;
            keyLegend.AddChild(row);

            var keyLbl = new Label
            {
                Text                = $"[{key}]",
                CustomMinimumSize   = new Vector2(80, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            if (_font != null) keyLbl.AddThemeFontOverride("font", _font);
            keyLbl.AddThemeFontSizeOverride("font_size", 14);
            keyLbl.AddThemeColorOverride("font_color", new Color(1f, 0.92f, 0.55f));
            row.AddChild(keyLbl);

            var actLbl = new Label { Text = action };
            if (_font != null) actLbl.AddThemeFontOverride("font", _font);
            actLbl.AddThemeFontSizeOverride("font_size", 14);
            actLbl.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.85f));
            row.AddChild(actLbl);
        }
        AddRow("L-Click", "pick up / place");
        AddRow("R-Click", "drop / delete");
        AddRow("R",       "rotate");
        AddRow("W",       "floor up");
        AddRow("S",       "floor down");
        AddRow("D",       "delete held");
        AddRow("Esc",     "cancel hold");

        // ── Floating floor up/down controls on the right edge of the viewport ──
        const int FloorBtnSz  = 72;
        const int FloorBtnPad = 24;
        const int FloorLblH   = 30;

        var floorCol = new VBoxContainer();
        floorCol.AnchorLeft     = 1f; floorCol.AnchorRight  = 1f;
        floorCol.AnchorTop      = 0.5f; floorCol.AnchorBottom = 0.5f;
        floorCol.GrowHorizontal = GrowDirection.Begin;
        floorCol.GrowVertical   = GrowDirection.Both;
        floorCol.OffsetLeft     = -(FloorBtnSz + FloorBtnPad);
        floorCol.OffsetRight    = -FloorBtnPad;
        float halfH = (FloorBtnSz + FloorLblH + FloorBtnSz + 16) * 0.5f;
        floorCol.OffsetTop      = -halfH;
        floorCol.OffsetBottom   =  halfH;
        floorCol.AddThemeConstantOverride("separation", 8);
        floorCol.MouseFilter    = MouseFilterEnum.Ignore;   // pass through gaps; children grab clicks
        AddChild(floorCol);

        // Up/down arrow buttons show their keyboard shortcut letters (W / S) at the
        // "end" of each arrow so the binding is discoverable.
        var fUpBig = MakeFloorBtn("▲\nW", FloorBtnSz, FloorUp);
        floorCol.AddChild(fUpBig);

        _floorLbl = Lbl($"Floor {_floor}", 20, Colors.White, HorizontalAlignment.Center);
        _floorLbl.CustomMinimumSize = new Vector2(FloorBtnSz, FloorLblH);
        _floorLbl.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
        _floorLbl.AddThemeConstantOverride("outline_size", 4);
        floorCol.AddChild(_floorLbl);

        var fDnBig = MakeFloorBtn("S\n▼", FloorBtnSz, FloorDown);
        floorCol.AddChild(fDnBig);

        RefreshSlotButtons();
    }

    // Rectangular floating-style button used for the side controls. Translucent dark
    // background, no border, gold hover/pressed accents — matches the floor arrow
    // buttons visually but lets the caller pick the size.
    Button MakeFloatBtn(string text, int fontSize, Action pressed, Vector2 minSize)
    {
        var b = new Button
        {
            Text              = text,
            CustomMinimumSize = minSize,
            Alignment         = HorizontalAlignment.Center,
        };
        if (_font != null) b.AddThemeFontOverride("font", _font);
        b.AddThemeFontSizeOverride("font_size", fontSize);
        b.AddThemeColorOverride("font_color",         Colors.White);
        b.AddThemeColorOverride("font_hover_color",   new Color(1f, 0.92f, 0.55f));
        b.AddThemeColorOverride("font_pressed_color", new Color(0.85f, 0.75f, 0.2f));
        b.AddThemeColorOverride("font_focus_color",   Colors.White);

        var bg      = Flat(new Color(0.04f, 0.04f, 0.04f, 0.55f));
        var bgHover = Flat(new Color(0.12f, 0.12f, 0.12f, 0.78f));
        var bgPress = Flat(new Color(0.20f, 0.16f, 0.04f, 0.85f));
        b.AddThemeStyleboxOverride("normal",  bg);
        b.AddThemeStyleboxOverride("hover",   bgHover);
        b.AddThemeStyleboxOverride("pressed", bgPress);
        b.AddThemeStyleboxOverride("focus",   bg);
        b.AddThemeStyleboxOverride("disabled", bg);
        b.Pressed += pressed;
        return b;
    }

    // Big square floating arrow button used by the floor navigator on the right.
    // Text supports a second line (e.g. the W/S keybind below the arrow glyph).
    Button MakeFloorBtn(string glyph, int sz, Action pressed)
    {
        var b = new Button
        {
            Text              = glyph,
            CustomMinimumSize = new Vector2(sz, sz),
            Alignment         = HorizontalAlignment.Center,
            AutowrapMode      = TextServer.AutowrapMode.Off,
            ClipText          = false,
        };
        if (_font != null) b.AddThemeFontOverride("font", _font);
        b.AddThemeFontSizeOverride("font_size", 24);
        b.AddThemeColorOverride("font_color",         Colors.White);
        b.AddThemeColorOverride("font_hover_color",   new Color(1f, 0.92f, 0.55f));
        b.AddThemeColorOverride("font_pressed_color", new Color(0.85f, 0.75f, 0.2f));

        var bg      = Flat(new Color(0.04f, 0.04f, 0.04f, 0.65f), new Color(0.85f, 0.85f, 0.85f, 0.6f));
        var bgHover = Flat(new Color(0.10f, 0.10f, 0.10f, 0.80f), new Color(1f,    0.92f, 0.55f, 0.9f));
        var bgPress = Flat(new Color(0.18f, 0.15f, 0.04f, 0.85f), new Color(1f,    0.92f, 0.55f, 1f));
        b.AddThemeStyleboxOverride("normal",  bg);
        b.AddThemeStyleboxOverride("hover",   bgHover);
        b.AddThemeStyleboxOverride("pressed", bgPress);
        b.AddThemeStyleboxOverride("focus",   bg);
        b.Pressed += pressed;
        return b;
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

    // ── Background smoke ambiance ─────────────────────────────────────────────
    // Slow-drifting translucent wisps below the grid that read as light fog.
    // Uses CPUParticles3D + a procedural soft-circle texture so it works on the
    // GL Compatibility renderer (no GPU particle requirement, no asset import).
    void SetupBackgroundSmoke()
    {
        var smoke = new CpuParticles3D
        {
            Name                = "BgSmoke",
            Amount              = 80,
            Lifetime            = 14.0,
            Preprocess          = 14.0,
            OneShot             = false,
            EmissionShape       = CpuParticles3D.EmissionShapeEnum.Box,
            EmissionBoxExtents  = new Vector3(70f, 6f, 70f),
            Direction           = new Vector3(0.3f, 1f, 0.2f),
            Spread              = 35f,
            InitialVelocityMin  = 0.5f,
            InitialVelocityMax  = 1.4f,
            Gravity             = new Vector3(0f, 0.4f, 0f),
            ScaleAmountMin      = 7f,
            ScaleAmountMax      = 13f,
            Color               = new Color(0.55f, 0.62f, 0.78f, 1f),
            Position            = new Vector3(GridCentre.X, -16f, GridCentre.Z),
        };
        // Alpha fade in/out via ColorRamp (AlphaCurve property only exists in Godot 4.5+).
        var ramp = new Gradient();
        ramp.SetColor(0, new Color(1, 1, 1, 0f));
        ramp.SetColor(1, new Color(1, 1, 1, 0f));
        ramp.AddPoint(0.25f, new Color(1, 1, 1, 1f));
        ramp.AddPoint(0.75f, new Color(1, 1, 1, 1f));
        smoke.ColorRamp = ramp;

        var quadMat = new StandardMaterial3D
        {
            AlbedoTexture          = MakeSoftParticleTexture(64),
            AlbedoColor            = new Color(0.55f, 0.62f, 0.78f, 0.12f),
            Transparency           = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode          = BaseMaterial3D.BillboardModeEnum.Enabled,
            ShadingMode            = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            DisableReceiveShadows  = true,
            BlendMode              = BaseMaterial3D.BlendModeEnum.Add,
        };
        var quad = new QuadMesh { Size = new Vector2(1f, 1f), Material = quadMat };
        smoke.Mesh = quad;

        _mainVp.AddChild(smoke);
    }

    // Builds a small soft-edged radial-alpha texture for smoke particles.
    static ImageTexture MakeSoftParticleTexture(int size)
    {
        var img      = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        float center = (size - 1) * 0.5f;
        float maxD   = center;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = x - center, dy = y - center;
            float d  = Mathf.Sqrt(dx * dx + dy * dy) / maxD;
            float a  = Mathf.Clamp(1f - d, 0f, 1f);
            a = a * a * a;  // smooth-step the falloff for a soft cloud edge
            img.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        return ImageTexture.CreateFromImage(img);
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
        RefreshGridOpacity();   // apply the blue active-floor tint at startup
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

    // Visibility falls off with distance from the current floor.
    // Floors below fade gently; floors above fade aggressively so the maze you're
    // editing always reads clearly without the ceiling grid drowning it out.
    float GridAlpha(int f)
    {
        int dist = _floor - f;          // positive = below, negative = above
        if (dist == 0) return 0.80f;
        if (dist  > 0) return Mathf.Max(0.45f * Mathf.Pow(0.65f,  dist - 1), 0.05f);
        return                Mathf.Max(0.18f * Mathf.Pow(0.45f, -dist - 1), 0.02f);
    }

    // Active floor gets a saturated blue tint so the workspace plane is unmistakable.
    // Other floors keep the neutral grey so they don't compete for attention.
    static readonly Color GridColorActive   = new(0.25f, 0.55f, 1.0f);
    static readonly Color GridColorInactive = new(0.55f, 0.55f, 0.55f);

    void RefreshGridOpacity()
    {
        foreach (var (f, mi) in _gridMi)
        {
            if (mi.MaterialOverride is not StandardMaterial3D m) continue;
            Color c = (f == _floor) ? GridColorActive : GridColorInactive;
            m.AlbedoColor = new Color(c.R, c.G, c.B, GridAlpha(f));
        }
    }

    // ── Piece geometry ─────────────────────────────────────────────────────────
    void RebuildGeometry()
    {
        foreach (var n in _cellGeo.Values) n.QueueFree();
        _cellGeo.Clear();

        // Render every placed piece on its own floor. Stairs no longer auto-spawn an
        // inverse "landing" piece on the destination floor — the user is required to
        // place an actual piece at the landing cell. The stair's step geometry still
        // physically spans both floors, so the destination-floor view shows the stair
        // structure translucent through the floor without a separate landing visual.
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

        RebuildEdgeWalls();
    }

    void BuildPieceInto(Node3D parent, PieceType type, int rot,
                        float wx, float wy, float wz, bool isActive)
    {
        // Inactive floors keep their natural color (green Start, red Exit, orange Stair, white Floor)
        // but render at lower alpha — easier to identify pieces on adjacent floors at a glance.
        // Stairs flip to bright blue when they live on the floor the player is currently
        // editing, so a busy column of orange stairs across many floors clearly highlights
        // which one you're actively on.
        Color floorCol = type switch
        {
            PieceType.Start                             => CStart,
            PieceType.Exit                              => CExit,
            PieceType.StairsUp or PieceType.StairsDown => isActive ? CStairActive : CStair,
            _                                           => CFloor,
        };

        float cx   = wx + CS * 0.5f;
        float cy   = wy + FlrT * 0.5f;
        float cz   = wz + CS * 0.5f;
        // Inactive floors used to render at 0.40 alpha; bumped to give more visibility,
        // especially for pieces ABOVE the current floor (which the camera looks through).
        float flrA;
        if (isActive) flrA = 1f;
        else
        {
            int pieceFloor = Mathf.RoundToInt(wy / EdFH);
            flrA = pieceFloor > _floor ? 0.70f : 0.55f;
        }

        // Stair pieces don't get a central floor square — the step geometry below already
        // fills that space, so the orange flat slab was redundant.
        if (type != PieceType.StairsUp && type != PieceType.StairsDown)
        {
            parent.AddChild(MakeMeshBox(
                new Vector3(cx, cy, cz),
                new Vector3(CentW - 0.1f, FlrT, CentW - 0.1f),
                floorCol, flrA));
        }

        Dir open = PieceDB.GetOpenings(type, rot);
        // For stair pieces, suppress the arm floor on the CROSS side. The cross side is
        // where the steps reach a different floor's Y, so a floor patch at the home Y
        // sits OVER the descending/ascending steps and visually breaks the chain — most
        // noticeable on StairsDown chains where it appears as a lip at home-floor level
        // blocking the view of the next stair below.
        Dir crossDir = PieceDB.IsStair(type) ? PieceDB.GetStairCrossDir(type, rot) : Dir.None;

        void ArmFloor(float ax, float az, float aw, float ad)
            => parent.AddChild(MakeMeshBox(
                   new Vector3(ax, cy, az),
                   new Vector3(aw - 0.1f, FlrT, ad - 0.1f),
                   floorCol, flrA));

        if ((open & Dir.N) != 0 && crossDir != Dir.N) ArmFloor(cx,              wz + ArmLen * 0.5f,      ArmW, ArmLen);
        if ((open & Dir.S) != 0 && crossDir != Dir.S) ArmFloor(cx,              wz + CS - ArmLen * 0.5f, ArmW, ArmLen);
        if ((open & Dir.E) != 0 && crossDir != Dir.E) ArmFloor(wx + CS - ArmLen * 0.5f, cz,              ArmLen, ArmW);
        if ((open & Dir.W) != 0 && crossDir != Dir.W) ArmFloor(wx + ArmLen * 0.5f,      cz,              ArmLen, ArmW);

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

        // Bright blue when this stair sits on the floor you're editing; orange otherwise.
        // The blue-vs-orange split makes it obvious which stair in a stack is your active
        // one when several stairs span multiple floors.
        Color col = isActive ? CStairActive : CStair;
        float alpha;
        if (isActive) alpha = 0.95f;
        else
        {
            int pieceFloor = Mathf.RoundToInt(wy / EdFH);
            alpha = pieceFloor > _floor ? 0.70f : 0.55f;
        }

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
            col, isActive ? 0.22f : 0.07f));
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
                    if (!wasDrag)
                    {
                        // Right-click while holding a piece → abandon the hold
                        // (Start/Exit return to origin, regular pieces are discarded).
                        if (_holding)
                        {
                            AbandonHolding();
                        }
                        else if (TryGetCell(mb.Position, out int cx, out int cy))
                        {
                            // Short right-click = delete the piece at that cell (Start/Exit are permanent).
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
                // 10-pixel threshold (was 4) — small mouse jitter no longer eats the click,
                // so a quick right-click reliably triggers delete / abandon-hold instead of
                // being misread as a tiny orbit drag.
                if ((mm.Position - _rmbStart).Length() > 10f) _rmbDrag = true;
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
        // Start/Exit are locked to their row regardless of where the cursor is, so the user
        // can never move them off-row even visually.
        if (cx >= 0 && _holding)
        {
            if (_heldType == PieceType.Start) cy = StartRow;
            else if (_heldType == PieceType.Exit) cy = ExitRow;
        }

        if (cx == _hoverCx && cy == _hoverCy) return;
        _hoverCx = cx;
        _hoverCy = cy;
        UpdateHoverHighlight();

        // Re-snap the held ghost's rotation to the new cell's neighbors so the user
        // sees the orientation they'll actually get on click.
        if (_holding && _heldGeo != null)
        {
            if (cx >= 0)
            {
                int snapped = PieceDB.IsStair(_heldType)
                    ? InferStairRotation(_heldType, cx, cy, _floor, fallback: _heldRot)
                    : InferRotation(_heldType,      cx, cy, _floor, fallback: _heldRot);
                if (snapped != _heldRot)
                {
                    _heldRot = snapped;
                    RebuildHeldGeo();
                }
                _heldGeo.Visible  = true;
                _heldGeo.Position = new Vector3(cx * CS, _floor * EdFH, cy * CS);
            }
            else
            {
                _heldGeo.Visible = false;
            }
        }

        // Same idea for the palette preview ghost.
        if (_previewGeo != null && _selType.HasValue)
        {
            if (cx >= 0)
            {
                int snapped = PieceDB.IsStair(_selType.Value)
                    ? InferStairRotation(_selType.Value, cx, cy, _floor, fallback: _rotation)
                    : InferRotation(_selType.Value,      cx, cy, _floor, fallback: _rotation);
                if (snapped != _rotation)
                {
                    _rotation = snapped;
                    RebuildPreviewGeo();
                }
                _previewGeo.Visible  = true;
                _previewGeo.Position = new Vector3(cx * CS, _floor * EdFH, cy * CS);
            }
            else
            {
                _previewGeo.Visible = false;
            }
        }
    }

    // Rebuilds the held-piece ghost in place at the current hover position.
    void RebuildHeldGeo()
    {
        if (!_holding) return;
        _heldGeo?.QueueFree();
        _heldGeo = new Node3D();
        _world.AddChild(_heldGeo);
        BuildPieceInto(_heldGeo, _heldType, _heldRot, 0f, 0f, 0f, isActive: true);
        YellowTint(_heldGeo);
        if (_hoverCx >= 0)
            _heldGeo.Position = new Vector3(_hoverCx * CS, _floor * EdFH, _hoverCy * CS);
    }

    void UpdateHoverHighlight()
    {
        foreach (var mi in _hoverBorder) { if (IsInstanceValid(mi)) mi.QueueFree(); }
        _hoverBorder.Clear();

        if (_hoverCx < 0 || _hoverCy < 0) return;
        if (!_holding && !_selType.HasValue) return;

        // Determine if this hover cell is a valid target. Start/Exit are already clamped
        // to their row by SetHover, so the off-row case never appears here — what's left is
        // "no rotation of this piece would be a legal placement at the hovered cell."
        PieceType? activeType = _holding ? _heldType : _selType;
        bool invalid = false;
        if (activeType.HasValue)
        {
            bool anyValid = false;
            for (int r = 0; r < 4; r++)
            {
                if (IsValidPlacement(_hoverCx, _hoverCy, activeType.Value, r, silent: true))
                {
                    anyValid = true;
                    break;
                }
            }
            invalid = !anyValid;
        }

        float y  = _floor * EdFH + FlrT + 0.1f;
        float x0 = _hoverCx * CS;
        float z0 = _hoverCy * CS;
        const float bW = 0.28f, bH = 0.1f;

        // Strong, glowing cyan-blue when valid — stands out against the softer blue
        // grid lines so the hovered cell pops. Red for invalid placement.
        var hoverColor = invalid
            ? new Color(0.95f, 0.18f, 0.18f, 0.98f)
            : new Color(0.55f, 0.85f, 1.0f,  1.0f);

        var mat = new StandardMaterial3D
        {
            AlbedoColor                = hoverColor,
            ShadingMode                = BaseMaterial3D.ShadingModeEnum.Unshaded,
            EmissionEnabled            = !invalid,
            Emission                   = new Color(0.30f, 0.65f, 1.0f),
            EmissionEnergyMultiplier   = 1.8f,
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
        _clickSfx?.Play();
        SetStatus($"Moving {PieceDB.Labels[_heldType]}  —  R to rotate  |  click to place  |  Esc to cancel");
    }

    void DropPiece(int cx, int cy)
    {
        if (!_holding) return;

        // Clamp Start/Exit to their valid row — clicking anywhere on the grid drops them
        // somewhere on their dedicated row (matching what the user sees via the held ghost).
        if (_heldType == PieceType.Start) cy = StartRow;
        else if (_heldType == PieceType.Exit) cy = ExitRow;

        // Auto-orient on drop, same as palette placement
        int useRot = PieceDB.IsStair(_heldType)
            ? InferStairRotation(_heldType, cx, cy, _floor, fallback: _heldRot)
            : InferRotation(_heldType,      cx, cy, _floor, fallback: _heldRot);

        if (!IsValidPlacement(cx, cy, _heldType, useRot)) return;

        _heldGeo?.QueueFree();
        _heldGeo = null;

        RemovePiece(cx, cy);   // clear any existing piece at destination

        var piece = new MazePiece { X = cx, Y = cy, Floor = _floor,
                                    Type = _heldType, Rotation = useRot };
        _maze.Pieces.Add(piece);
        _heldRot = useRot;   // remember the snapped rotation in case user picks it up again

        var key = new CK(cx, cy, _floor);
        var geo = new Node3D();
        _world.AddChild(geo);
        BuildPieceInto(geo, _heldType, useRot, cx * CS, _floor * EdFH, cy * CS, isActive: true);
        _cellGeo[key] = geo;

        _holding = false;
        RebuildGeometry();   // also refreshes stair shadows
        RebuildBorderHints();
        UpdateHoverHighlight();
        _clickSfx?.Play();
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
        _clickSfx?.Play();
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
    // Right-click / void-click / D while holding always discards the held piece, including
    // Start and Exit. If a critical piece gets discarded, EnsureStartExit() re-spawns it at
    // the default cell on the next Save/Load/Clear, so the maze auto-recovers.
    // Escape still calls CancelHolding() directly for the "I changed my mind" gesture.
    void AbandonHolding()
    {
        if (!_holding) return;
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
        // Border hints are floor-aware (Exit can land on any floor — see RebuildBorderHints)
        // so they must be redrawn whenever the visible floor set changes.
        RebuildBorderHints();
        // Held ghost follows the floor change — same cell, new Y level
        if (_holding && _heldGeo != null && _hoverCx >= 0)
            _heldGeo.Position = new Vector3(_hoverCx * CS, _floor * EdFH, _hoverCy * CS);
    }

    // ── Piece type selection ───────────────────────────────────────────────────
    void SelectType(PieceType pt)
    {
        // Picking from the palette while holding a piece discards it (Start/Exit return to origin)
        if (_holding) AbandonHolding();

        _selType = pt;
        RefreshPalette();
        RebuildBorderHints();
        RebuildPreviewGeo();
        SetStatus($"{PieceDB.Labels[pt]}  [{PieceDB.GoldCosts[pt]}g]");
        // Camera stays where it is — user can right-click orbit freely.
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
    // Shows a Yes/No confirmation dialog before wiping the current slot —
    // clearing is destructive (loses every piece on every floor) so a single
    // misclick shouldn't trash the user's maze.
    void ConfirmClearMap()
    {
        var dialog = new ConfirmationDialog
        {
            Title         = "Clear Map",
            DialogText    = "Clear every piece in this maze?\nThis cannot be undone.",
            OkButtonText  = "Yes",
            CancelButtonText = "No",
        };
        if (_font != null)
        {
            dialog.AddThemeFontOverride("font", _font);
            dialog.AddThemeFontSizeOverride("font_size", 16);
        }
        AddChild(dialog);
        dialog.Confirmed   += () => { ClearCurrentMap(); dialog.QueueFree(); };
        dialog.Canceled    += () => dialog.QueueFree();
        dialog.CloseRequested += () => dialog.QueueFree();
        dialog.PopupCentered(new Vector2I(360, 160));
    }

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
            var  data    = MazeSerializer.Load(i);
            string name  = data?.Name ?? "(empty)";
            int pieces   = data?.Pieces?.Count ?? 0;
            // A saved-but-broken maze (has pieces yet isn't game-ready) gets a red [!] prefix
            // and red text. The slot can still be clicked to load and fix, but it's flagged
            // as unusable until the path between Start and Exit is restored — PlayGameScreen
            // already filters out non-ready slots from the matchmaking dropdown.
            bool notReady = data != null && pieces > 0 && !data.IsGameReady;
            string prefix = notReady ? "[!] " : "  ";
            _slotBtns[i].Text = $"{prefix}{i + 1}.  {name}  [{pieces}p]";

            bool isCur = i == _slot;
            _slotBtns[i].AddThemeStyleboxOverride("normal",
                Flat(isCur ? new Color(0.20f, 0.16f, 0.04f, 0.80f)
                           : new Color(0.04f, 0.04f, 0.04f, 0.55f)));
            _slotBtns[i].AddThemeColorOverride("font_color",
                notReady ? new Color(1f, 0.30f, 0.30f)
                : isCur  ? new Color(1f, 0.92f, 0.55f)
                         : Colors.White);
        }
    }

    // ── Enter dungeon ──────────────────────────────────────────────────────────
    void OnEnterDungeon()
    {
        // Must be a full game-ready maze (Start, Exit, and a valid path between them) before
        // we let the user test it. Same gate as Save/PlayGameScreen filtering.
        if (!MazeValidator.IsGameReady(_maze, out string err))
        {
            SetStatus($"Can't enter — {err}");
            return;
        }
        SaveCurrentSlot();
        // Play-test always goes through the dual-maze arena scene so the exit dumps the
        // player into the central arena (matching the online match layout). Both maze
        // slots are set to the maze being edited — the second copy is a mirror on the
        // far side of the arena, which the player can ignore or wander into.
        GameState.ActiveSlot        = _slot;
        GameState.IsArenaMode       = true;
        GameState.ArenaSlotA        = _slot;
        GameState.ArenaSlotB        = _slot;
        GameState.EditorReturnScene = "res://scenes/MazeEditor3D.tscn";
        DungeonArena.ChosenSpawn    = DungeonArena.SpawnPoint.MazeA;
        GetTree().ChangeSceneToFile("res://scenes/DungeonArena.tscn");
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

    // ── Pulsing red error indicator ────────────────────────────────────────────
    // When a validation rejects a placement because of an opening-mismatch with a
    // specific neighbor piece, we flash a red glow over that neighbor's cell so the
    // user can see exactly which piece is causing the conflict. The pulse fades
    // out over ~3.5 seconds and re-activates each time a new mismatch is reported.
    void FlagErrorCell(int x, int y, int floor)
    {
        if (_errorPulse == null)
        {
            _errorPulseMat = new StandardMaterial3D
            {
                AlbedoColor               = new Color(1.0f, 0.18f, 0.18f, 0.55f),
                ShadingMode               = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency              = BaseMaterial3D.TransparencyEnum.Alpha,
                EmissionEnabled           = true,
                Emission                  = new Color(1.0f, 0.12f, 0.12f),
                EmissionEnergyMultiplier  = 2.0f,
            };
            _errorPulse = new MeshInstance3D
            {
                Name             = "ErrorPulse",
                Mesh             = new BoxMesh { Size = new Vector3(CS - 0.4f, 0.12f, CS - 0.4f) },
                MaterialOverride = _errorPulseMat,
            };
            _world.AddChild(_errorPulse);
        }
        _errorPulseCx    = x;
        _errorPulseCy    = y;
        _errorPulseFloor = floor;
        _errorPulseTimer = 3.5f;
        _errorPulsePhase = 0f;
        _errorPulse.Visible = true;
    }

    void UpdateErrorPulse(float dt)
    {
        if (_errorPulse == null || !_errorPulse.Visible) return;
        _errorPulseTimer -= dt;
        if (_errorPulseTimer <= 0f)
        {
            _errorPulse.Visible = false;
            return;
        }

        // Pulse cycle (~3 Hz) + slight vertical bob — "glows red up and down"
        _errorPulsePhase += dt * 6.0f;
        float pulse01 = (Mathf.Sin(_errorPulsePhase) + 1f) * 0.5f;
        // Final-second fade so it doesn't snap off
        float life    = Mathf.Clamp(_errorPulseTimer / 1.2f, 0f, 1f);

        if (_errorPulseMat != null)
        {
            _errorPulseMat.AlbedoColor              = new Color(1.0f, 0.18f, 0.18f, (0.35f + pulse01 * 0.5f) * life);
            _errorPulseMat.EmissionEnergyMultiplier = (1.4f + pulse01 * 2.4f) * life;
        }

        float baseY = _errorPulseFloor * EdFH + FlrT + 0.25f;
        _errorPulse.Position = new Vector3(
            _errorPulseCx * CS + CS * 0.5f,
            baseY + Mathf.Sin(_errorPulsePhase) * 0.35f,
            _errorPulseCy * CS + CS * 0.5f);
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
        // Green for Start, red for Exit
        Color tint = activeType == PieceType.Start
            ? new Color(0.20f, 0.90f, 0.35f, 0.45f)
            : new Color(0.95f, 0.25f, 0.25f, 0.45f);

        // Start stays on floor 0. Exit can be placed on any floor — highlight its row
        // on every floor so the user sees all valid destinations.
        int floorMin = activeType == PieceType.Exit ? FlMin : 0;
        int floorMax = activeType == PieceType.Exit ? FlMax : 0;

        for (int f = floorMin; f <= floorMax; f++)
        {
            // Dim hints on non-current floors so the active floor still reads as primary
            float alphaScale = (f == _floor) ? 1f : 0.35f;
            var floorMat = new StandardMaterial3D
            {
                AlbedoColor  = new Color(tint.R, tint.G, tint.B, tint.A * alphaScale),
                ShadingMode  = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            };
            float y = f * EdFH + FlrT + 0.05f;

            for (int x = 0; x < GW; x++)
            {
                // Skip cells that already contain a piece on this floor
                if (_cellGeo.ContainsKey(new CK(x, validRow, f))) continue;
                var mi = new MeshInstance3D
                {
                    Mesh             = new BoxMesh { Size = new Vector3(CS - 0.1f, 0.06f, CS - 0.1f) },
                    MaterialOverride = floorMat,
                    Position         = new Vector3(x * CS + CS * 0.5f, y, validRow * CS + CS * 0.5f),
                };
                _world.AddChild(mi);
                _borderHints.Add(mi);
            }
        }
    }

    // ── Placement validation ───────────────────────────────────────────────────
    bool IsValidPlacement(int cx, int cy, PieceType type, int rot, bool silent = false)
    {
        // Start stays in its row; Exit stays in its row
        if (type == PieceType.Start)
        {
            if (cy != StartRow)
            {
                if (!silent) SetStatus("Start must stay in the top row.");
                return false;
            }
        }
        else if (type == PieceType.Exit)
        {
            if (cy != ExitRow)
            {
                if (!silent) SetStatus("Exit must stay in the bottom row.");
                return false;
            }
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
                if (!silent)
                {
                    SetStatus($"Opening mismatch with {PieceDB.Labels[nbr.Type]} at ({nx},{ny}).");
                    FlagErrorCell(nx, ny, _floor);
                }
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
            if (!silent) SetStatus("Piece must connect to an existing piece.");
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
                FlagErrorCell(nx, ny, _floor);
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

        // Stair-to-stair: a new stair placed AT the landing cell of an existing stair on
        // an adjacent floor is a valid connection (the player can step from the existing
        // stair's top onto the new stair's bottom at the cell boundary). This is what
        // makes "chained" stairs going up or down multiple floors work — and it lets a
        // StairsUp meet a StairsDown directly without an intermediate corridor.
        bool stairLanding = HasStairLandingNeighbor(cx, cy, _floor, type, rot);

        int floorCount = _maze.Pieces.Count(p => p.Floor == _floor);
        if (floorCount > 0 && !flatConnected && !crossConnected && !stairLanding)
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

        // Case A: (cx, cy) IS itself a stair landing — i.e., a stair on an adjacent
        // floor pops up directly into my cell. I'm connected if my opening faces back
        // toward the stair (opposite of the stair's cross direction).
        // Without this, placing a piece right AT a stair's landing cell — which is
        // especially common near the grid border where there's no room to use an
        // adjacent intermediate cell — would fail "must connect to an existing piece".
        foreach (var s in _maze.Pieces)
        {
            if (!PieceDB.IsStair(s.Type)) continue;
            int sf = s.Floor + PieceDB.StairFloorDelta(s.Type);
            if (sf != floor) continue;
            Dir scross = PieceDB.GetStairCrossDir(s.Type, s.Rotation);
            var (sdx, sdy) = DirToOffset(scross);
            if (s.X + sdx == cx && s.Y + sdy == cy)
            {
                Dir backToStair = PieceDB.Opposite(scross);
                if ((myOpen & backToStair) != 0) return true;
            }
        }

        // Case B: an adjacent cell on this floor is a stair landing — my opening into
        // that cell acts as the connection (the landing's "virtual" piece).
        foreach (Dir dir in new[] { Dir.N, Dir.S, Dir.E, Dir.W })
        {
            if ((myOpen & dir) == 0) continue;
            var (dx, dz) = DirToOffset(dir);
            int nx = cx + dx, ny = cy + dz;
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

    // For non-stair pieces: pick the rotation that connects to the most neighbors WHILE
    // never producing an opening mismatch. Ties prefer the user's current `fallback`.
    // If no rotation is valid, returns `fallback` (the placement call will then fail loudly).
    int InferRotation(PieceType type, int x, int y, int floor, int fallback)
    {
        int bestRot     = fallback;
        int bestScore   = -1;
        bool foundValid = false;

        // Iterate starting from `fallback` so ties resolve to the user's preferred rotation.
        for (int i = 0; i < 4; i++)
        {
            int r = (fallback + i) % 4;
            if (!IsRotationCompatibleAt(type, r, x, y, floor)) continue;

            int score = CountConnectionsAt(type, r, x, y, floor);
            if (!foundValid || score > bestScore)
            {
                bestRot    = r;
                bestScore  = score;
                foundValid = true;
            }
        }
        return bestRot;
    }

    // True if rotation `r` at (x,y,floor) has no opening-mismatch with any placed neighbor.
    // Mirrors IsValidPlacement's neighbor check but with no side effects.
    bool IsRotationCompatibleAt(PieceType type, int r, int x, int y, int floor)
    {
        foreach (Dir dir in new[] { Dir.N, Dir.E, Dir.S, Dir.W })
        {
            var (dx, dz) = DirToOffset(dir);
            int nx = x + dx, ny = y + dz;
            if (nx < 0 || nx >= GW || ny < 0 || ny >= GH) continue;
            if (!TryGetPlacedPieceOnFloor(nx, ny, floor, out var nb)) continue;

            Dir opp        = PieceDB.Opposite(dir);
            bool myOpen    = PieceDB.HasSameFloorOpening(type,    r,           dir);
            bool nbrOpen   = PieceDB.HasSameFloorOpening(nb.Type, nb.Rotation, opp);
            if (myOpen != nbrOpen) return false;
        }
        return true;
    }

    // Counts how many of this piece's openings actually connect to a placed neighbor.
    int CountConnectionsAt(PieceType type, int r, int x, int y, int floor)
    {
        Dir openings = PieceDB.GetOpenings(type, r);
        int count    = 0;
        foreach (Dir dir in new[] { Dir.N, Dir.E, Dir.S, Dir.W })
        {
            if ((openings & dir) == 0) continue;
            var (dx, dz) = DirToOffset(dir);
            int nx = x + dx, ny = y + dz;
            if (nx < 0 || nx >= GW || ny < 0 || ny >= GH) continue;
            if (!TryGetPlacedPieceOnFloor(nx, ny, floor, out var nb)) continue;
            Dir opp = PieceDB.Opposite(dir);
            if (PieceDB.HasSameFloorOpening(nb.Type, nb.Rotation, opp)) count++;
        }

        // Also count connections through stair landings (matches the validation in
        // HasStairLandingNeighbor). Without this the auto-rotation didn't know that a
        // piece placed AT the top/bottom of a stair chain should orient itself to face
        // back toward the stair — so corridors at chain endpoints stayed at fallback
        // rotation and you'd have to spin them by hand.
        foreach (var s in _maze.Pieces)
        {
            if (!PieceDB.IsStair(s.Type)) continue;
            int sf = s.Floor + PieceDB.StairFloorDelta(s.Type);
            if (sf != floor) continue;
            Dir scross = PieceDB.GetStairCrossDir(s.Type, s.Rotation);
            var (sdx, sdy) = DirToOffset(scross);
            int landingX = s.X + sdx, landingY = s.Y + sdy;

            // Case A: my cell IS the stair's landing — opening at back-to-stair direction connects.
            if (landingX == x && landingY == y)
            {
                Dir backDir = PieceDB.Opposite(scross);
                if ((openings & backDir) != 0) count++;
            }
            // Case B: a neighbor cell of mine is a stair landing — my opening points to it.
            foreach (Dir dir in new[] { Dir.N, Dir.E, Dir.S, Dir.W })
            {
                if ((openings & dir) == 0) continue;
                var (dx, dz) = DirToOffset(dir);
                if (x + dx == landingX && y + dz == landingY) { count++; break; }
            }
        }
        return count;
    }

    // For stair pieces: pick the rotation that maximises connections at the flat
    // (same-floor) side and at the cross-floor landing cell.
    int InferStairRotation(PieceType type, int x, int y, int floor, int fallback)
    {
        int destFloor = floor + PieceDB.StairFloorDelta(type);

        int bestRot     = fallback;
        int bestScore   = -1;
        bool foundValid = false;

        for (int i = 0; i < 4; i++)
        {
            // Iterate starting from `fallback` so ties prefer the user's current choice.
            int r = (fallback + i) % 4;

            // Reject rotations that would cause an opening-mismatch with a same-floor
            // neighbor on the stair's side walls or flat side. Otherwise the auto-rotate
            // could "pick" a rotation that immediately fails IsValidStairPlacement and the
            // click silently does nothing — the bug that made the "two pieces on two
            // floors with a stair between them" scenario feel impossible to set up.
            if (!IsStairRotationCompatible(type, r, x, y, floor)) continue;

            int score = 0;

            Dir flatDir = PieceDB.GetStairFlatDir(type, r);
            var (fdx, fdy) = DirToOffset(flatDir);
            int nx = x + fdx, ny = y + fdy;
            if (nx >= 0 && nx < GW && ny >= 0 && ny < GH &&
                TryGetPlacedPieceOnFloor(nx, ny, floor, out var nbr))
            {
                Dir opp = PieceDB.Opposite(flatDir);
                if (PieceDB.HasSameFloorOpening(nbr.Type, nbr.Rotation, opp))
                    score += 2;
            }

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

            // Stair-on-stair link: if my flat side faces a stair's landing cell on this
            // floor — meaning my flat opening connects directly to another stair's top —
            // weight this rotation highly so chained stairs auto-orient correctly.
            foreach (var s in _maze.Pieces)
            {
                if (!PieceDB.IsStair(s.Type)) continue;
                int sf = s.Floor + PieceDB.StairFloorDelta(s.Type);
                if (sf != floor) continue;
                Dir scross = PieceDB.GetStairCrossDir(s.Type, s.Rotation);
                var (sdx, sdy) = DirToOffset(scross);
                // My flat-side neighbor cell == that stair's landing cell?
                if (s.X + sdx == nx && s.Y + sdy == ny) { score += 2; break; }
                // Or my OWN cell IS that stair's landing and my flat faces back toward it?
                if (s.X + sdx == x && s.Y + sdy == y &&
                    PieceDB.Opposite(scross) == flatDir) { score += 2; break; }
            }

            if (!foundValid || score > bestScore)
            {
                bestRot    = r;
                bestScore  = score;
                foundValid = true;
            }
        }

        return bestRot;
    }

    // True iff placing `type` at rotation `r` at (x,y,floor) wouldn't create an
    // opening mismatch with any placed same-floor neighbor (excluding the cross face,
    // which connects to the OTHER floor and is checked separately).
    bool IsStairRotationCompatible(PieceType type, int r, int x, int y, int floor)
    {
        Dir crossDir = PieceDB.GetStairCrossDir(type, r);
        foreach (Dir dir in new[] { Dir.N, Dir.S, Dir.E, Dir.W })
        {
            if (dir == crossDir) continue;
            var (dx, dz) = DirToOffset(dir);
            int nx = x + dx, ny = y + dz;
            if (nx < 0 || nx >= GW || ny < 0 || ny >= GH) continue;
            if (!TryGetPlacedPieceOnFloor(nx, ny, floor, out var nbr)) continue;
            Dir  opp     = PieceDB.Opposite(dir);
            bool myOpen  = PieceDB.HasSameFloorOpening(type, r, dir);
            bool nbrOpen = PieceDB.HasSameFloorOpening(nbr.Type, nbr.Rotation, opp);
            if (myOpen != nbrOpen) return false;
        }
        return true;
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
