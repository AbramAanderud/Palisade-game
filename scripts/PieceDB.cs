using System.Collections.Generic;
using Godot;

/// res://scripts/PieceDB.cs — Piece type definitions, openings, costs, and colors.
public enum PieceType { Start, Exit, Straight, LHall, THall, Stairs, StairsUp, StairsDown }

[System.Flags]
public enum Dir { None = 0, N = 1, E = 2, S = 4, W = 8 }

public static class PieceDB
{
    // Openings at rotation = 0.
    // Stairs (legacy): S on floor F, N connects to floor F+1.
    // StairsUp: N connects to floor F+1, S stays on floor F.  Fixed – no rotation.
    // StairsDown: N stays on floor F, S connects to floor F-1.  Fixed – no rotation.
    static readonly Dictionary<PieceType, Dir> BaseOpenings = new()
    {
        [PieceType.Start]      = Dir.S,
        [PieceType.Exit]       = Dir.N,
        [PieceType.Straight]   = Dir.N | Dir.S,
        [PieceType.LHall]      = Dir.N | Dir.E,
        [PieceType.THall]      = Dir.N | Dir.E | Dir.S,
        [PieceType.Stairs]     = Dir.S | Dir.N,   // legacy
        [PieceType.StairsUp]   = Dir.N | Dir.S,
        [PieceType.StairsDown] = Dir.N | Dir.S,
    };

    public static Dir GetOpenings(PieceType type, int rotation)
    {
        Dir d = BaseOpenings[type];
        // StairsUp / StairsDown are always rotation-0 — no rotation applied.
        if (type == PieceType.StairsUp || type == PieceType.StairsDown) return d;
        int r = ((rotation % 4) + 4) % 4;
        for (int i = 0; i < r; i++) d = RotateCW(d);
        return d;
    }

    /// Returns true for any stair piece type.
    public static bool IsStair(PieceType t)
        => t == PieceType.Stairs || t == PieceType.StairsUp || t == PieceType.StairsDown;

    /// Floor delta: +1 for upward stairs, -1 for StairsDown.
    public static int StairFloorDelta(PieceType t)
        => t == PieceType.StairsDown ? -1 : +1;

    /// Returns the face of a stair piece that crosses to a different floor.
    /// For Stairs/StairsUp this is the "up" face (toward floor+1).
    /// For StairsDown this is the "down" face (toward floor-1) = Dir.S.
    public static Dir GetStairCrossDir(PieceType type, int rotation = 0)
    {
        if (type == PieceType.StairsDown) return Dir.S;
        return GetStairUpDir(rotation);
    }

    /// Returns the opening direction of a Stairs/StairsUp piece that exits to floor+1.
    /// At rotation 0 that is N; rotates clockwise with the piece.
    /// For StairsDown always returns Dir.S (the face that exits to floor-1).
    public static Dir GetStairUpDir(int rotation)
    {
        Dir d = Dir.N;
        int r = ((rotation % 4) + 4) % 4;
        for (int i = 0; i < r; i++) d = RotateCW(d);
        return d;
    }

    static Dir RotateCW(Dir d)
    {
        Dir result = Dir.None;
        if ((d & Dir.N) != 0) result |= Dir.E;
        if ((d & Dir.E) != 0) result |= Dir.S;
        if ((d & Dir.S) != 0) result |= Dir.W;
        if ((d & Dir.W) != 0) result |= Dir.N;
        return result;
    }

    public static readonly Dictionary<PieceType, Color> Colors = new()
    {
        [PieceType.Start]      = new Color(0.20f, 0.75f, 0.20f),
        [PieceType.Exit]       = new Color(0.70f, 0.20f, 0.80f),
        [PieceType.Straight]   = new Color(0.55f, 0.55f, 0.60f),
        [PieceType.LHall]      = new Color(0.50f, 0.55f, 0.65f),
        [PieceType.THall]      = new Color(0.40f, 0.45f, 0.75f),
        [PieceType.Stairs]     = new Color(0.80f, 0.60f, 0.20f),  // legacy
        [PieceType.StairsUp]   = new Color(0.85f, 0.55f, 0.10f),  // warm orange-amber
        [PieceType.StairsDown] = new Color(0.60f, 0.40f, 0.10f),  // darker amber-brown
    };

    public static readonly Dictionary<PieceType, string> Labels = new()
    {
        [PieceType.Start]      = "Start",
        [PieceType.Exit]       = "Exit",
        [PieceType.Straight]   = "Straight",
        [PieceType.LHall]      = "L-Hall",
        [PieceType.THall]      = "T-Junction",
        [PieceType.Stairs]     = "Stairs (legacy)",
        [PieceType.StairsUp]   = "Stairs Up",
        [PieceType.StairsDown] = "Stairs Down",
    };

    public static readonly Dictionary<PieceType, string> ShortLabels = new()
    {
        [PieceType.Start]      = "S",
        [PieceType.Exit]       = "X",
        [PieceType.Straight]   = "I",
        [PieceType.LHall]      = "L",
        [PieceType.THall]      = "T",
        [PieceType.Stairs]     = "UP",
        [PieceType.StairsUp]   = "UP",
        [PieceType.StairsDown] = "DN",
    };

    public static readonly Dictionary<PieceType, int> GoldCosts = new()
    {
        [PieceType.Start]      = 0,
        [PieceType.Exit]       = 0,
        [PieceType.Straight]   = 0,
        [PieceType.LHall]      = 0,
        [PieceType.THall]      = 25,
        [PieceType.Stairs]     = 50,
        [PieceType.StairsUp]   = 50,
        [PieceType.StairsDown] = 50,
    };
}
