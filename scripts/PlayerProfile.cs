using Godot;
using System.Text.Json;

/// Persists player name and gold to user://player_profile.json.
/// Call Load() on startup, Save() after any change to gold or name.
public static class PlayerProfile
{
    const string FilePath = "user://player_profile.json";

    public static void Load()
    {
        if (!FileAccess.FileExists(FilePath)) return;
        using var file = FileAccess.Open(FilePath, FileAccess.ModeFlags.Read);
        if (file == null) return;
        try
        {
            using var doc = JsonDocument.Parse(file.GetAsText());
            var root = doc.RootElement;
            if (root.TryGetProperty("name", out var n))
            {
                string name = n.GetString() ?? "";
                if (!string.IsNullOrEmpty(name))
                    OnlineGameState.PlayerName = name;
            }
            if (root.TryGetProperty("gold", out var g))
                OnlineGameState.Gold = g.GetInt32();
        }
        catch { }
    }

    public static void Save()
    {
        string json = JsonSerializer.Serialize(new
        {
            name = OnlineGameState.PlayerName,
            gold = OnlineGameState.Gold,
        });
        using var file = FileAccess.Open(FilePath, FileAccess.ModeFlags.Write);
        file?.StoreString(json);
    }
}
