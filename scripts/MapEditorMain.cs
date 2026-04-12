using Godot;
using System;
using static TileData;

public partial class MapEditorMain : Node2D
{
    // ── Constants ─────────────────────────────────────────────────────────────
    private const int GridW     = 10;
    private const int GridH     = 10;
    private const int Floors    = 3;
    private const int Cell      = 64;
    private const int MainFloor = Floors / 2;   // index 1 = ground floor

    private static readonly Color BgColor     = new(0f,    0f,    0f,    1f);
    private static readonly Color LineColor   = new(0.18f, 0.18f, 0.18f, 1f);
    private static readonly Color BorderColor = new(0.55f, 0.55f, 0.55f, 1f);

    private static readonly TileType[] PaletteTiles =
    {
        TileType.Floor, TileType.Wall,
        TileType.StairsUp, TileType.StairsDown,
        TileType.Entrance, TileType.Exit,
    };

    // ── State ──────────────────────────────────────────────────────────────────
    private TileType[,,] _mapData   = new TileType[Floors, GridW, GridH];
    private int          _curFloor  = MainFloor;
    private TileType     _selTile   = TileType.Floor;
    private bool         _isTesting;
    private bool         _painting;
    private bool         _erasing;

    // ── Node refs (all built in _Ready) ───────────────────────────────────────
    private CharacterBody2D _player      = null!;
    private CanvasLayer     _uiLayer     = null!;
    private Control         _editorRoot  = null!;
    private Label           _floorLabel  = null!;
    private Button          _stopBtn     = null!;
    private Button[]        _paletteBtns = Array.Empty<Button>();
    private StaticBody2D?   _wallBody;

    // ══════════════════════════════════════════════════════════════════════════
    //  LIFECYCLE
    // ══════════════════════════════════════════════════════════════════════════
    public override void _Ready()
    {
        BuildCamera();
        BuildPlayer();
        BuildUi();
    }

    private void BuildCamera()
    {
        var cam = new Camera2D
        {
            Position = new Vector2(GridW * Cell * 0.5f, GridH * Cell * 0.5f),
        };
        AddChild(cam);
    }

    private void BuildPlayer()
    {
        var scene       = GD.Load<PackedScene>("res://scenes/Player.tscn");
        _player         = (CharacterBody2D)scene.Instantiate();
        _player.Visible     = false;
        _player.ProcessMode = ProcessModeEnum.Disabled;
        AddChild(_player);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  UI BUILDING
    // ══════════════════════════════════════════════════════════════════════════
    private void BuildUi()
    {
        _uiLayer = new CanvasLayer();
        AddChild(_uiLayer);

        // _editorRoot holds all editor widgets — hidden during test play
        _editorRoot             = new Control();
        _editorRoot.MouseFilter = Control.MouseFilterEnum.Ignore;
        _editorRoot.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _uiLayer.AddChild(_editorRoot);

        BuildPalette();
        BuildFloorPanel();
        BuildActionPanel();

        // Stop-Test button lives outside _editorRoot so it stays visible in test mode
        _stopBtn         = new Button { Text = "◀ Stop Test", Size = new Vector2(145, 40), Position = new Vector2(10, 10), Visible = false };
        _stopBtn.Pressed += OnStopTest;
        _uiLayer.AddChild(_stopBtn);
    }

    // ── Palette (bottom bar) ──────────────────────────────────────────────────
    private void BuildPalette()
    {
        var panel = new PanelContainer();
        panel.SetAnchor(Side.Left,   0f); panel.SetAnchor(Side.Right,  1f);
        panel.SetAnchor(Side.Top,    1f); panel.SetAnchor(Side.Bottom, 1f);
        panel.SetOffset(Side.Top,  -110); panel.SetOffset(Side.Bottom,   0);

        var panelStyle = new StyleBoxFlat { BgColor = new Color(0.08f, 0.08f, 0.08f, 0.96f) };
        panel.AddThemeStyleboxOverride("panel", panelStyle);
        _editorRoot.AddChild(panel);

        var hbox = new HBoxContainer();
        hbox.Alignment = BoxContainer.AlignmentMode.Center;
        hbox.AddThemeConstantOverride("separation", 10);
        panel.AddChild(hbox);

        var hint = new Label { Text = "Right-click: erase", VerticalAlignment = VerticalAlignment.Center };
        hint.AddThemeColorOverride("font_color", new Color(0.55f, 0.55f, 0.55f));
        hbox.AddChild(hint);
        hbox.AddChild(new VSeparator());

        _paletteBtns = new Button[PaletteTiles.Length];
        for (int i = 0; i < PaletteTiles.Length; i++)
        {
            var tile = PaletteTiles[i];
            var btn  = new Button { CustomMinimumSize = new Vector2(100, 72), Text = TileNames[tile] };
            ApplyPaletteStyle(btn, tile, selected: false);
            var captured = tile;
            btn.Pressed += () => OnPalettePressed(captured);
            hbox.AddChild(btn);
            _paletteBtns[i] = btn;
        }
        ApplyPaletteStyle(_paletteBtns[0], PaletteTiles[0], selected: true);
    }

    private static void ApplyPaletteStyle(Button btn, TileType tile, bool selected)
    {
        var base_ = TileColors[tile];
        var s     = new StyleBoxFlat
        {
            BgColor     = selected ? base_.Darkened(0.1f) : base_.Darkened(0.38f),
            BorderColor = selected ? Colors.Yellow        : base_.Lightened(0.25f),
        };
        s.SetBorderWidthAll(selected ? 3 : 2);
        s.SetCornerRadiusAll(4);
        btn.AddThemeStyleboxOverride("normal",  s);
        btn.AddThemeStyleboxOverride("hover",   s);
        btn.AddThemeStyleboxOverride("pressed", s);
        btn.AddThemeColorOverride("font_color", Colors.White);
    }

    private void OnPalettePressed(TileType tile)
    {
        _selTile = tile;
        for (int i = 0; i < _paletteBtns.Length; i++)
            ApplyPaletteStyle(_paletteBtns[i], PaletteTiles[i], PaletteTiles[i] == tile);
    }

    // ── Floor panel (top-right) ───────────────────────────────────────────────
    private void BuildFloorPanel()
    {
        var panel = new PanelContainer();
        panel.SetAnchor(Side.Left,   1f); panel.SetAnchor(Side.Right,  1f);
        panel.SetAnchor(Side.Top,    0f); panel.SetAnchor(Side.Bottom, 0f);
        panel.SetOffset(Side.Left, -172); panel.SetOffset(Side.Right,   -8);
        panel.SetOffset(Side.Top,     8); panel.SetOffset(Side.Bottom, 138);

        var style = new StyleBoxFlat { BgColor = new Color(0.08f, 0.08f, 0.08f, 0.9f) };
        panel.AddThemeStyleboxOverride("panel", style);
        _editorRoot.AddChild(panel);

        var vbox = new VBoxContainer();
        vbox.Alignment = BoxContainer.AlignmentMode.Center;
        vbox.AddThemeConstantOverride("separation", 8);
        panel.AddChild(vbox);

        var up = new Button { Text = "▲ Up Floor" };
        up.Pressed += OnFloorUp;
        vbox.AddChild(up);

        _floorLabel = new Label { Text = "Floor (main)", HorizontalAlignment = HorizontalAlignment.Center };
        _floorLabel.AddThemeColorOverride("font_color", Colors.White);
        vbox.AddChild(_floorLabel);

        var dn = new Button { Text = "▼ Down Floor" };
        dn.Pressed += OnFloorDown;
        vbox.AddChild(dn);
    }

    // ── Action panel (top-left) ───────────────────────────────────────────────
    private void BuildActionPanel()
    {
        var hbox = new HBoxContainer();
        hbox.SetAnchor(Side.Left,  0f); hbox.SetAnchor(Side.Right,  0f);
        hbox.SetAnchor(Side.Top,   0f); hbox.SetAnchor(Side.Bottom, 0f);
        hbox.SetOffset(Side.Left,  10); hbox.SetOffset(Side.Right,  420);
        hbox.SetOffset(Side.Top,   10); hbox.SetOffset(Side.Bottom,  50);
        hbox.AddThemeConstantOverride("separation", 6);
        _editorRoot.AddChild(hbox);

        AddActionBtn(hbox, "Validate",   OnValidate  );
        AddActionBtn(hbox, "Save",       OnSave      );
        AddActionBtn(hbox, "Load",       OnLoad      );
        AddActionBtn(hbox, "▶ Test Map", OnStartTest );
    }

    private static void AddActionBtn(HBoxContainer parent, string label, Action callback)
    {
        var btn = new Button { Text = label };
        btn.Pressed += callback;
        parent.AddChild(btn);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  DRAWING
    // ══════════════════════════════════════════════════════════════════════════
    public override void _Draw()
    {
        // Black background
        DrawRect(new Rect2(0, 0, GridW * Cell, GridH * Cell), BgColor);

        // Tile fills
        for (int x = 0; x < GridW; x++)
        for (int y = 0; y < GridH; y++)
        {
            var tile = _mapData[_curFloor, x, y];
            if (tile != TileType.Empty)
                DrawRect(new Rect2(x * Cell + 1, y * Cell + 1, Cell - 2, Cell - 2), TileColors[tile]);
        }

        // Inner grid lines
        for (int x = 1; x < GridW; x++)
            DrawLine(new Vector2(x * Cell, 0), new Vector2(x * Cell, GridH * Cell), LineColor);
        for (int y = 1; y < GridH; y++)
            DrawLine(new Vector2(0, y * Cell), new Vector2(GridW * Cell, y * Cell), LineColor);

        // Outer border
        DrawRect(new Rect2(0, 0, GridW * Cell, GridH * Cell), BorderColor, filled: false, width: 2f);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  INPUT — tile painting
    // ══════════════════════════════════════════════════════════════════════════
    public override void _UnhandledInput(InputEvent @event)
    {
        if (_isTesting) return;

        if (@event is InputEventMouseButton mb)
        {
            switch (mb.ButtonIndex)
            {
                case MouseButton.Left:
                    _painting = mb.Pressed;
                    if (mb.Pressed) { _erasing = false; PaintAtMouse(erase: false); }
                    break;
                case MouseButton.Right:
                    _erasing = mb.Pressed;
                    if (mb.Pressed) { _painting = false; PaintAtMouse(erase: true); }
                    break;
            }
        }
        else if (@event is InputEventMouseMotion)
        {
            if      (_painting) PaintAtMouse(erase: false);
            else if (_erasing)  PaintAtMouse(erase: true);
        }
    }

    private void PaintAtMouse(bool erase)
    {
        var world = GetGlobalMousePosition();
        int cx    = (int)(world.X / Cell);
        int cy    = (int)(world.Y / Cell);

        if (cx < 0 || cx >= GridW || cy < 0 || cy >= GridH) return;

        var tile = erase ? TileType.Empty : _selTile;

        if (!erase)
        {
            bool isDoor = tile is TileType.Entrance or TileType.Exit;
            if (isDoor && _curFloor != MainFloor) return;

            if (isDoor)  // only one allowed — remove the old one first
            {
                for (int xi = 0; xi < GridW; xi++)
                for (int yi = 0; yi < GridH; yi++)
                    if (_mapData[MainFloor, xi, yi] == tile)
                        _mapData[MainFloor, xi, yi] = TileType.Empty;
            }
        }

        _mapData[_curFloor, cx, cy] = tile;
        QueueRedraw();
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  FLOOR NAVIGATION
    // ══════════════════════════════════════════════════════════════════════════
    private void OnFloorUp()
    {
        if (_curFloor >= Floors - 1) return;
        _curFloor++;
        RefreshFloorLabel();
        QueueRedraw();
    }

    private void OnFloorDown()
    {
        if (_curFloor <= 0) return;
        _curFloor--;
        RefreshFloorLabel();
        QueueRedraw();
    }

    private void RefreshFloorLabel()
    {
        int    d   = _curFloor - MainFloor;
        string tag = d == 0 ? " (main)" : d > 0 ? $" +{d}" : $" {d}";
        _floorLabel.Text = "Floor" + tag;
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  TEST MODE
    // ══════════════════════════════════════════════════════════════════════════
    private void OnStartTest()
    {
        RunValidate(silentPass: true);
        _curFloor = MainFloor;
        QueueRedraw();
        BuildWallColliders();

        _player.GlobalPosition = FindEntranceWorldPos();
        _player.Velocity       = Vector2.Zero;
        _player.Visible        = true;
        _player.ProcessMode    = ProcessModeEnum.Inherit;

        _editorRoot.Visible = false;
        _stopBtn.Visible    = true;
        _isTesting          = true;
    }

    private void OnStopTest()
    {
        _player.Visible     = false;
        _player.ProcessMode = ProcessModeEnum.Disabled;
        _editorRoot.Visible = true;
        _stopBtn.Visible    = false;
        _isTesting          = false;

        _wallBody?.QueueFree();
        _wallBody = null;
    }

    private Vector2 FindEntranceWorldPos()
    {
        for (int x = 0; x < GridW; x++)
        for (int y = 0; y < GridH; y++)
            if (_mapData[MainFloor, x, y] == TileType.Entrance)
                return new Vector2((x + 0.5f) * Cell, (y + 0.5f) * Cell);

        return new Vector2(GridW * Cell * 0.5f, GridH * Cell * 0.5f);  // fallback: center
    }

    private void BuildWallColliders()
    {
        _wallBody?.QueueFree();
        _wallBody = new StaticBody2D();
        AddChild(_wallBody);

        // One collision box per wall tile
        for (int x = 0; x < GridW; x++)
        for (int y = 0; y < GridH; y++)
            if (_mapData[MainFloor, x, y] == TileType.Wall)
                AddRectCollider(new Vector2((x + 0.5f) * Cell, (y + 0.5f) * Cell), new Vector2(Cell, Cell));

        // Invisible border — stops player walking off the grid edge
        float gw = GridW * Cell, gh = GridH * Cell;
        AddRectCollider(new Vector2(gw * 0.5f,  -2f),       new Vector2(gw + 4, 4));
        AddRectCollider(new Vector2(gw * 0.5f,   gh + 2f),  new Vector2(gw + 4, 4));
        AddRectCollider(new Vector2(-2f,         gh * 0.5f), new Vector2(4, gh + 4));
        AddRectCollider(new Vector2(gw + 2f,     gh * 0.5f), new Vector2(4, gh + 4));
    }

    private void AddRectCollider(Vector2 center, Vector2 size)
    {
        var shape = new RectangleShape2D { Size = size };
        var col   = new CollisionShape2D { Shape = shape, Position = center };
        _wallBody!.AddChild(col);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  VALIDATE
    // ══════════════════════════════════════════════════════════════════════════
    private void OnValidate() => RunValidate(silentPass: false);

    private void RunValidate(bool silentPass)
    {
        var result = MapValidator.Validate(_mapData, Floors, GridW, GridH, MainFloor);
        if (result.IsValid)
        {
            if (!silentPass) GD.Print("[Validator] Map is valid and ready!");
        }
        else
            foreach (var err in result.Errors)
                GD.PushError("[Validator] " + err);

        foreach (var warn in result.Warnings)
            GD.PushWarning("[Validator] " + warn);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  SAVE / LOAD
    // ══════════════════════════════════════════════════════════════════════════
    private void OnSave() => MapSerializer.SaveMap(_mapData, Floors, GridW, GridH);

    private void OnLoad()
    {
        var loaded = MapSerializer.LoadMap(Floors, GridW, GridH);
        if (loaded is not null)
        {
            _mapData = loaded;
            QueueRedraw();
        }
    }
}
