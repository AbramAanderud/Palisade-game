using Godot;

/// Autoload singleton — drives PSX rendering only while inside gameplay scenes.
/// In DungeonGame / DungeonArena:
///   • 3D renders at 480×270 (0.375 × 1280×720) and bilinear-upscales to full res
///   • Bayer dither grain overlay is shown
/// In all other scenes (map editor, lobby, etc.) both effects are off.
public partial class PSXOverlay : CanvasLayer
{
    public static PSXOverlay? Instance { get; private set; }

    const float PsxScale  = 0.375f; // 480×270 on a 1280×720 viewport
    const float FullScale = 1.0f;

    ColorRect? _dither;
    bool       _lastApplied  = false; // tracks the actual last-applied state
    bool       _userEnabled  = true;

    public override void _Ready()
    {
        Instance = this;
        Layer = 100;

        var shader = GD.Load<Shader>("res://assets/shaders/psx_dither.gdshader");
        if (shader != null)
        {
            var mat = new ShaderMaterial { Shader = shader };
            _dither = new ColorRect { Name = "DitherRect", Visible = false };
            _dither.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            _dither.Material    = mat;
            _dither.MouseFilter = Control.MouseFilterEnum.Ignore;
            AddChild(_dither);
        }
    }

    public void SetUserEnabled(bool enabled)
    {
        _userEnabled = enabled;
        ApplyState(); // apply immediately without waiting for next frame
    }

    public override void _Process(double delta)
    {
        var scene    = GetTree().CurrentScene;
        bool gameplay = scene != null &&
                        (scene.SceneFilePath.Contains("DungeonGame") ||
                         scene.SceneFilePath.Contains("DungeonArena") ||
                         scene.SceneFilePath.Contains("OnlineDungeonArena"));
        bool want = gameplay && _userEnabled;
        if (want == _lastApplied) return;
        ApplyState(want);
    }

    void ApplyState()
    {
        var scene    = GetTree().CurrentScene;
        bool gameplay = scene != null &&
                        (scene.SceneFilePath.Contains("DungeonGame") ||
                         scene.SceneFilePath.Contains("DungeonArena") ||
                         scene.SceneFilePath.Contains("OnlineDungeonArena"));
        ApplyState(gameplay && _userEnabled);
    }

    void ApplyState(bool applyPsx)
    {
        var vp = GetViewport();
        if (applyPsx)
        {
            vp.Scaling3DMode  = Viewport.Scaling3DModeEnum.Bilinear;
            vp.Scaling3DScale = PsxScale;
        }
        else
        {
            vp.Scaling3DScale = FullScale;
        }
        if (_dither != null) _dither.Visible = applyPsx;
        _lastApplied = applyPsx;
    }
}
