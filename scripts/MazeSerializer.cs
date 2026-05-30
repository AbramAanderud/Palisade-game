using Godot;
using System.Text.Json;

/// res://scripts/MazeSerializer.cs — Multi-slot save/load for MazeData.
/// Paths are resolved per-account via AccountManager, e.g. user://accounts/alice/maze_2.json.
public static class MazeSerializer
{
    public const int SlotCount = 5;

    static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    static string SlotPath(int slot) => AccountManager.MazeSlotPath(slot);

    public static void Save(int slot, MazeData data)
    {
        data.GoldSpent = 0;
        if (data.Pieces != null)
            foreach (var p in data.Pieces)
                data.GoldSpent += PieceDB.GoldCosts[p.Type];

        string json = JsonSerializer.Serialize(data, JsonOpts);
        using var file = FileAccess.Open(SlotPath(slot), FileAccess.ModeFlags.Write);
        file?.StoreString(json);
    }

    public static MazeData? Load(int slot)
    {
        string path = SlotPath(slot);
        if (!FileAccess.FileExists(path)) return null;
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file == null) return null;
        string json = file.GetAsText();
        try
        {
            var data = JsonSerializer.Deserialize<MazeData>(json, JsonOpts);
            data ??= new MazeData();
            data.Pieces ??= new();
            return data;
        }
        catch
        {
            GD.PushError($"[MazeSerializer] Failed to parse slot {slot}");
            return null;
        }
    }

    public static bool Exists(int slot) => FileAccess.FileExists(SlotPath(slot));
}
