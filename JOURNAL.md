# Engineering Journal

## 2026-05-17 — Online Multiplayer: Play Game / Matchmaking Flow

### What was built

**Full matchmaking lobby** replacing the manual room-code create/join flow with a browseable player list.

**Relay server (`server/relay.js`)** — extended with a lobby pool:
- `lobby_join` → assigns player ID, broadcasts updated list to all lobby members
- `challenge` / `challenge_response` → challenge flow; relay creates room and sends `match_made` to both on accept
- Lobby cleaned up on disconnect; pending challenges auto-declined

**New `PlayGameScreen.cs` / `.tscn`**:
- Shows online players with Challenge buttons
- Incoming challenge dialog (Accept / Decline)
- Outgoing challenge dialog with Cancel
- Maze selection via `OptionButton` at the bottom (positioned low so popup opens upward naturally)
- Displays total gold

**`TitleScreen.cs`** — added "Play Game" button above "Online Maze" in centre column.

**`OnlineDungeonArena.cs`** — complete game-end loop:
- `WeaponPickup.PickedUp` event wired; sword pickup awards 100 gold and starts 40 s combat timer
- Peer sword pickup detected via `{t: "sword_taken"}` message; same timer started on both sides
- `{t: "die"}` message handled: receiver awards self 100 gold (kill) and calls EndMatch("Victory!")
- `PlayerHealth.OnDied` wired locally: sends `die` to peer, calls EndMatch("Defeated!")
- Timer expiry: survivor (non-sword-holder) gets 100 gold, EndMatch("Time's Up!")
- Post-match overlay shows result, match gold earned, total gold, and "Return to Lobby" button
- Auto-returns to `PlayGameScreen` after 8 s

**`NetworkManager.cs`** — bug fix + lobby signals:
- **Bug fixed**: `HandleMessage` was dropping all game-data messages (those with `t` not `type`). Fixed: if no `type` field, emit `GameDataReceived` directly.
- New signals: `LobbyJoined`, `LobbyUpdated`, `IncomingChallenge`, `ChallengeDeclined`, `MatchMade`
- New methods: `ConnectAndJoinLobby`, `SendChallenge`, `RespondChallenge`

**`OnlineGameState.cs`** — added `Gold` (persists across matches), `PlayerName`, `MyPlayerId`.

**`WeaponPickup.cs`** — added `PickedUp` event; fixed puppet erroneously blocking pickup detection.

### Gold rules
| Condition | Gold |
|---|---|
| First player to pick up the sword | +100 |
| Killing the opponent | +100 |
| Surviving the 40 s timer (didn't have the sword) | +100 |

---

## 2026-05-07 — Stair Physics: Final Solution

### Problem
Walking down stairs caused the player to arc outward ("fly off") regardless of how `FloorSnapLength` or gravity were tuned. Per-tread collision let the capsule briefly step off each tread edge and become airborne; once airborne, horizontal momentum dominated.

### Solution
Adopted the industry-standard pattern: **visual steps with no physics collision, invisible diagonal ramp for physics**.

**`DungeonBuilder.cs`** stair branch in `BuildFloor`:
- `AddMeshVisual` for treads (`floorST`) and risers (`riserST`) — rendered but no collision shapes
- `AddStairRamp(body, p, geomBase)` — invisible ramp spanning full stair cell, `BackfaceCollision = true`

**`PlayerController.cs`**:
- `FloorMaxAngle = 68° * Pi/180` — must exceed ramp angle `atan(FloorHeight / CellSize)`. With `FloorHeight=18, CellSize=10` the ramp is ~61°; 68° gives 7° margin
- `FloorSnapLength = 1.5f`
- `TryStepUp` kept for non-stair obstacles; it never fires on the smooth ramp

### Why it's robust
The ramp provides continuous floor contact at all times. There is no tread edge to step off, no airborne moment, and no asymmetry between ascending and descending. `FloorMaxAngle` is the only value to watch: if `FloorHeight` or `CellSize` ever changes, recalculate `atan(FloorHeight / CellSize)` and keep `FloorMaxAngle` at least 5° above it.

---

## 2026-05-05 18:05

### Stair Step Geometry Fix
- **What**: Removed the diagonal stair-center panels and central under-floor from stair cells, then increased generated stair count from 20 to 48 shallow steps.
- **Why**: The stairwell was presenting a ramp-like surface under the stairs, while the visible steps lacked a convincing front lip and were too coarse for normal walking feel.
- **How**: Updated `scripts/DungeonBuilder.cs` so stair treads and risers are the actual visible/collidable stair surface. Left side/wing floors and walls intact so the stairwell remains sealed without a hidden diagonal walk plane.
- **Issues**: `godot` is not on PATH in this shell, so visual capture of `test/StairInspect.tscn` was not available from here.
- **Result**: `dotnet build Palisade.csproj` succeeds with 0 warnings and 0 errors.

## 2026-05-05 16:20

### Claude Chat Exporter
- **What**: Added a local PowerShell exporter for Claude Code JSONL transcripts into Obsidian-readable Markdown.
- **Why**: Let Obsidian act as a cheap cross-project second brain without spending Claude tokens on logging or summaries.
- **How**: Added `tools/export-claude-chats.ps1` with source/output parameters, transcript-path export, project filtering, dry-run support, and an idempotent manifest. Added `tools/export-claude-hook.ps1` for Claude `SessionEnd` hook automation.
- **Issues**: Claude CLI helper produced the wrong implementation direction, so the exporter was implemented directly. Direct script execution was blocked by PowerShell policy, so docs use per-command `ExecutionPolicy Bypass`.
- **Result**: Project can now archive local Claude Code chats into `AI Chats/Claude Code` from the project root. A global Claude `SessionEnd` hook was installed in `C:\Users\Abe\.claude\settings.json` so future Claude sessions auto-export into this vault.

---

## 2026-05-05 17:25

### NoBrainer Central Vault Setup
- **What**: Created `C:\Users\Abe\Documents\NoBrainer` as the main personal Obsidian vault and copied Claude exporter scripts to `C:\Users\Abe\.claude\tools`.
- **Why**: Keep a separate general second-brain vault while allowing individual project vaults like Palisade and LetsGitaJob to remain project-specific.
- **How**: Updated the global Claude `SessionEnd` hook to run the global hook script and export to `NoBrainer\AI Chats\Claude Code`.
- **Issues**: Initial backfill needed elevated filesystem access because `NoBrainer` is outside the Palisade workspace. Some older Claude project folders map to generic names like `Abe` or `Documents` because those sessions were started from broad directories.
- **Result**: Backfilled 59 Claude transcripts into NoBrainer, including folders for `palisade-godot` and `LetsGitaJob`.

---

## 2026-05-05 17:35

### Obsidian Stability Fix
- **What**: Updated the Claude transcript exporter to skip thinking blocks, tool-use entries, and tool results by default, with a smaller per-entry truncation limit.
- **Why**: Raw Claude transcripts produced multi-megabyte Markdown files that caused Obsidian to open to a black screen or hang during indexing.
- **How**: Added `-IncludeThinking`, `-IncludeToolResults`, and `-MaxEntryChars` controls, then regenerated NoBrainer exports in lightweight conversation mode.
- **Issues**: Existing NoBrainer export had files as large as 12 MB before cleanup.
- **Result**: NoBrainer chat archive dropped to about 2.3 MB total, with the largest note under 1 MB.

---

## 2026-05-02 01:05

### Documentation Framework Implementation
- **What**: Implemented Claude Conductor modular documentation system
- **Why**: Improve AI navigation and code maintainability
- **How**: Used `npx claude-conductor` to initialize framework
- **Issues**: None - clean implementation
- **Result**: Documentation framework successfully initialized

---
