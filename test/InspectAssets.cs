using Godot;

/// Headless inspection — prints bone names, animation clips, and mesh node names
/// from the new character, UAL2 animation library, and sword GLBs.
/// Run: godot --headless --script test/InspectAssets.cs
public partial class InspectAssets : SceneTree
{
    public override void _Initialize()
    {
        Inspect("CHARACTER", "res://playerModels/armored_guard_knight_rig.glb");
        Inspect("UAL1 ANIMATIONS", "res://playerModels/animations/Universal Animation Library[Standard]/Unreal-Godot/UAL1_Standard.glb");
        Inspect("UAL2 ANIMATIONS", "res://playerModels/animations/Universal Animation Library 2[Standard]/Unreal-Godot/UAL2_Standard.glb");
        Inspect("DMC SWORDS", "res://assets/all_dmc2_swords_remade.glb");
        Inspect("ZWEIHANDER", "res://assets/zweihander.glb");
        Quit();
    }

    static void Inspect(string label, string path)
    {
        GD.Print($"\n══ {label} ══  {path}");
        var packed = GD.Load<PackedScene>(path);
        if (packed == null) { GD.Print("  ERROR: could not load"); return; }
        var root = packed.Instantiate<Node>();

        PrintTree(root, "  ");

        // Skeleton bones
        foreach (var skel in FindAll<Skeleton3D>(root))
        {
            GD.Print($"  [Skeleton3D] {skel.GetPath()} — {skel.GetBoneCount()} bones:");
            for (int i = 0; i < skel.GetBoneCount(); i++)
                GD.Print($"    [{i}] {skel.GetBoneName(i)}");
        }

        // Animation clips
        foreach (var ap in FindAll<AnimationPlayer>(root))
        {
            GD.Print($"  [AnimationPlayer] {ap.GetPath()} — {ap.GetAnimationList().Length} clips:");
            foreach (var name in ap.GetAnimationList())
                GD.Print($"    \"{name}\"");
        }

        root.Free();
    }

    static void PrintTree(Node node, string indent)
    {
        GD.Print($"{indent}{node.GetType().Name}  \"{node.Name}\"");
        foreach (Node child in node.GetChildren())
            PrintTree(child, indent + "  ");
    }

    static System.Collections.Generic.List<T> FindAll<T>(Node root) where T : Node
    {
        var list = new System.Collections.Generic.List<T>();
        Collect(root, list);
        return list;
    }

    static void Collect<T>(Node node, System.Collections.Generic.List<T> list) where T : Node
    {
        if (node is T t) list.Add(t);
        foreach (Node child in node.GetChildren())
            Collect(child, list);
    }
}
