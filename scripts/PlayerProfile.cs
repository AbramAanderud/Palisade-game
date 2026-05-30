using Godot;
using System.Text.Json;

/// Persists name + gold to the currently active account.
/// Storage path is resolved by AccountManager — typically:
///     user://accounts/{active}/profile.json
public static class PlayerProfile
{
    public static void Load()
    {
        AccountManager.EnsureLoaded();
        string path = AccountManager.ProfilePath();

        if (!FileAccess.FileExists(path))
        {
            // Fresh account — seed with starter rank and the account name as display name.
            OnlineGameState.PlayerName = AccountManager.ActiveAccount;
            OnlineGameState.Gold       = AccountManager.StarterRank;
            Save();
            return;
        }

        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
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
        AccountManager.EnsureLoaded();
        string json = JsonSerializer.Serialize(new
        {
            name = OnlineGameState.PlayerName,
            gold = OnlineGameState.Gold,
        });
        using var file = FileAccess.Open(AccountManager.ProfilePath(), FileAccess.ModeFlags.Write);
        file?.StoreString(json);
    }
}
