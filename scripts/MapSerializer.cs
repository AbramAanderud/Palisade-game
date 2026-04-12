using Godot;
using Godot.Collections;
using System;
using static TileData;

public static class MapSerializer
{
    private const string SavePath = "user://labyrinth_map.json";

    public static void SaveMap(TileType[,,] mapData, int floors, int w, int h)
    {
        var flat = new Array();
        for (int f = 0; f < floors; f++)
        for (int x = 0; x < w;      x++)
        for (int y = 0; y < h;      y++)
            flat.Add((int)mapData[f, x, y]);

        var dict = new Dictionary
        {
            ["floors"] = floors,
            ["width"]  = w,
            ["height"] = h,
            ["tiles"]  = flat,
        };

        using var file = FileAccess.Open(SavePath, FileAccess.ModeFlags.Write);
        if (file is null) { GD.PushError($"[Save] Cannot open {SavePath} for writing."); return; }

        file.StoreString(Json.Stringify(dict, "\t"));
        GD.Print($"[Save] Map saved → {ProjectSettings.GlobalizePath(SavePath)}");
    }

    public static TileType[,,]? LoadMap(int floors, int w, int h)
    {
        if (!FileAccess.FileExists(SavePath)) { GD.PushWarning("[Load] No save file found."); return null; }

        using var file = FileAccess.Open(SavePath, FileAccess.ModeFlags.Read);
        if (file is null) { GD.PushError("[Load] Cannot open save file."); return null; }

        var json = new Json();
        if (json.Parse(file.GetAsText()) != Error.Ok)
        {
            GD.PushError($"[Load] Parse error: {json.GetErrorMessage()}");
            return null;
        }

        var data = json.Data.AsGodotDictionary();
        int svf  = data["floors"].As<int>();
        int svw  = data["width"].As<int>();
        int svh  = data["height"].As<int>();
        var flat = data["tiles"].AsGodotArray();

        var result = new TileType[floors, w, h];
        int lf = Math.Min(svf, floors);
        int lw = Math.Min(svw, w);
        int lh = Math.Min(svh, h);

        for (int f = 0; f < lf; f++)
        for (int x = 0; x < lw; x++)
        for (int y = 0; y < lh; y++)
        {
            int idx = f * svw * svh + x * svh + y;
            if (idx < flat.Count)
                result[f, x, y] = (TileType)flat[idx].As<int>();
        }

        GD.Print("[Load] Map loaded successfully.");
        return result;
    }
}
