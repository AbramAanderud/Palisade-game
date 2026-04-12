using Godot;
using Godot.Collections;

// Autoload singleton — available everywhere as TileData.TileColors[TileData.TileType.Floor] etc.
public partial class TileData : Node
{
    public enum TileType
    {
        Empty      = 0,
        Floor      = 1,
        Wall       = 2,
        StairsUp   = 3,
        StairsDown = 4,
        Entrance   = 5,
        Exit       = 6,
    }

    public static readonly Dictionary<TileType, Color> TileColors = new()
    {
        [TileType.Empty]      = new Color(0f,    0f,    0f,    0f),
        [TileType.Floor]      = new Color(0.67f, 0.67f, 0.67f, 1f),
        [TileType.Wall]       = new Color(0.29f, 0.22f, 0.16f, 1f),
        [TileType.StairsUp]   = new Color(0.13f, 0.73f, 0.27f, 1f),
        [TileType.StairsDown] = new Color(0.07f, 0.33f, 0.13f, 1f),
        [TileType.Entrance]   = new Color(0.13f, 0.27f, 1.0f,  1f),
        [TileType.Exit]       = new Color(1.0f,  0.13f, 0.13f, 1f),
    };

    public static readonly Dictionary<TileType, string> TileNames = new()
    {
        [TileType.Empty]      = "Erase",
        [TileType.Floor]      = "Floor",
        [TileType.Wall]       = "Wall",
        [TileType.StairsUp]   = "Stairs Up",
        [TileType.StairsDown] = "Stairs Down",
        [TileType.Entrance]   = "Entrance",
        [TileType.Exit]       = "Exit",
    };
}
