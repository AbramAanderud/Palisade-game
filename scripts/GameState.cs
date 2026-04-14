/// res://scripts/GameState.cs
/// Simple static singleton for passing state between scene transitions.
public static class GameState
{
    /// Maze slot to load when entering DungeonGame.
    public static int ActiveSlot = 0;

    /// Arena mode: load two mazes and connect them via a circular room.
    public static bool IsArenaMode = false;
    public static int  ArenaSlotA  = 0;   // first maze  (spawned at Z=0)
    public static int  ArenaSlotB  = 1;   // second maze (flipped, spawned beyond arena)
}
