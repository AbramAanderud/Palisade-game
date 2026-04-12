using System.Collections.Generic;
using Godot;

/// res://scripts/PieceDB.cs — Piece type definitions, openings, costs, and colors.
public enum PieceType { Start, Exit, Straight, LHall, THall, Stairs }

[System.Flags]
public enum Dir { None = 0, N = 1, E = 2, S = 4, W = 8 }

public static class PieceDB
{
    // Openings at rotation = 0.  Stairs: S on floor F, N connects to floor F+1.
    static readonly Dictionary<PieceType, Dir> BaseOpenings = new()
    {
        [PieceType.Start]    = Dir.S,
        [PieceType.Exit]     = Dir.N,
        [PieceType.Straight] = Dir.N | Dir.S,
        [PieceType.LHall]    = Dir.N | Dir.E,
        [PieceType.THall]    = Dir.N | Dir.E | Dir.S,
        [PieceType.Stairs]   = Dir.S | Dir.N,
    };

    public static Dir GetOpenings(PieceType type, int rotation)
    {
        Dir d = BaseOpenings[type];
        int r = ((rotation % 4) + 4) % 4;
        for (int i = 0; i < r; i++) d = RotateCW(d);
        return d;
    }

    /// <summary>
    /// Returns the opening direction of a Stairs piece that exits to floor+1.
    /// At rotation 0 that is N; rotates clockwise with the piece.
    /// </summary>
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
        [PieceType.Start]    = new Color(0.20f, 0.75f, 0.20f),
        [PieceType.Exit]     = new Color(0.70f, 0.20f, 0.80f),
        [PieceType.Straight] = new Color(0.55f, 0.55f, 0.60f),
        [PieceType.LHall]    = new Color(0.50f, 0.55f, 0.65f),
        [PieceType.THall]    = new Color(0.40f, 0.45f, 0.75f),
        [PieceType.Stairs]   = new Color(0.80f, 0.60f, 0.20f),
    };

    public static readonly Dictionary<PieceType, string> Labels = new()
    {
        [PieceType.Start]    = "Start",
        [PieceType.Exit]     = "Exit",
        [PieceType.Straight] = "Straight",
        [PieceType.LHall]    = "L-Hall",
        [PieceType.THall]    = "T-Junction",
        [PieceType.Stairs]   = "Stairs",
    };

    public static readonly Dictionary<PieceType, string> ShortLabels = new()
    {
        [PieceType.Start]    = "S",
        [PieceType.Exit]     = "X",
        [PieceType.Straight] = "I",
        [PieceType.LHall]    = "L",
        [PieceType.THall]    = "T",
        [PieceType.Stairs]   = "UP",
    };

    public static readonly Dictionary<PieceType, int> GoldCosts = new()
    {
        [PieceType.Start]    = 0,
        [PieceType.Exit]     = 0,
        [PieceType.Straight] = 0,
        [PieceType.LHall]    = 0,
        [PieceType.THall]    = 25,
        [PieceType.Stairs]   = 50,
    };
}
