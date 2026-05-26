using System.Collections.Generic;
using System.Linq;

/// res://scripts/MazeValidator.cs — Game-readiness validation for a MazeData.
/// A maze is "game-ready" when it has a Start, an Exit, and BFS from Start reaches Exit
/// (and every other placed piece) through valid same-floor or cross-floor stair connections.
public static class MazeValidator
{
    static readonly Dir[]       AllDirs  = { Dir.N, Dir.E, Dir.S, Dir.W };
    static readonly (int, int)[] DirDelta = { (0, -1), (1, 0), (0, 1), (-1, 0) };
    static readonly Dir[]       Opposite = { Dir.S, Dir.W, Dir.N, Dir.E };

    /// Validates `maze` and reports the reason it is not game-ready (if any).
    /// Returns true only when Start exists, Exit exists, and the BFS from Start visits
    /// every piece in the maze (including Exit).
    public static bool IsGameReady(MazeData? maze, out string error)
    {
        error = "";
        if (maze == null || maze.Pieces == null || maze.Pieces.Count == 0)
        {
            error = "Maze is empty";
            return false;
        }

        var start = maze.Pieces.FirstOrDefault(p => p.Type == PieceType.Start);
        if (start == null) { error = "No Start piece"; return false; }

        var exit = maze.Pieces.FirstOrDefault(p => p.Type == PieceType.Exit);
        if (exit == null) { error = "No Exit piece"; return false; }

        var lookup = new Dictionary<(int, int, int), MazePiece>();
        foreach (var p in maze.Pieces) lookup[(p.X, p.Y, p.Floor)] = p;

        var visited = new HashSet<(int, int, int)>();
        var queue   = new Queue<MazePiece>();
        queue.Enqueue(start);
        visited.Add((start.X, start.Y, start.Floor));

        while (queue.Count > 0)
        {
            var piece = queue.Dequeue();
            Dir open  = PieceDB.GetOpenings(piece.Type, piece.Rotation);

            for (int i = 0; i < AllDirs.Length; i++)
            {
                Dir dir = AllDirs[i];
                if ((open & dir) == 0) continue;

                var (dx, dy) = DirDelta[i];
                Dir opp      = Opposite[i];

                // Stair pieces cross floors on their cross-dir face
                int nFloor = piece.Floor;
                if (PieceDB.IsStair(piece.Type) &&
                    dir == PieceDB.GetStairCrossDir(piece.Type, piece.Rotation))
                    nFloor = piece.Floor + PieceDB.StairFloorDelta(piece.Type);

                int nx = piece.X + dx, ny = piece.Y + dy;
                var key = (nx, ny, nFloor);
                if (visited.Contains(key)) continue;
                if (!lookup.TryGetValue(key, out var nb)) continue;

                // Cannot enter a stair from its cross-floor face at the same floor level
                if (PieceDB.IsStair(nb.Type) &&
                    opp == PieceDB.GetStairCrossDir(nb.Type, nb.Rotation))
                    continue;

                if ((PieceDB.GetOpenings(nb.Type, nb.Rotation) & opp) == 0) continue;

                visited.Add(key);
                queue.Enqueue(nb);
            }
        }

        if (!visited.Contains((exit.X, exit.Y, exit.Floor)))
        {
            error = "No path from Start to Exit";
            return false;
        }

        var unreached = maze.Pieces.Where(p => !visited.Contains((p.X, p.Y, p.Floor))).ToList();
        if (unreached.Count > 0)
        {
            var sample = unreached[0];
            error = $"{unreached.Count} piece(s) unreachable from Start " +
                    $"(e.g. {PieceDB.Labels[sample.Type]} at ({sample.X},{sample.Y},F{sample.Floor}))";
            return false;
        }

        return true;
    }
}
