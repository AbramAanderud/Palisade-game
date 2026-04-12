using Godot;

public partial class Player : CharacterBody2D
{
    private const float Speed  = 220f;
    private const float Radius = 20f;

    private static readonly Color BodyColor = new(0.2f, 0.5f, 1.0f);
    private static readonly Color EdgeColor = Colors.White;

    public override void _Ready()
    {
        // Top-down — no gravity / floor normal needed
        MotionMode = MotionModeEnum.Floating;

        // Build collision shape in code so the .tscn stays minimal
        var shape = new CircleShape2D { Radius = Radius };
        AddChild(new CollisionShape2D { Shape = shape });
    }

    public override void _PhysicsProcess(double _delta)
    {
        var dir = Vector2.Zero;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) dir.X += 1f;
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left))  dir.X -= 1f;
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down))  dir.Y += 1f;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up))    dir.Y -= 1f;

        Velocity = dir.Normalized() * Speed;
        MoveAndSlide();
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawCircle(Vector2.Zero, Radius, BodyColor);
        DrawArc(Vector2.Zero, Radius, 0f, Mathf.Tau, 32, EdgeColor, 2f);
    }
}
