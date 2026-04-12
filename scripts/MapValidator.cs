using System.Collections.Generic;
using static TileData;

public static class MapValidator
{
    public struct ValidationResult
    {
        public bool         IsValid;
        public List<string> Errors;
        public List<string> Warnings;
    }

    public static ValidationResult Validate(
        TileType[,,] mapData,
        int floors, int width, int height, int mainFloor)
    {
        var result = new ValidationResult
        {
            IsValid  = true,
            Errors   = new List<string>(),
            Warnings = new List<string>(),
        };

        // ── Entrance / Exit must each appear exactly once on the main floor ──
        int entranceCount = 0, exitCount = 0;
        for (int x = 0; x < width;  x++)
        for (int y = 0; y < height; y++)
        {
            if (mapData[mainFloor, x, y] == TileType.Entrance) entranceCount++;
            if (mapData[mainFloor, x, y] == TileType.Exit)     exitCount++;
        }
        if (entranceCount != 1) Fail(ref result, $"Main floor needs exactly 1 Entrance (found {entranceCount}).");
        if (exitCount     != 1) Fail(ref result, $"Main floor needs exactly 1 Exit (found {exitCount}).");

        // ── No entrance / exit on non-main floors ────────────────────────────
        for (int f = 0; f < floors; f++)
        {
            if (f == mainFloor) continue;
            for (int x = 0; x < width;  x++)
            for (int y = 0; y < height; y++)
            {
                var t = mapData[f, x, y];
                if (t == TileType.Entrance || t == TileType.Exit)
                    Fail(ref result, $"Entrance/Exit on non-main floor {f - mainFloor} at ({x},{y}).");
            }
        }

        // ── Stair pairing: StairsUp on floor N ↔ StairsDown on floor N+1 ────
        for (int f = 0; f < floors - 1; f++)
        for (int x = 0; x < width;      x++)
        for (int y = 0; y < height;     y++)
        {
            bool hasUp   = mapData[f,     x, y] == TileType.StairsUp;
            bool hasDown = mapData[f + 1, x, y] == TileType.StairsDown;
            if (hasUp   && !hasDown) Fail(ref result, $"StairsUp at ({x},{y}) floor {f - mainFloor} has no StairsDown above.");
            if (hasDown && !hasUp)   Fail(ref result, $"StairsDown at ({x},{y}) floor {f + 1 - mainFloor} has no StairsUp below.");
        }

        // ── Warn if main floor is nearly empty ───────────────────────────────
        int walkable = 0;
        for (int x = 0; x < width;  x++)
        for (int y = 0; y < height; y++)
        {
            var t = mapData[mainFloor, x, y];
            if (t == TileType.Floor || t == TileType.Entrance || t == TileType.Exit)
                walkable++;
        }
        if (walkable < 4)
            result.Warnings.Add("Main floor has very few walkable tiles — add more.");

        return result;
    }

    private static void Fail(ref ValidationResult r, string msg)
    {
        r.IsValid = false;
        r.Errors.Add(msg);
    }
}
