# Palisade Relay Server

Stateless WebSocket relay — pairs two players for a match and forwards game packets between them. No database needed; all player data (mazes, name, gold) lives on each player's own machine.

## How it works

1. Players open the game and click **Play Game** → both connect to the relay and enter the matchmaking lobby.
2. Each player sees a live list of other players currently looking for a match.
3. One player clicks **Challenge** on another → the target gets an Accept / Decline dialog.
4. On Accept, the relay creates a room, designates one player host, and sends both a `match_made` message.
5. The host transmits their selected maze to the client; both build the arena locally.
6. All game data (positions, hits, sword events) is forwarded peer-to-peer through the relay.
7. When the match ends, both players are returned to the Play Game lobby.

The relay only holds connection state while clients are connected. On disconnect, rooms are deleted.

---

## Running locally (same machine / LAN)

```bash
npm install
node relay.js
```

Server listens on port 3000. In `scripts/NetworkManager.cs`, set:

```csharp
public const string RelayUrl = "ws://localhost:3000";
// or for LAN: "ws://YOUR_LAN_IP:3000"  (run ipconfig to find your IP)
```

---

## Cross-network play (Render.com free tier)

> Both players connect **outbound** to the relay — no port forwarding needed on either side.

1. Push this repo to GitHub (the `server/` directory is the root for deployment).
2. Go to [render.com](https://render.com) → **New → Web Service**.
3. Connect your repo and set:
   - **Root Directory:** `server`
   - **Build Command:** `npm install`
   - **Start Command:** `node relay.js`
   - **Environment:** Node
4. Deploy. Render assigns a URL like `https://palisade-relay.onrender.com`.
5. In `scripts/NetworkManager.cs`, update the constant:
   ```csharp
   public const string RelayUrl = "wss://palisade-relay.onrender.com";
   ```
   Note `wss://` (TLS) not `ws://` — Render requires TLS on public URLs.
6. Rebuild the Godot project and distribute the binary.

> **Free tier note:** Render spins the server down after 15 min of inactivity. The first connection after spin-down takes ~30 s. To avoid this, ping the server periodically or upgrade to a paid instance ($7/month).

### Alternatives to Render

| Host | Free tier | Always-on | Notes |
|---|---|---|---|
| Render.com | ✓ | No (15 min sleep) | Easy GitHub deploy |
| Railway.app | ✓ | ~500 hrs/mo | Slightly better uptime |
| Fly.io | ✓ | Yes | Needs `fly.toml`, more setup |
| VPS (Hetzner/DO) | $4–6/mo | Yes | Full control |

---

## Do I need a database?

**No.** Here's what lives where:

| Data | Where stored | Persists |
|---|---|---|
| Player name | `user://player_profile.json` (local) | Between sessions on same machine |
| Gold | `user://player_profile.json` (local) | Between sessions on same machine |
| Mazes | `user://maze_slot_N.json` (local) | Between sessions on same machine |
| Match rooms | Relay RAM only | Until both players disconnect |

The host's maze is transmitted to the client at match start, so nothing maze-related needs to live on a server.

A database only becomes useful if you want cross-device sync (a player logging in from a different computer) or a shared public maze library. That can be added later with Supabase (free tier) without changing the relay.

---

## Relay message protocol

### Lobby (before a match)

| Direction | Message | Effect |
|---|---|---|
| Client → Relay | `{type:"lobby_join", name:"PlayerName"}` | Enter matchmaking pool; receive `lobby_joined` + `lobby_update` |
| Relay → Client | `{type:"lobby_joined", playerId:"1"}` | Confirms join, assigns ID |
| Relay → All lobby | `{type:"lobby_update", players:[{id,name}]}` | Live player list (excludes self) |
| Client → Relay | `{type:"challenge", targetId:"2"}` | Send challenge to target |
| Relay → Target | `{type:"incoming_challenge", fromId:"1", fromName:"X"}` | Show Accept/Decline dialog |
| Client → Relay | `{type:"challenge_response", challengerId:"1", accepted:true}` | Accept/decline |
| Relay → Both | `{type:"match_made", code:"123", isHost:true/false}` | Match confirmed; start game |
| Relay → Challenger | `{type:"challenge_declined"}` | Challenge was declined or target disconnected |

### In-match (after `match_made`)

All messages are forwarded verbatim to the peer. Game code uses `t` (not `type`) to avoid collision with relay messages.

| `t` | Sender | Payload | Purpose |
|---|---|---|---|
| `maze` | Host | `{json:"..."}` | Send serialised MazeData to client |
| `ready_for_maze` | Client | — | Request re-send of maze (handshake) |
| `pos` | Both | `{x,y,z,yaw,pitch,stam}` | 20 Hz position sync |
| `hit` | Both | `{dmg:float}` | Damage dealt to puppet → apply to local player |
| `swing` | Both | `{idx:int}` | Sword swing animation sync |
| `sword_taken` | Both | — | Sword picked up; start 40 s combat timer |
| `die` | Both | — | Local player died; peer awards kill gold |
