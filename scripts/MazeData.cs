using System.Collections.Generic;

/// res://scripts/MazeData.cs — Serializable maze data model.

public class MazePiece
{
    public PieceType Type     { get; set; }
    public int       X        { get; set; }
    public int       Y        { get; set; }
    public int       Floor    { get; set; }
    public int       Rotation { get; set; }  // 0-3 (clockwise quarter turns)
}

public class MazeData
{
    public string          Name      { get; set; } = "Untitled";
    public int             GoldSpent { get; set; } = 0;
    public List<MazePiece> Pieces    { get; set; } = new();
}
