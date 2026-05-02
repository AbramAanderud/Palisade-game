using System.Collections.Generic;
using Godot;

/// res://scripts/PieceDB.cs — Piece type definitions, openings, costs, and colors.
public enum PieceType { Start, Exit, Straight, LHall, THall, Cross, Stairs, StairsUp, StairsDown }

[System.Flags]
public enum Dir { None = 0, N = 1, E = 2, S = 4, W = 8 }

public static class PieceDB
{
    // Openings at rotation = 0.
    // Stairs (legacy): S on floor F, N connects to floor F+1.
    // StairsUp: N connects to floor F+1, S stays on floor F.
    // StairsDown: N stays on floor F, S connects to floor F-1.
    static readonly Dictionary<PieceType, Dir> BaseOpenings = new()
    {
        [PieceType.Start]      = Dir.S,
        [PieceType.Exit]       = Dir.N,
        [PieceType.Straight]   = Dir.N | Dir.S,
        [PieceType.LHall]      = Dir.N | Dir.E,
        [PieceType.THall]      = Dir.N | Dir.E | Dir.S,
        [PieceType.Cross]      = Dir.N | Dir.E | Dir.S | Dir.W,
        [PieceType.Stairs]     = Dir.S | Dir.N,   // legacy
        [PieceType.StairsUp]   = Dir.N | Dir.S,
        [PieceType.StairsDown] = Dir.N | Dir.S,
    };

    public static Dir GetOpenings(PieceType type, int rotation)
    {
        Dir d = BaseOpenings[type];
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

    /// Returns the opposite of a direction.
    public static Dir Opposite(Dir d) => d switch
    {
        Dir.N => Dir.S,
        Dir.S => Dir.N,
        Dir.E => Dir.W,
        _     => Dir.E,
    };

    /// Returns the same-floor (flat/low-end) opening direction for a stair piece.
    /// This is the direction where the stair exits onto the SAME floor at the low end.
    /// StairsUp/Stairs at rot=0: flat dir = Dir.S (low end faces south).
    /// StairsDown at rot=0: flat dir = Dir.N (low end is the high geometry end, faces north on same floor).
    /// Both rotate CW with the piece rotation.
    public static Dir GetStairFlatDir(PieceType type, int rotation)
    {
        // FlatDir is always the opposite of CrossDir.
        return Opposite(GetStairCrossDir(type, rotation));
    }

    /// Canonical stair descriptor bundling flat dir, cross dir, and floor delta.
    public struct StairInfo
    {
        /// Same-floor exit direction (low end of stair).
        public Dir FlatDir;
        /// Cross-floor exit direction (high end, leads to floor + FloorDelta).
        public Dir CrossDir;
        /// +1 for StairsUp/Stairs (goes up), -1 for StairsDown (goes down).
        public int FloorDelta;
    }

    /// Returns a canonical StairInfo for the given stair piece and rotation.
    public static StairInfo GetStairInfo(PieceType type, int rotation)
    {
        Dir cross = GetStairCrossDir(type, rotation);
        return new StairInfo
        {
            CrossDir   = cross,
            FlatDir    = Opposite(cross),
            FloorDelta = StairFloorDelta(type),
        };
    }

    /// Returns the face of a stair piece that crosses to a different floor.
    /// For Stairs/StairsUp this is the "up" face (toward floor+1): Dir.N at rot=0, rotates CW.
    /// For StairsDown this is the "down" face (toward floor-1): Dir.S at rot=0, rotates CW.
    public static Dir GetStairCrossDir(PieceType type, int rotation = 0)
    {
        if (type == PieceType.StairsDown)
        {
            Dir d = Dir.S;
            int r = ((rotation % 4) + 4) % 4;
            for (int i = 0; i < r; i++) d = RotateCW(d);
            return d;
        }
        return GetStairUpDir(rotation);
    }

    /// Returns the opening direction of a Stairs/StairsUp piece that exits to floor+1.
    /// At rotation 0 that is N; rotates clockwise with the piece.
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
        [PieceType.Cross]      = new Color(0.30f, 0.55f, 0.85f),
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
        [PieceType.Cross]      = "Cross / X-Junction",
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
        [PieceType.Cross]      = "+",
        [PieceType.Stairs]     = "UP",
        [PieceType.StairsUp]   = "UP",
        [PieceType.StairsDown] = "DN",
    };

    public static readonly Dictionary<PieceType, int> GoldCosts = new()
    {
        [PieceType.Start]      = 0,
        [PieceType.Exit]       = 0,
        [PieceType.Straight]   = 10,
        [PieceType.LHall]      = 20,
        [PieceType.THall]      = 30,
        [PieceType.Cross]      = 40,
        [PieceType.Stairs]     = 50,
        [PieceType.StairsUp]   = 50,
        [PieceType.StairsDown] = 50,
    };
}
