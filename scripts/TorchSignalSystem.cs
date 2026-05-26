using Godot;
using System.Collections.Generic;
using System.Linq;

/// res://scripts/TorchSignalSystem.cs
/// Reveals the most direct path from Exit back to Start by lighting torches one cell at a time.
///
/// Timeline:
///   t = 0              — dungeon loaded, system idle
///   t = TriggerDelay   — first cell (Exit) goes blue
///   t += WaveInterval — next cell along the shortest path lights up
///
/// The shortest path is computed via BFS from Exit to Start using piece-opening adjacency
/// (including cross-floor stair connections). Only cells on that single direct path light up —
/// other branches stay dark.
public partial class TorchSignalSystem : Node
{
    public float TriggerDelay = 20f;   // seconds before the first cell lights
    public float WaveInterval = 5.0f;  // seconds between consecutive cells on the path

    List<MazePiece> _path = new();
    Dictionary<(int, int, int), OmniLight3D> _torches = new();
    int   _nextIdx   = 0;
    float _elapsed   = 0f;
    bool  _triggered = false;
    bool  _active    = false;

    public void Init(MazeData data, DungeonBuilder builder)
    {
        _torches = builder.TorchByCell;

        var exit  = data.Pieces.FirstOrDefault(p => p.Type == PieceType.Exit);
        var start = data.Pieces.FirstOrDefault(p => p.Type == PieceType.Start);
        if (exit == null || start == null)
        {
            GD.PushWarning("[TorchSignalSystem] Missing Start or Exit — signal disabled");
            return;
        }

        _path   = ShortestPath(data, exit, start);
        _active = _path.Count > 0;
        if (!_active)
            GD.PushWarning("[TorchSignalSystem] No path from Exit to Start — signal disabled");
    }

    public override void _Process(double delta)
    {
        if (!_active) return;
        _elapsed += (float)delta;

        if (!_triggered)
        {
            if (_elapsed < TriggerDelay) return;
            _triggered = true;
            _elapsed   = 0f;
            LightNextCell();
            return;
        }

        while (_elapsed >= WaveInterval && _nextIdx < _path.Count)
        {
            _elapsed -= WaveInterval;
            LightNextCell();
        }

        if (_nextIdx >= _path.Count) _active = false;
    }

    void LightNextCell()
    {
        if (_nextIdx >= _path.Count) return;
        var piece = _path[_nextIdx++];
        if (_torches.TryGetValue((piece.X, piece.Y, piece.Floor), out var light))
            TweenToBlue(light);
    }

    static void TweenToBlue(OmniLight3D light)
    {
        var tw = light.CreateTween().SetParallel(true);
        tw.TweenProperty(light, "light_color",  new Color(0.15f, 0.45f, 1.0f), 1.5f)
          .SetTrans(Tween.TransitionType.Sine);
        tw.TweenProperty(light, "light_energy", 3.5f, 1.5f)
          .SetTrans(Tween.TransitionType.Sine);
        tw.TweenProperty(light, "omni_range",   18f,  1.5f)
          .SetTrans(Tween.TransitionType.Sine);
    }

    // ── BFS shortest path ─────────────────────────────────────────────────────
    // Returns the cells of the shortest path from `from` to `to` (inclusive of both).
    // Empty list means no path exists.
    static List<MazePiece> ShortestPath(MazeData data, MazePiece from, MazePiece to)
    {
        var adj      = BuildAdjacency(data);
        var startKey = (from.X, from.Y, from.Floor);
        var goalKey  = (to.X,   to.Y,   to.Floor);

        var parent  = new Dictionary<(int, int, int), (int, int, int)>();
        var visited = new HashSet<(int, int, int)> { startKey };
        var queue   = new Queue<MazePiece>();
        queue.Enqueue(from);

        bool found = startKey == goalKey;
        while (queue.Count > 0 && !found)
        {
            var p = queue.Dequeue();
            if (!adj.TryGetValue((p.X, p.Y, p.Floor), out var neighbors)) continue;
            foreach (var n in neighbors)
            {
                var nk = (n.X, n.Y, n.Floor);
                if (!visited.Add(nk)) continue;
                parent[nk] = (p.X, p.Y, p.Floor);
                if (nk == goalKey) { found = true; break; }
                queue.Enqueue(n);
            }
        }

        if (!found) return new List<MazePiece>();

        var lookup = data.Pieces.ToDictionary(p => (p.X, p.Y, p.Floor));
        var path   = new List<MazePiece>();
        var cur    = goalKey;
        while (true)
        {
            if (lookup.TryGetValue(cur, out var piece)) path.Add(piece);
            if (cur == startKey) break;
            if (!parent.TryGetValue(cur, out cur)) break;
        }
        path.Reverse();
        return path;
    }

    // Build a bidirectional adjacency map from piece openings.
    // Handles same-floor corridor connections and cross-floor stair connections.
    static Dictionary<(int, int, int), List<MazePiece>> BuildAdjacency(MazeData data)
    {
        var lookup = data.Pieces.ToDictionary(p => (p.X, p.Y, p.Floor));
        var adj    = new Dictionary<(int, int, int), List<MazePiece>>(data.Pieces.Count);
        foreach (var p in data.Pieces)
            adj[(p.X, p.Y, p.Floor)] = new List<MazePiece>();

        static (int dx, int dy) Delta(Dir d) => d switch
        {
            Dir.N => (0, -1), Dir.S => (0, 1), Dir.E => (1, 0), _ => (-1, 0)
        };

        foreach (var p in data.Pieces)
        {
            Dir openings = PieceDB.GetOpenings(p.Type, p.Rotation);

            foreach (Dir dir in new[] { Dir.N, Dir.S, Dir.E, Dir.W })
            {
                if ((openings & dir) == 0) continue;

                var (dx, dy) = Delta(dir);
                Dir opp      = PieceDB.Opposite(dir);

                bool isCross = PieceDB.IsStair(p.Type) &&
                               dir == PieceDB.GetStairCrossDir(p.Type, p.Rotation);

                int targetFloor = isCross
                    ? (p.Type == PieceType.StairsDown ? p.Floor - 1 : p.Floor + 1)
                    : p.Floor;

                var nk = (p.X + dx, p.Y + dy, targetFloor);
                if (!lookup.TryGetValue(nk, out var neighbor)) continue;

                bool compatible = isCross
                    ? (PieceDB.GetOpenings(neighbor.Type, neighbor.Rotation) & opp) != 0
                    : PieceDB.HasSameFloorOpening(neighbor.Type, neighbor.Rotation, opp);

                if (!compatible) continue;

                var pk = (p.X, p.Y, p.Floor);
                if (!adj[pk].Contains(neighbor)) adj[pk].Add(neighbor);
                if (!adj[nk].Contains(p))        adj[nk].Add(p);
            }
        }

        return adj;
    }
}
