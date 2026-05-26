using Godot;

/// Diagnostic harness: spawns the player, lets PlayerAnimator.Init() run,
/// prints all [PlayerAnimator] output, then quits after 5 frames.
public partial class TestAnimDiag : Node3D
{
    int _frame = 0;

    public override void _Ready()
    {
        var floor = new StaticBody3D();
        floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(40f, 0.4f, 40f) } });
        floor.Position = new Vector3(0f, -0.2f, 0f);
        AddChild(floor);
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-45f, 0f, 0f) });

        PlayerController.Spawn(this, new Vector3(0f, 1f, 0f));
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    public override void _Process(double delta)
    {
        if (++_frame >= 5)
            GetTree().Quit();
    }
}
