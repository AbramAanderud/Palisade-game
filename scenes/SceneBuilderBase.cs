using Godot;

/// Shared base for all headless scene builder scripts.
public partial class SceneBuilderBase : SceneTree
{
    protected void SetOwnerOnNewNodes(Node node, Node sceneOwner)
    {
        foreach (var child in node.GetChildren())
        {
            child.Owner = sceneOwner;
            if (string.IsNullOrEmpty(child.SceneFilePath))
                SetOwnerOnNewNodes(child, sceneOwner);
        }
    }

    protected int CountNodes(Node node)
    {
        int total = 1;
        foreach (var child in node.GetChildren())
            total += CountNodes(child);
        return total;
    }

    protected bool ValidatePackedScene(PackedScene packed, int expectedCount, string scenePath)
    {
        var testInstance = packed.Instantiate();
        int actual = CountNodes(testInstance);
        testInstance.Free();
        if (actual < expectedCount)
        {
            GD.PushError($"Pack validation failed for {scenePath}: expected {expectedCount} nodes, got {actual}");
            return false;
        }
        return true;
    }

    protected void PackAndSave(Node rootNode, string outputPath)
    {
        SetOwnerOnNewNodes(rootNode, rootNode);
        int count = CountNodes(rootNode);

        var packed = new PackedScene();
        var err = packed.Pack(rootNode);
        if (err != Error.Ok) { GD.PushError($"Pack failed: {err}"); Quit(1); return; }
        if (!ValidatePackedScene(packed, count, outputPath)) { Quit(1); return; }

        err = ResourceSaver.Save(packed, outputPath);
        if (err != Error.Ok) { GD.PushError($"Save failed: {err}"); Quit(1); return; }

        GD.Print($"BUILT: {count} nodes → {outputPath}");
        Quit(0);
    }
}
