using Godot;

/// Autoload singleton — owns all persisted game settings.
/// Applies them to the engine immediately on load and on every change.
public partial class GameSettings : Node
{
    public static GameSettings? Instance { get; private set; }

    const string SavePath = "user://settings.cfg";

    public bool  Fullscreen { get; private set; } = true;
    public bool  PSXEffect  { get; private set; } = true;
    public bool  VSync      { get; private set; } = true;
    public float FOV        { get; private set; } = 90f;

    public override void _Ready()
    {
        Instance = this;
        Load();
        // Defer by one frame so the display server is fully initialised
        // before we change window mode; avoids the 1220×780 sizing glitch.
        CallDeferred(MethodName.Apply);
    }

    // ── Setters (apply immediately + persist) ─────────────────────────────────

    public void SetFullscreen(bool v) { Fullscreen = v; ApplyWindow(); Save(); }
    public void SetPSXEffect(bool v)  { PSXEffect  = v; ApplyPSX();    Save(); }
    public void SetVSync(bool v)      { VSync      = v; ApplyVSync();  Save(); }
    public void SetFOV(float v)       { FOV        = v; Save(); }   // PlayerController reads on next spawn

    // ── Apply ─────────────────────────────────────────────────────────────────

    void Apply()
    {
        ApplyWindow();
        ApplyVSync();
        ApplyPSX();
        // FOV is read by PlayerController at spawn; no runtime push needed here
    }

    void ApplyWindow()
    {
        DisplayServer.WindowSetMode(Fullscreen
            ? DisplayServer.WindowMode.Fullscreen
            : DisplayServer.WindowMode.Windowed);
    }

    void ApplyVSync()
    {
        DisplayServer.WindowSetVsyncMode(VSync
            ? DisplayServer.VSyncMode.Enabled
            : DisplayServer.VSyncMode.Disabled);
    }

    void ApplyPSX()
    {
        // PSXOverlay may not be ready yet on first call from _Ready — null-safe
        PSXOverlay.Instance?.SetUserEnabled(PSXEffect);
    }

    // ── Persistence ───────────────────────────────────────────────────────────

    void Load()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(SavePath) != Error.Ok) return;

        Fullscreen = (bool)cfg.GetValue("display", "fullscreen", Fullscreen);
        VSync      = (bool)cfg.GetValue("display", "vsync",      VSync);
        PSXEffect  = (bool)cfg.GetValue("graphics", "psx_effect", PSXEffect);
        FOV        = (float)cfg.GetValue("graphics", "fov",       FOV);
    }

    void Save()
    {
        var cfg = new ConfigFile();
        cfg.SetValue("display",  "fullscreen", Fullscreen);
        cfg.SetValue("display",  "vsync",      VSync);
        cfg.SetValue("graphics", "psx_effect", PSXEffect);
        cfg.SetValue("graphics", "fov",        FOV);
        cfg.Save(SavePath);
    }
}
