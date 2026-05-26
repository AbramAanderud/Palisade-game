using Godot;

/// HUD: red HP bar (top-centre) + self-updating stamina donut ring (bottom-centre).
/// Attached as child of PlayerController; siblings are found by type, not name.
public partial class PlayerHUD : CanvasLayer
{
    const float BarW = 360f;
    const float BarH = 20f;
    const float PadY = 18f;

    ColorRect     _hpFill = null!;
    PlayerHealth? _health;

    public override void _Ready()
    {
        Layer = 10;

        var root = new Control { Name = "HUDRoot" };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        // ── HP bar — top-centre, red, no outline ─────────────────────────────
        // Dim red track shows the "empty" portion
        var hpBack = new ColorRect { Color = new Color(0.7f, 0.05f, 0.05f, 0.25f) };
        hpBack.AnchorLeft     = 0.5f;
        hpBack.AnchorRight    = 0.5f;
        hpBack.AnchorTop      = 0f;
        hpBack.AnchorBottom   = 0f;
        hpBack.GrowHorizontal = Control.GrowDirection.Both;
        hpBack.OffsetLeft     = -BarW / 2f;
        hpBack.OffsetRight    =  BarW / 2f;
        hpBack.OffsetTop      =  PadY;
        hpBack.OffsetBottom   =  PadY + BarH;
        root.AddChild(hpBack);

        _hpFill = new ColorRect
        {
            Position = Vector2.Zero,
            Size     = new Vector2(BarW, BarH),
            Color    = new Color(0.88f, 0.12f, 0.12f, 1f),
        };
        hpBack.AddChild(_hpFill);

        // ── Stamina ring — self-contained, positioned at bottom-centre ────────
        var ring = new StaminaRing();
        ring.AnchorLeft     = 0.5f;
        ring.AnchorRight    = 0.5f;
        ring.AnchorTop      = 1.0f;
        ring.AnchorBottom   = 1.0f;
        ring.GrowHorizontal = Control.GrowDirection.Both;
        ring.GrowVertical   = Control.GrowDirection.Begin;
        ring.OffsetLeft     = -44f;
        ring.OffsetRight    =  44f;
        ring.OffsetTop      = -126f;
        ring.OffsetBottom   =  -38f;
        root.AddChild(ring);
    }

    public override void _Process(double delta)
    {
        // Resolve PlayerHealth by type — immune to node-name changes
        if (_health == null)
        {
            var parent = GetParent();
            if (parent != null)
                foreach (var child in parent.GetChildren())
                    if (child is PlayerHealth ph) { _health = ph; break; }
        }

        if (_health != null)
        {
            float frac = Mathf.Clamp(_health.CurrentHealth / PlayerHealth.MaxHealth, 0f, 1f);
            _hpFill.Size = new Vector2(BarW * frac, BarH);
        }
    }
}

/// Donut-style stamina ring.  Finds its own SwordCombat by walking up the tree.
/// Redraws itself every frame via its own _Process — no external push needed.
public partial class StaminaRing : Control
{
    const float Radius    = 34f;
    const float Thickness =  9f;
    const int   Points    = 64;

    SwordCombat? _combat;

    public override void _Process(double delta)
    {
        // Walk: StaminaRing → HUDRoot (Control) → PlayerHUD (CanvasLayer) → PlayerController
        if (_combat == null)
        {
            var playerCtrl = GetParent()?.GetParent()?.GetParent();
            if (playerCtrl != null)
                foreach (var child in playerCtrl.GetChildren())
                    if (child is SwordCombat sc) { _combat = sc; break; }
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        float used = _combat != null
            ? 1f - Mathf.Clamp(_combat.Stamina / _combat.MaxStam, 0f, 1f)
            : 0f;
        bool exhausted = _combat?.IsExhausted ?? false;

        // Use a fixed centre so the ring draws correctly even if Size hasn't
        // been resolved yet by the layout engine.
        var center = new Vector2(44f, 44f);

        // Faint full track — always visible as a hint of the ring bounds
        DrawArc(center, Radius,
                -Mathf.Pi / 2f, -Mathf.Pi / 2f + Mathf.Tau,
                Points, new Color(1f, 1f, 1f, 0.10f), Thickness, true);

        if (used < 0.015f) return;

        float endAngle = -Mathf.Pi / 2f + Mathf.Tau * Mathf.Clamp(used, 0f, 1f);
        Color col = exhausted
            ? new Color(1.0f, 0.15f, 0.10f, 0.90f)   // red flash when locked out
            : new Color(0.92f, 0.92f, 0.92f, 0.58f);  // translucent grey normally

        DrawArc(center, Radius,
                -Mathf.Pi / 2f, endAngle,
                Points, col, Thickness, true);
    }
}
