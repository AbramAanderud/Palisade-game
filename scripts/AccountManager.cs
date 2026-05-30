using Godot;
using System.Collections.Generic;
using System.Text.Json;

/// Per-username account management. Each account has its own folder under
/// user://accounts/{name}/ containing profile.json + maze_0..4.json.
///
/// Storage layout:
///   user://accounts/{name}/profile.json   { name, gold }
///   user://accounts/{name}/maze_0.json    … maze_4.json
///   user://active_account.txt             "{name}"
///
/// Gold acts as rank: every new account starts at 1000 and only ever grows.
/// Each maze independently budgets up to current Gold for piece spending.
public static class AccountManager
{
    public const int    StarterRank = 1000;
    public const string DefaultName = "default";

    const string AccountsRoot = "user://accounts/";
    const string ActiveFile   = "user://active_account.txt";

    static string _active  = "";
    static bool   _loaded  = false;

    public static string ActiveAccount
    {
        get { EnsureLoaded(); return _active; }
    }

    public static string AccountDir(string name)  => $"{AccountsRoot}{name}/";
    public static string ProfilePath()            => AccountDir(ActiveAccount) + "profile.json";
    public static string MazeSlotPath(int slot)   => AccountDir(ActiveAccount) + $"maze_{slot}.json";

    public static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;

        // First run: if accounts root doesn't exist, do the one-time migration.
        if (!DirAccess.DirExistsAbsolute(ProjectSettings.GlobalizePath(AccountsRoot)))
        {
            CreateAccountsRoot();
            MigrateLegacyData();
        }

        // CLI override for local two-window testing:  --account <name>
        // Does NOT update active_account.txt — it's a per-session override only.
        string? cliAccount = ParseCliAccount();
        if (cliAccount != null)
        {
            _active = cliAccount;
            EnsureAccountDir(_active);
            GD.Print($"[AM] CLI override → active account = {_active}");
            return;
        }

        if (FileAccess.FileExists(ActiveFile))
        {
            using var f = FileAccess.Open(ActiveFile, FileAccess.ModeFlags.Read);
            _active = f?.GetAsText().Trim() ?? DefaultName;
        }
        else
        {
            _active = DefaultName;
            SaveActiveMarker();
        }

        EnsureAccountDir(_active);
    }

    static string? ParseCliAccount()
    {
        var args = OS.GetCmdlineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--account")
            {
                string raw = args[i + 1] ?? "";
                string clean = raw.Trim();
                if (string.IsNullOrEmpty(clean)) return null;
                return clean;
            }
        }
        return null;
    }

    static void CreateAccountsRoot()
    {
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(AccountsRoot));
    }

    static void EnsureAccountDir(string name)
    {
        string path = AccountDir(name);
        if (!DirAccess.DirExistsAbsolute(ProjectSettings.GlobalizePath(path)))
            DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(path));
    }

    /// One-time migration: pull user://player_profile.json and user://maze_slot_0..4.json
    /// into user://accounts/default/. Bumps gold to at least StarterRank.
    static void MigrateLegacyData()
    {
        string defaultDir = AccountDir(DefaultName);
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(defaultDir));

        string oldProfile = "user://player_profile.json";
        int    gold       = StarterRank;
        string name       = DefaultName;

        if (FileAccess.FileExists(oldProfile))
        {
            using var src = FileAccess.Open(oldProfile, FileAccess.ModeFlags.Read);
            try
            {
                using var doc = JsonDocument.Parse(src?.GetAsText() ?? "{}");
                var root = doc.RootElement;
                if (root.TryGetProperty("name", out var n))
                {
                    string n2 = n.GetString() ?? "";
                    if (!string.IsNullOrEmpty(n2)) name = n2;
                }
                if (root.TryGetProperty("gold", out var g))
                    gold = System.Math.Max(StarterRank, g.GetInt32());
            }
            catch { }
            GD.Print($"[AM] Migrated legacy profile → default account ({name}, {gold}g)");
        }

        string profileJson = JsonSerializer.Serialize(new { name, gold });
        using (var dst = FileAccess.Open(defaultDir + "profile.json", FileAccess.ModeFlags.Write))
            dst?.StoreString(profileJson);

        for (int i = 0; i < 5; i++)
        {
            string oldPath = $"user://maze_slot_{i}.json";
            if (!FileAccess.FileExists(oldPath)) continue;
            using var src = FileAccess.Open(oldPath, FileAccess.ModeFlags.Read);
            string content = src?.GetAsText() ?? "";
            using var dst  = FileAccess.Open(defaultDir + $"maze_{i}.json", FileAccess.ModeFlags.Write);
            dst?.StoreString(content);
            GD.Print($"[AM] Migrated maze slot {i} → default account");
        }
    }

    public static List<string> ListAccounts()
    {
        EnsureLoaded();
        var list = new List<string>();
        using var dir = DirAccess.Open(AccountsRoot);
        if (dir == null) return list;
        dir.ListDirBegin();
        string entry;
        while ((entry = dir.GetNext()) != "")
        {
            if (dir.CurrentIsDir() && !entry.StartsWith(".")) list.Add(entry);
        }
        return list;
    }

    /// Switch to (and create if missing) the named account. Saves the active marker.
    public static void SwitchOrCreate(string name)
    {
        EnsureLoaded();
        EnsureAccountDir(name);
        _active = name;
        SaveActiveMarker();
    }

    static void SaveActiveMarker()
    {
        using var f = FileAccess.Open(ActiveFile, FileAccess.ModeFlags.Write);
        f?.StoreString(_active);
    }
}
