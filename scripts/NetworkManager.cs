using Godot;
using System;
using System.Text;
using System.Text.Json;

/// Autoload singleton. Manages WebSocket connection to the Palisade relay server.
/// Both peers connect outbound — no port forwarding needed.
///
/// Relay protocol (JSON, "type" field):
///   create          → room_created {code}
///   join {code}     → joined {code} | error {reason}
///   peer_connected  ← relay notifies host when guest joins
///   peer_disconnected ← relay notifies remaining peer on disconnect
///   (anything else) ← forwarded verbatim to the other peer
///
/// Game data travels as raw JSON forwarded by the relay.
/// Call SendGameData(json) to send; subscribe GameDataReceived to receive.
public partial class NetworkManager : Node
{
    // ── Relay URL ─────────────────────────────────────────────────────────────
    // Local testing:  "ws://localhost:3000"
    // Production:     paste your Render URL here, e.g. "wss://palisade-game.onrender.com"
    public const string RelayUrl = "wss://PASTE_YOUR_RENDER_URL_HERE";

    // ── Signals ───────────────────────────────────────────────────────────────
    [Signal] public delegate void RoomCreatedEventHandler(string code);
    [Signal] public delegate void JoinedRoomEventHandler(string code);
    [Signal] public delegate void PeerConnectedEventHandler();
    [Signal] public delegate void PeerDisconnectedEventHandler();
    [Signal] public delegate void GameDataReceivedEventHandler(string json);
    [Signal] public delegate void ConnectionFailedEventHandler(string reason);

    // ── Lobby / matchmaking signals ───────────────────────────────────────────
    [Signal] public delegate void LobbyJoinedEventHandler(string myId);
    [Signal] public delegate void LobbyUpdatedEventHandler(string playersJson);
    [Signal] public delegate void IncomingChallengeEventHandler(string fromId, string fromName);
    [Signal] public delegate void ChallengeDeclinedEventHandler();
    [Signal] public delegate void MatchMadeEventHandler(bool isHost);

    // ── State ─────────────────────────────────────────────────────────────────
    readonly WebSocketPeer _ws = new();
    bool   _wasOpen;
    Action? _pendingAction;  // executed once socket opens

    public bool IsOpen => _ws.GetReadyState() == WebSocketPeer.State.Open;

    // ── Public API ────────────────────────────────────────────────────────────

    /// Connect to relay and create a new room. Fires RoomCreated on success.
    public void ConnectAndCreate()
    {
        _pendingAction = () => _ws.SendText("{\"type\":\"create\"}");
        OpenSocket();
    }

    /// Connect to relay and join an existing room by code. Fires JoinedRoom on success.
    public void ConnectAndJoin(string code)
    {
        _pendingAction = () => _ws.SendText($"{{\"type\":\"join\",\"code\":\"{code}\"}}");
        OpenSocket();
    }

    /// Forward raw JSON to the other peer via the relay.
    public void SendGameData(string json)
    {
        if (_ws.GetReadyState() == WebSocketPeer.State.Open)
            _ws.SendText(json);
    }

    /// Close the WebSocket and reset state.
    public void Disconnect()
    {
        _ws.Close();
        _wasOpen    = false;
        _pendingAction = null;
    }

    // ── Lobby / matchmaking API ───────────────────────────────────────────────

    /// Connect to relay and join the matchmaking lobby under the given name.
    public void ConnectAndJoinLobby(string playerName)
    {
        string safe = JsonSerializer.Serialize(playerName);
        _pendingAction = () => _ws.SendText($"{{\"type\":\"lobby_join\",\"name\":{safe}}}");
        OpenSocket();
    }

    /// Send a challenge request to another lobby player.
    public void SendChallenge(string targetId)
    {
        if (_ws.GetReadyState() == WebSocketPeer.State.Open)
            _ws.SendText($"{{\"type\":\"challenge\",\"targetId\":\"{targetId}\"}}");
    }

    /// Accept or decline an incoming challenge.
    public void RespondChallenge(string challengerId, bool accepted)
    {
        if (_ws.GetReadyState() == WebSocketPeer.State.Open)
        {
            string acc = accepted ? "true" : "false";
            _ws.SendText($"{{\"type\":\"challenge_response\",\"challengerId\":\"{challengerId}\",\"accepted\":{acc}}}");
        }
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override void _Process(double delta)
    {
        var state = _ws.GetReadyState();
        if (state == WebSocketPeer.State.Closed) return;

        _ws.Poll();

        // Detect socket opening
        if (!_wasOpen && _ws.GetReadyState() == WebSocketPeer.State.Open)
        {
            _wasOpen = true;
            _pendingAction?.Invoke();
            _pendingAction = null;
        }

        // Detect unexpected close (connection failure before open)
        if (_wasOpen && _ws.GetReadyState() == WebSocketPeer.State.Closed)
        {
            _wasOpen = false;
            EmitSignal(SignalName.ConnectionFailed, "disconnected");
            return;
        }

        // Also detect failure to open at all
        if (!_wasOpen && _ws.GetReadyState() == WebSocketPeer.State.Closing)
        {
            EmitSignal(SignalName.ConnectionFailed, "timeout");
            return;
        }

        // Drain received packets
        while (_ws.GetAvailablePacketCount() > 0)
        {
            var bytes = _ws.GetPacket();
            var text  = Encoding.UTF8.GetString(bytes);
            HandleMessage(text);
        }
    }

    // ── Internal ──────────────────────────────────────────────────────────────

    void OpenSocket()
    {
        // Close any existing connection first
        if (_ws.GetReadyState() != WebSocketPeer.State.Closed)
            _ws.Close();
        _wasOpen = false;

        var err = _ws.ConnectToUrl(RelayUrl);
        if (err != Error.Ok)
        {
            GD.PushError($"[NetworkManager] ConnectToUrl failed: {err}");
            EmitSignal(SignalName.ConnectionFailed, err.ToString());
        }
    }

    void HandleMessage(string raw)
    {
        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            root = doc.RootElement.Clone();
        }
        catch { return; }

        // Messages without "type" are game data forwarded verbatim from the peer
        if (!root.TryGetProperty("type", out var typeProp))
        {
            EmitSignal(SignalName.GameDataReceived, raw);
            return;
        }
        var t = typeProp.GetString() ?? "";

        switch (t)
        {
            case "room_created":
                EmitSignal(SignalName.RoomCreated,
                    root.GetProperty("code").GetString() ?? "");
                break;

            case "joined":
                EmitSignal(SignalName.JoinedRoom,
                    root.GetProperty("code").GetString() ?? "");
                break;

            case "peer_connected":
                EmitSignal(SignalName.PeerConnected);
                break;

            case "peer_disconnected":
                EmitSignal(SignalName.PeerDisconnected);
                break;

            case "error":
                EmitSignal(SignalName.ConnectionFailed,
                    root.TryGetProperty("reason", out var r) ? r.GetString() ?? "" : "unknown");
                break;

            // ── Lobby / matchmaking ───────────────────────────────────────────
            case "lobby_joined":
                OnlineGameState.MyPlayerId = root.GetProperty("playerId").GetString() ?? "";
                EmitSignal(SignalName.LobbyJoined, OnlineGameState.MyPlayerId);
                break;

            case "lobby_update":
                EmitSignal(SignalName.LobbyUpdated, raw);
                break;

            case "incoming_challenge":
                EmitSignal(SignalName.IncomingChallenge,
                    root.GetProperty("fromId").GetString() ?? "",
                    root.GetProperty("fromName").GetString() ?? "");
                break;

            case "challenge_declined":
            case "challenge_failed":
                EmitSignal(SignalName.ChallengeDeclined);
                break;

            case "match_made":
            {
                bool   mIsHost = root.GetProperty("isHost").GetBoolean();
                string mCode   = root.GetProperty("code").GetString() ?? "";
                OnlineGameState.IsHost   = mIsHost;
                OnlineGameState.RoomCode = mCode;
                EmitSignal(SignalName.MatchMade, mIsHost);
                break;
            }

            default:
                // Unrecognised relay message — treat as game data
                EmitSignal(SignalName.GameDataReceived, raw);
                break;
        }
    }
}
