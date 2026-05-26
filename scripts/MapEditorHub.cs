using Godot;

/// Tab wrapper that hosts the new 3D editor ("Maze") and provides a button
/// to launch the legacy 2D editor ("Maze Testing") as its own scene.
public partial class MapEditorHub : Control
{
    const int   TabH = 46;
    FontFile?   _font;
    MazeEditor3D? _editor;
    Button        _tabMaze = null!;
    Button        _tabTest = null!;

    public override void _Ready()
    {
        _font = GD.Load<FontFile>("res://assets/fonts/Agmena Pro Book.ttf");
        SetAnchorsPreset(LayoutPreset.FullRect);

        // Full-screen black BG
        var bg = new ColorRect { Color = new Color(0f, 0f, 0f) };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        // Tab bar strip
        var bar = new ColorRect { Color = new Color(0.07f, 0.07f, 0.07f) };
        bar.SetAnchorsPreset(LayoutPreset.TopWide);
        bar.Size = new Vector2(0, TabH);
        AddChild(bar);

        var hbox = new HBoxContainer();
        hbox.SetAnchorsPreset(LayoutPreset.TopLeft);
        hbox.Size = new Vector2(500, TabH);
        AddChild(hbox);

        _tabMaze = MakeTab("Maze");
        _tabTest = MakeTab("Maze Testing");
        hbox.AddChild(_tabMaze);
        hbox.AddChild(_tabTest);

        _tabMaze.Pressed += SelectMaze;
        _tabTest.Pressed += SelectTesting;

        // 3D editor fills below tab bar
        _editor = new MazeEditor3D();
        _editor.SetAnchorsPreset(LayoutPreset.FullRect);
        _editor.OffsetTop = TabH;
        AddChild(_editor);

        HighlightTab(0);
    }

    public override void _Input(InputEvent ev)
    {
        // Nothing — editor handles its own input
    }

    void SelectMaze()
    {
        if (_editor != null) _editor.Visible = true;
        HighlightTab(0);
    }

    void SelectTesting()
    {
        // Legacy editor is a standalone scene; save current work first
        _editor?.SaveCurrentSlot();
        GetTree().ChangeSceneToFile("res://scenes/MapEditor.tscn");
    }

    void HighlightTab(int idx)
    {
        StyleTab(_tabMaze, idx == 0);
        StyleTab(_tabTest, idx == 1);
    }

    Button MakeTab(string text)
    {
        var b = new Button { Text = text };
        b.CustomMinimumSize = new Vector2(160, TabH);
        if (_font != null)
        {
            b.AddThemeFontOverride("font", _font);
            b.AddThemeFontSizeOverride("font_size", 14);
        }
        return b;
    }

    void StyleTab(Button b, bool active)
    {
        var col  = active ? Colors.White : new Color(0.55f, 0.55f, 0.55f);
        var bgCol = active ? new Color(0.12f, 0.12f, 0.12f) : new Color(0.07f, 0.07f, 0.07f);
        b.AddThemeColorOverride("font_color",       col);
        b.AddThemeColorOverride("font_color_hover", col);
        var sb = new StyleBoxFlat
        {
            BgColor           = bgCol,
            BorderColor       = active ? Colors.White : new Color(0.2f, 0.2f, 0.2f),
            BorderWidthBottom = active ? 2 : 0,
        };
        b.AddThemeStyleboxOverride("normal",   sb);
        b.AddThemeStyleboxOverride("hover",    sb);
        b.AddThemeStyleboxOverride("pressed",  sb);
    }
}
