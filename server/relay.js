const { WebSocketServer } = require('ws');
const PORT = process.env.PORT || 3000;
const wss = new WebSocketServer({ port: PORT });

// rooms: code -> { host: ws, guest: ws|null }
const rooms = new Map();
// lobby: playerId -> { ws, name }
const lobby = new Map();
// pendingChallenges: challengerId -> targetId
const pendingChallenges = new Map();

let nextId = 1;

function makeCode() {
  let c;
  do { c = String(Math.floor(Math.random() * 900) + 100); }
  while (rooms.has(c));
  return c;
}

function send(ws, obj) {
  if (ws && ws.readyState === 1) ws.send(JSON.stringify(obj));
}

function broadcastLobbyUpdate() {
  const players = Array.from(lobby.entries()).map(([id, p]) => ({ id, name: p.name }));
  for (const [id, p] of lobby) {
    send(p.ws, { type: 'lobby_update', players: players.filter(pl => pl.id !== id) });
  }
}

wss.on('connection', ws => {
  ws._room = null;
  ws._role = null;
  ws._id   = String(nextId++);
  ws._name = '';

  ws.on('message', raw => {
    let msg;
    try { msg = JSON.parse(raw); } catch { return; }

    if (msg.type === 'create') {
      const code = makeCode();
      rooms.set(code, { host: ws, guest: null });
      ws._room = code;
      ws._role = 'host';
      send(ws, { type: 'room_created', code });
      console.log(`Room ${code} created (manual)`);

    } else if (msg.type === 'join') {
      const room = rooms.get(msg.code);
      if (!room) { send(ws, { type: 'error', reason: 'not_found' }); return; }
      if (room.guest) { send(ws, { type: 'error', reason: 'full' }); return; }
      room.guest = ws;
      ws._room = msg.code;
      ws._role = 'guest';
      send(ws, { type: 'joined', code: msg.code });
      send(room.host, { type: 'peer_connected' });
      console.log(`Room ${msg.code} joined (manual)`);

    } else if (msg.type === 'lobby_join') {
      ws._name = (msg.name || 'Player').substring(0, 24);
      lobby.set(ws._id, { ws, name: ws._name });
      send(ws, { type: 'lobby_joined', playerId: ws._id });
      broadcastLobbyUpdate();
      console.log(`${ws._name} (${ws._id}) joined lobby — ${lobby.size} in lobby`);

    } else if (msg.type === 'lobby_leave') {
      lobby.delete(ws._id);
      pendingChallenges.delete(ws._id);
      broadcastLobbyUpdate();

    } else if (msg.type === 'challenge') {
      const target = lobby.get(msg.targetId);
      if (!target) { send(ws, { type: 'challenge_failed', reason: 'not_found' }); return; }
      if (pendingChallenges.has(ws._id)) { send(ws, { type: 'challenge_failed', reason: 'already_pending' }); return; }
      pendingChallenges.set(ws._id, msg.targetId);
      send(target.ws, { type: 'incoming_challenge', fromId: ws._id, fromName: ws._name });
      console.log(`Challenge: ${ws._name} -> ${target.name}`);

    } else if (msg.type === 'challenge_response') {
      const challengerId = msg.challengerId;
      const challenger = lobby.get(challengerId);
      if (!challenger || pendingChallenges.get(challengerId) !== ws._id) return;
      pendingChallenges.delete(challengerId);

      if (msg.accepted) {
        const code = makeCode();
        rooms.set(code, { host: challenger.ws, guest: ws });
        challenger.ws._room = code; challenger.ws._role = 'host';
        ws._room = code; ws._role = 'guest';
        lobby.delete(challengerId);
        lobby.delete(ws._id);
        broadcastLobbyUpdate();
        send(challenger.ws, { type: 'match_made', code, isHost: true });
        send(ws,            { type: 'match_made', code, isHost: false });
        console.log(`Match: ${challenger.ws._name} vs ${ws._name} → room ${code}`);
      } else {
        send(challenger.ws, { type: 'challenge_declined' });
      }

    } else {
      // Forward game data to peer in room
      const room = ws._room ? rooms.get(ws._room) : null;
      if (!room) return;
      const other = ws._role === 'host' ? room.guest : room.host;
      if (other && other.readyState === 1) other.send(raw.toString());
    }
  });

  ws.on('close', () => {
    // Clean up lobby membership
    if (lobby.has(ws._id)) {
      lobby.delete(ws._id);
      // Notify any challenger whose target just left
      for (const [cId, tId] of pendingChallenges) {
        if (tId === ws._id) {
          const challenger = lobby.get(cId);
          if (challenger) send(challenger.ws, { type: 'challenge_declined' });
          pendingChallenges.delete(cId);
        }
      }
      pendingChallenges.delete(ws._id);
      broadcastLobbyUpdate();
    }

    // Clean up room
    const code = ws._room;
    if (!code) return;
    const room = rooms.get(code);
    if (!room) return;
    const other = ws._role === 'host' ? room.guest : room.host;
    send(other, { type: 'peer_disconnected' });
    rooms.delete(code);
    console.log(`Room ${code} closed`);
  });

  ws.on('error', () => {});
});

console.log(`Palisade relay listening on port ${PORT}`);
