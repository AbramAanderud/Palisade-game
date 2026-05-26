/// Static state carrier for an online multiplayer session.
/// Kept separate from GameState.cs so offline play is unaffected.
public static class OnlineGameState
{
    public static bool   IsHost           = false;
    public static string RoomCode         = "";
    public static int    SelectedMazeSlot = 0;
    /// Full maze JSON received from the host (client-side only).
    public static string RemoteMazeJson   = "";

    // ── Lobby / identity ──────────────────────────────────────────────────────
    /// Display name shown to other players in the lobby.
    public static string PlayerName  = "";
    /// Player ID assigned by the relay server for this session.
    public static string MyPlayerId  = "";

    // ── Persistent gold (accumulates across matches) ──────────────────────────
    public static int Gold = 0;

    public static void Reset()
    {
        IsHost           = false;
        RoomCode         = "";
        SelectedMazeSlot = 0;
        RemoteMazeJson   = "";
        MyPlayerId       = "";
        // PlayerName and Gold intentionally preserved across matches
    }
}
