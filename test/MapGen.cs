// test/MapGen.cs
// Two-phase dungeon generator: Kruskal backbone + score-driven densification.
// Phase 1: Union-Find spanning tree guarantees a connected backbone of >=30 nodes.
// Phase 2: Score-based frontier expansion fills the grid up to 85% density or budget cap.
// Post-processing: dead-end extension, loop injection, stair validation, exit placement.

using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

[Tool]
public static class MapGen
{
    // ── Grid constants ─────────────────────────────────────────────────────────
    const int GridW     = 10;
    const int GridH     = 10;
    const int MaxFloors = 5;    // floors -2, -1, 0, 1, 2
    const int FloorMin  = -2;
    const int FloorMax  = 2;

    // GetMaxBudget: 10 * 10 * 5 * 50 = 25000g
    public static int GetMaxBudget() => GridW * GridH * MaxFloors * 50;

    static readonly Dir[]             AllDirs  = { Dir.N, Dir.E, Dir.S, Dir.W };
    static readonly (int dx, int dy)[] DirDelta = { (0,-1),(1,0),(0,1),(-1,0) };
    static readonly Dir[]             OppDirs  = { Dir.S, Dir.W, Dir.N, Dir.E };

    // ── Public result type ─────────────────────────────────────────────────────
    public record GenerateResult(bool Success, string Message, MazeData? Data);

    // ── Entry points ──────────────────────────────────────────────────────────
    public static GenerateResult GenerateSlot(int slot)
        => GenerateSlot(slot, GetMaxBudget() / 2);

    public static GenerateResult GenerateSlot(int slot, int targetBudget)
    {
        targetBudget = Math.Clamp(targetBudget, 0, GetMaxBudget());
        int seed = unchecked((int)System.Environment.TickCount64);
        MazeData? best      = null;
        int       bestScore = -1;
        int       bestGold  = 0;

        for (int attempt = 0; attempt < 5; attempt++)
        {
            var rng  = new Random(seed + attempt * 1_000_003);
            int goldSpent = 0;
            var data = BuildMap(rng, slot, targetBudget, out goldSpent);
            if (data == null) continue;

            var violations = Validate(data, targetBudget);
            int score = data.Pieces.Count - violations.Count * 10;

            if (violations.Count == 0)
            {
                MazeSerializer.Save(slot, data);
                float pct = targetBudget > 0 ? (goldSpent / (float)targetBudget * 100f) : 0f;
                return new GenerateResult(true,
                    $"Generated {data.Pieces.Count} pieces  {goldSpent}g / {targetBudget}g budget ({pct:F1}%)", data);
            }

            if (score > bestScore) { bestScore = score; best = data; bestGold = goldSpent; }
        }

        if (best != null)
        {
            MazeSerializer.Save(slot, best);
            var v = Validate(best, targetBudget);
            float pct2 = targetBudget > 0 ? (bestGold / (float)targetBudget * 100f) : 0f;
            return new GenerateResult(false,
                $"Saved best ({best.Pieces.Count} pcs, {bestGold}g/{targetBudget}g ({pct2:F1}%), {v.Count} issues: {string.Join("; ", v.Take(2))})", best);
        }

        return new GenerateResult(false, "Generation failed after 5 attempts", null);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  UNION-FIND
    // ══════════════════════════════════════════════════════════════════════════
    class UnionFind
    {
        Dictionary<(int,int,int), (int,int,int)> parent = new();
        Dictionary<(int,int,int), int>           rank   = new();

        (int,int,int) Find((int,int,int) x)
        {
            if (!parent.ContainsKey(x)) { parent[x] = x; rank[x] = 0; }
            if (parent[x].Equals(x)) return x;
            parent[x] = Find(parent[x]);
            return parent[x];
        }

        public bool Connected((int,int,int) a, (int,int,int) b) => Find(a).Equals(Find(b));

        public bool Union((int,int,int) a, (int,int,int) b)
        {
            var ra = Find(a); var rb = Find(b);
            if (ra.Equals(rb)) return false;
            if (!rank.ContainsKey(ra)) rank[ra] = 0;
            if (!rank.ContainsKey(rb)) rank[rb] = 0;
            if (rank[ra] < rank[rb]) { var t = ra; ra = rb; rb = t; }
            parent[rb] = ra;
            if (rank[ra] == rank[rb]) rank[ra]++;
            return true;
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  HELPERS
    // ══════════════════════════════════════════════════════════════════════════
    static bool InBounds(int x, int y, int floor) =>
        x >= 0 && x < GridW && y >= 0 && y < GridH &&
        floor >= FloorMin && floor <= FloorMax;

    static Dir OppDir(Dir d)
    {
        int i = Array.IndexOf(AllDirs, d);
        return OppDirs[i];
    }

    static int CountBits(int n)
    {
        int c = 0; while (n != 0) { c += n & 1; n >>= 1; } return c;
    }

    // ── Reserved-cell set: all shadow floors of placed stair pieces ──────────
    static System.Collections.Generic.HashSet<(int,int,int)> ComputeReserved(
        Dictionary<(int,int,int), MazePiece> grid)
    {
        var reserved = new System.Collections.Generic.HashSet<(int,int,int)>();
        foreach (var (key, piece) in grid)
        {
            if (!PieceDB.IsStair(piece.Type)) continue;
            int shadowFloor = key.Item3 + PieceDB.StairFloorDelta(piece.Type);
            reserved.Add((key.Item1, key.Item2, shadowFloor));
        }
        return reserved;
    }

    // ── Upgrade piece type to include an additional opening direction ──────────
    static PieceType? UpgradePieceForOpenings(PieceType current, int currentRot, Dir addDir,
                                               out int newRot)
    {
        newRot = currentRot;
        Dir existing = PieceDB.GetOpenings(current, currentRot);
        if ((existing & addDir) != 0) return current;

        Dir desired = existing | addDir;
        int desiredBits = CountBits((int)desired);

        PieceType[] flatTypes = { PieceType.Straight, PieceType.LHall, PieceType.THall, PieceType.Cross };
        foreach (var pt in flatTypes)
        {
            for (int r = 0; r < 4; r++)
            {
                Dir open = PieceDB.GetOpenings(pt, r);
                if ((open & desired) == desired && CountBits((int)open) == desiredBits)
                {
                    newRot = r;
                    return pt;
                }
            }
        }
        newRot = 0;
        return PieceType.Cross;
    }

    // ── Choose the minimal piece type that covers the needed openings ─────────
    static (PieceType type, int rot) ChoosePieceForOpenings(Dir needed)
    {
        PieceType[] candidates = { PieceType.Straight, PieceType.LHall, PieceType.THall, PieceType.Cross };
        foreach (var pt in candidates)
        {
            for (int r = 0; r < 4; r++)
            {
                Dir open = PieceDB.GetOpenings(pt, r);
                if ((open & needed) == needed)
                    return (pt, r);
            }
        }
        return (PieceType.Straight, 0);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  CORE GENERATOR
    // ══════════════════════════════════════════════════════════════════════════
    static MazeData? BuildMap(Random rng, int slot, int targetBudget, out int goldSpentOut)
    {
        goldSpentOut = 0;
        var grid       = new Dictionary<(int,int,int), MazePiece>();
        var usedTypes  = new HashSet<PieceType>();
        int crossCount = 0;
        int stairCount = 0;
        int goldSpent  = 0;

        // ─────────────────────────────────────────────────────────────────────
        //  PHASE 1: BACKBONE (Kruskal on shuffled flat edges, floor 0 only)
        // ─────────────────────────────────────────────────────────────────────

        int startX = rng.Next(2, 8);
        // Start piece costs 0g
        grid[(startX, 0, 0)] = new MazePiece { Type = PieceType.Start, X = startX, Y = 0, Floor = 0, Rotation = 0 };
        usedTypes.Add(PieceType.Start);

        // Enumerate all horizontal flat edges for floor 0
        // Valid cell rows: 0..GridH-2 (row GridH-1 is exit zone, skip)
        var flatEdges = new List<(int x1, int y1, int x2, int y2, Dir dir)>();
        for (int x = 0; x < GridW; x++)
        for (int y = 0; y <= GridH - 2; y++)
        {
            if (x + 1 < GridW)
                flatEdges.Add((x, y, x+1, y, Dir.E));
            if (y + 1 <= GridH - 2)
                flatEdges.Add((x, y, x, y+1, Dir.S));
        }
        Shuffle(flatEdges, rng);

        var uf = new UnionFind();
        uf.Union((startX, 0, 0), (startX, 0, 0));

        var backboneConns  = new Dictionary<(int,int,int), Dir>();
        var backboneNodes  = new HashSet<(int,int,int)> { (startX, 0, 0) };
        backboneConns[(startX, 0, 0)] = Dir.None;

        const int MinBackboneNodes = 30;

        foreach (var (x1, y1, x2, y2, dir) in flatEdges)
        {
            if (backboneNodes.Count >= MinBackboneNodes) break;
            if (y1 == GridH - 1 || y2 == GridH - 1) continue;

            var a = (x1, y1, 0);
            var b = (x2, y2, 0);

            bool aIn = backboneNodes.Contains(a);
            bool bIn = backboneNodes.Contains(b);

            if (!aIn && !bIn) continue;
            if (aIn && bIn && uf.Connected(a, b)) continue;

            // Budget check for backbone pieces
            PieceType ptA = (a == (startX, 0, 0)) ? PieceType.Start : PieceType.Straight;
            PieceType ptB = PieceType.Straight;
            int costA = aIn ? 0 : PieceDB.GoldCosts[ptA];
            int costB = bIn ? 0 : PieceDB.GoldCosts[ptB];
            if (goldSpent + costA + costB > targetBudget && targetBudget > 0) continue;

            if (!aIn)
            {
                backboneNodes.Add(a);
                if (a != (startX, 0, 0)) goldSpent += costA;
            }
            if (!bIn)
            {
                backboneNodes.Add(b);
                goldSpent += costB;
            }

            uf.Union(a, b);

            if (!backboneConns.ContainsKey(a)) backboneConns[a] = Dir.None;
            if (!backboneConns.ContainsKey(b)) backboneConns[b] = Dir.None;
            backboneConns[a] |= dir;
            backboneConns[b] |= OppDir(dir);
        }

        // Assign piece types from connection masks
        foreach (var (key, conns) in backboneConns)
        {
            if (key == (startX, 0, 0)) continue;
            if (key.Item2 == 0) continue; // row 0 is Start-only

            int connCount = CountBits((int)conns);
            if (connCount == 0) continue;

            var (ptype, prot) = ChoosePieceForOpenings(conns);
            // Adjust gold: backbone already counted Straight cost, upgrade if needed
            int oldCost = PieceDB.GoldCosts[PieceType.Straight];
            int newCost = PieceDB.GoldCosts[ptype];
            goldSpent += (newCost - oldCost); // delta from Straight assumption
            grid[key] = new MazePiece { Type = ptype, X = key.Item1, Y = key.Item2, Floor = key.Item3, Rotation = prot };
            usedTypes.Add(ptype);
            if (ptype == PieceType.Cross) crossCount++;
        }
        // Clamp goldSpent in case delta went negative (L-halls cheaper? no — Straight=10, LHall=20, so always >=)
        if (goldSpent < 0) goldSpent = 0;

        // Vertical mode declared here so backbone injection can set it
        bool verticalMode      = false;
        int  verticalBudget    = 0;

        // ── Fix 1: Force backbone stair edges so every floor is seeded ──────────
        // After Kruskal (floor 0 only), inject stair edges for all non-zero floors
        // if they have no backbone nodes yet.
        // Upper floors (1, 2): inject StairsUp from srcFloor = forceFloor - 1
        // Lower floors (-1, -2): inject StairsDown from srcFloor = forceFloor + 1
        // Iterate upward floors ascending (1, 2) and downward floors descending (-1, -2)
        // so each seeded floor is available when the next one is processed.
        {
            // Upward: floors 1, 2
            for (int forceFloor = 1; forceFloor <= FloorMax; forceFloor++)
            {
                bool hasNodes = grid.Keys.Any(k => k.Item3 == forceFloor);
                if (hasNodes) continue;

                int srcFloor = forceFloor - 1;
                // Candidate source cells on srcFloor that can host a StairsUp
                var srcCells = grid.Keys
                    .Where(k => k.Item3 == srcFloor && k.Item2 >= 2 && k.Item2 <= GridH - 3)
                    .OrderBy(_ => rng.Next())
                    .ToList();

                bool injected = false;
                foreach (var src in srcCells)
                {
                    // StairsUp at rot=0: flat=S (same-floor exit), cross=N (goes to forceFloor)
                    // Landing cell is at (src.x, src.y - 1, forceFloor)
                    int landX = src.Item1, landY = src.Item2 - 1;
                    if (landY < 1 || landY >= GridH - 1) continue;

                    var stairKey = (src.Item1, src.Item2, srcFloor);
                    var landKey  = (landX, landY, forceFloor);

                    if (grid.ContainsKey(landKey)) continue;
                    // Skip if the landing cell is already reserved by another stair's shadow
                    if (ComputeReserved(grid).Contains(landKey)) continue;
                    // Skip if the stair's own shadow floor cell is already occupied
                    if (grid.ContainsKey((src.Item1, src.Item2, forceFloor))) continue;
                    // Reject side-by-side stair: no same-floor cardinal neighbor may be a stair
                    bool bbUpAdj = false;
                    for (int dbi = 0; dbi < 4 && !bbUpAdj; dbi++) {
                        var (adx, ady) = DirDelta[dbi];
                        if (grid.TryGetValue((src.Item1 + adx, src.Item2 + ady, srcFloor), out var adjBbUp) && PieceDB.IsStair(adjBbUp.Type))
                            bbUpAdj = true;
                    }
                    if (bbUpAdj) continue;

                    int stairCost = PieceDB.GoldCosts[PieceType.StairsUp];
                    int landCost  = PieceDB.GoldCosts[PieceType.Straight];
                    if (targetBudget > 0 && goldSpent + stairCost + landCost > targetBudget) continue;

                    // Refund current piece at stairKey if present (replacing it with StairsUp)
                    if (grid.TryGetValue(stairKey, out var existingStairCell))
                        goldSpent = Math.Max(0, goldSpent - PieceDB.GoldCosts[existingStairCell.Type]);

                    goldSpent += stairCost;
                    grid[stairKey] = new MazePiece
                    {
                        Type = PieceType.StairsUp, X = src.Item1, Y = src.Item2,
                        Floor = srcFloor, Rotation = 0
                    };
                    usedTypes.Add(PieceType.StairsUp);
                    stairCount++;

                    goldSpent += landCost;
                    grid[landKey] = new MazePiece
                    {
                        Type = PieceType.Straight, X = landX, Y = landY,
                        Floor = forceFloor, Rotation = 0
                    };
                    usedTypes.Add(PieceType.Straight);

                    injected = true;
                    verticalMode   = true;
                    verticalBudget = 80; // big head-start for upper-floor fill
                    break;
                }
                if (!injected)
                    GD.Print($"[MapGen] WARNING: could not force backbone stair to floor {forceFloor}");
            }

            // Downward: floors -1, -2
            for (int forceFloor = -1; forceFloor >= FloorMin; forceFloor--)
            {
                bool hasNodes = grid.Keys.Any(k => k.Item3 == forceFloor);
                if (hasNodes) continue;

                int srcFloor = forceFloor + 1; // the floor above that already exists
                // Candidate source cells on srcFloor that can host a StairsDown
                var srcCells = grid.Keys
                    .Where(k => k.Item3 == srcFloor && k.Item2 >= 2 && k.Item2 <= GridH - 3)
                    .OrderBy(_ => rng.Next())
                    .ToList();

                bool injected = false;
                foreach (var src in srcCells)
                {
                    // StairsDown at rot=0: flat=N (same-floor exit faces north), cross=S (goes to floor-1)
                    // CrossDir for StairsDown at rot=0 is Dir.S, so landing is at (src.x, src.y+1, forceFloor)
                    int landX = src.Item1, landY = src.Item2 + 1;
                    if (landY < 1 || landY >= GridH - 1) continue;

                    var stairKey = (src.Item1, src.Item2, srcFloor);
                    var landKey  = (landX, landY, forceFloor);

                    if (grid.ContainsKey(landKey)) continue;
                    // Skip if the landing cell is already reserved by another stair's shadow
                    if (ComputeReserved(grid).Contains(landKey)) continue;
                    // Skip if the stair's own shadow floor cell is already occupied
                    if (grid.ContainsKey((src.Item1, src.Item2, forceFloor))) continue;
                    // Reject side-by-side stair: no same-floor cardinal neighbor may be a stair
                    bool bbDnAdj = false;
                    for (int dbi = 0; dbi < 4 && !bbDnAdj; dbi++) {
                        var (adx, ady) = DirDelta[dbi];
                        if (grid.TryGetValue((src.Item1 + adx, src.Item2 + ady, srcFloor), out var adjBbDn) && PieceDB.IsStair(adjBbDn.Type))
                            bbDnAdj = true;
                    }
                    if (bbDnAdj) continue;

                    int stairCost = PieceDB.GoldCosts[PieceType.StairsDown];
                    int landCost  = PieceDB.GoldCosts[PieceType.Straight];
                    if (targetBudget > 0 && goldSpent + stairCost + landCost > targetBudget) continue;

                    // Refund current piece at stairKey if present (replacing it with StairsDown)
                    if (grid.TryGetValue(stairKey, out var existingStairCell))
                        goldSpent = Math.Max(0, goldSpent - PieceDB.GoldCosts[existingStairCell.Type]);

                    goldSpent += stairCost;
                    grid[stairKey] = new MazePiece
                    {
                        Type = PieceType.StairsDown, X = src.Item1, Y = src.Item2,
                        Floor = srcFloor, Rotation = 0
                    };
                    usedTypes.Add(PieceType.StairsDown);
                    stairCount++;

                    goldSpent += landCost;
                    grid[landKey] = new MazePiece
                    {
                        Type = PieceType.Straight, X = landX, Y = landY,
                        Floor = forceFloor, Rotation = 0
                    };
                    usedTypes.Add(PieceType.Straight);

                    injected = true;
                    verticalMode   = true;
                    verticalBudget = 80;
                    break;
                }
                if (!injected)
                    GD.Print($"[MapGen] WARNING: could not force backbone stair to floor {forceFloor}");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  PHASE 2: DENSIFICATION (score-based frontier expansion)
        // ─────────────────────────────────────────────────────────────────────

        var frontier    = new Queue<(int x, int y, int floor)>();
        var frontierSet = new HashSet<(int,int,int)>();

        void EnqueueFrontier(int fx, int fy, int ffl)
        {
            if (!InBounds(fx, fy, ffl)) return;
            if (ffl == 0 && fy == 0) return;   // row 0 reserved for Start
            if (fy == GridH - 1) return;        // row 9 reserved for Exit
            if (grid.ContainsKey((fx, fy, ffl))) return;
            var key = (fx, fy, ffl);
            if (!frontierSet.Add(key)) return;
            frontier.Enqueue(key);
        }

        foreach (var placed in grid.Keys.ToList())
        {
            for (int d = 0; d < 4; d++)
            {
                var (dx, dy) = DirDelta[d];
                EnqueueFrontier(placed.Item1 + dx, placed.Item2 + dy, placed.Item3);
            }
        }

        int  placementsTotal   = 0;

        int maxIter = 800;
        int iter    = 0;

        while (frontier.Count > 0 && iter < maxIter)
        {
            iter++;
            var (cx, cy, cfl) = frontier.Dequeue();
            frontierSet.Remove((cx, cy, cfl));

            if (grid.ContainsKey((cx, cy, cfl))) continue;
            if (!InBounds(cx, cy, cfl)) continue;
            if (cfl == 0 && cy == 0) continue;
            if (cy == GridH - 1) continue;
            // Skip cells reserved as stair shadow floors
            if (ComputeReserved(grid).Contains((cx, cy, cfl))) continue;

            // 85% density cap per floor (usable rows 1-8 = 80 cells; row 0 and row 9 are reserved)
            int floorCount = grid.Count(kv => kv.Key.Item3 == cfl);
            float density = floorCount / (float)(GridW * (GridH - 2));
            if (density >= 0.85f) continue;

            // Determine required and forbidden openings
            Dir requiredOpen  = Dir.None;
            Dir forbiddenOpen = Dir.None;

            for (int d = 0; d < 4; d++)
            {
                Dir dir = AllDirs[d];
                var (dx, dy) = DirDelta[d];
                int nx = cx + dx, ny = cy + dy;

                if (!InBounds(nx, ny, cfl)) { forbiddenOpen |= dir; continue; }

                if (grid.TryGetValue((nx, ny, cfl), out var nb))
                {
                    Dir nbOpen = PieceDB.GetOpenings(nb.Type, nb.Rotation);
                    Dir nbBack = OppDirs[d];
                    if ((nbOpen & nbBack) != 0) requiredOpen  |= dir;
                    else                        forbiddenOpen |= dir;
                }
            }

            if (requiredOpen == Dir.None) continue;

            var candidates = new List<(PieceType type, int rot, int score)>();

            // ── Stair candidates (when conditions allow) ───────────────────────
            bool tryStairs = stairCount < 20 && (cfl < FloorMax || cfl > FloorMin) && cy > 1 && cy < GridH - 2;
            if (tryStairs)
            {
                TryAddStairCandidates(cx, cy, cfl, requiredOpen, forbiddenOpen,
                    grid, candidates, verticalMode, stairCount, goldSpent, targetBudget);
            }

            // ── Flat piece candidates ──────────────────────────────────────────
            PieceType[] flatTypes = { PieceType.Cross, PieceType.THall, PieceType.LHall, PieceType.Straight };
            foreach (var pt in flatTypes)
            {
                int pieceCost = PieceDB.GoldCosts[pt];
                if (targetBudget > 0 && goldSpent + pieceCost > targetBudget) continue;

                for (int rot = 0; rot < 4; rot++)
                {
                    Dir open = PieceDB.GetOpenings(pt, rot);
                    if ((open & requiredOpen) != requiredOpen) continue;
                    if ((open & forbiddenOpen) != 0) continue;

                    int sc = 0;
                    if (!usedTypes.Contains(pt)) sc += 80;
                    if (CountBits((int)open) >= 3 && frontier.Count > 15) sc += 40;
                    if ((pt == PieceType.Straight || pt == PieceType.LHall) && floorCount < 10) sc += 30;
                    if (pt == PieceType.Cross && crossCount >= 2) sc -= 60;
                    if (cfl != 0) sc += 20;

                    // Per-floor underoccupied boost: strongly prefer filling thin floors
                    if (floorCount < 8) sc += 80;

                    // Budget scoring: prefer pieces that keep spending near 90% of target
                    if (targetBudget > 0)
                    {
                        int newGold = goldSpent + pieceCost;
                        float ratio = newGold / (float)targetBudget;
                        if (ratio <= 0.95f) sc += 50;
                        else                sc -= 30;
                    }

                    // Vertical mode: extra bonus for placing on non-ground floors (upper or lower)
                    if (verticalMode && cfl != 0) sc += 40;
                    else if (verticalMode) sc += 10;
                    candidates.Add((pt, rot, sc));
                }
            }

            if (candidates.Count == 0) continue;

            candidates = candidates
                .OrderByDescending(c => c.score)
                .ThenBy(_ => rng.Next())
                .ToList();

            var (chosenType, chosenRot, _) = candidates[0];
            goldSpent += PieceDB.GoldCosts[chosenType];
            grid[(cx, cy, cfl)] = new MazePiece { Type = chosenType, X = cx, Y = cy, Floor = cfl, Rotation = chosenRot };
            usedTypes.Add(chosenType);
            placementsTotal++;

            if (chosenType == PieceType.Cross) crossCount++;

            if (PieceDB.IsStair(chosenType))
            {
                stairCount++;
                // Every stair placement resets vertical mode for 60% of current total + 60 steps
                verticalMode   = true;
                verticalBudget = (int)(placementsTotal * 0.60f) + 60;

                // Front-queue the stair landing cell
                Dir  crossDir = PieceDB.GetStairCrossDir(chosenType, chosenRot);
                int  ci       = Array.IndexOf(AllDirs, crossDir);
                var (cdx, cdy) = DirDelta[ci];
                int  lx = cx + cdx, ly = cy + cdy;
                int  targetFloor = cfl + PieceDB.StairFloorDelta(chosenType);

                if (InBounds(lx, ly, targetFloor) && !grid.ContainsKey((lx, ly, targetFloor))
                    && ly != 0 && ly != GridH - 1)
                {
                    var pk = (lx, ly, targetFloor);
                    if (!frontierSet.Contains(pk))
                    {
                        frontierSet.Add(pk);
                        var rest = frontier.ToList();
                        frontier.Clear();
                        frontier.Enqueue(pk);
                        foreach (var item in rest) frontier.Enqueue(item);
                    }
                }
            }
            else if (verticalMode)
            {
                verticalBudget--;
                if (verticalBudget <= 0) verticalMode = false;
            }

            // Expand neighbors of the new piece
            Dir newOpen = PieceDB.GetOpenings(chosenType, chosenRot);
            for (int d = 0; d < 4; d++)
            {
                if ((newOpen & AllDirs[d]) == 0) continue;
                var (ddx, ddy) = DirDelta[d];
                int nfl = cfl;
                if (PieceDB.IsStair(chosenType) && AllDirs[d] == PieceDB.GetStairCrossDir(chosenType, chosenRot))
                    nfl = cfl + PieceDB.StairFloorDelta(chosenType);
                EnqueueFrontier(cx + ddx, cy + ddy, nfl);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  GREEDY FILL: if goldSpent < targetBudget * 0.85, keep adding pieces
        //  Strategy: iterate floors 2, 1, 0 (upper floors first) trying to
        //  fill each to at least 15 pieces, then do a full-grid scan to maximize
        //  spend. Pick the most expensive valid piece each time.
        // ─────────────────────────────────────────────────────────────────────
        if (targetBudget > 0 && goldSpent < (int)(targetBudget * 0.85f))
        {
            // Helper: attempt to place a piece at (nx, ny, nfl), picking most expensive first
            bool TryPlaceGreedy(int nx, int ny, int nfl)
            {
                if (grid.ContainsKey((nx, ny, nfl))) return false;
                if (!InBounds(nx, ny, nfl)) return false;
                if (nfl == 0 && ny == 0) return false;
                if (ny == GridH - 1) return false;
                if (ComputeReserved(grid).Contains((nx, ny, nfl))) return false;

                Dir req = Dir.None, forb = Dir.None;
                for (int d = 0; d < 4; d++)
                {
                    Dir dir = AllDirs[d];
                    var (ddx2, ddy2) = DirDelta[d];
                    int nnx = nx + ddx2, nny = ny + ddy2;
                    if (!InBounds(nnx, nny, nfl)) { forb |= dir; continue; }
                    if (grid.TryGetValue((nnx, nny, nfl), out var nb2))
                    {
                        Dir nb2Open = PieceDB.GetOpenings(nb2.Type, nb2.Rotation);
                        if ((nb2Open & OppDirs[d]) != 0) req  |= dir;
                        else                             forb |= dir;
                    }
                }
                if (req == Dir.None) return false;

                // Pick most expensive valid piece within remaining budget (most expansive first)
                PieceType[] expensiveFirst = { PieceType.Cross, PieceType.THall, PieceType.LHall, PieceType.Straight };
                foreach (var pt in expensiveFirst)
                {
                    if (goldSpent + PieceDB.GoldCosts[pt] > targetBudget) continue;
                    for (int rot = 0; rot < 4; rot++)
                    {
                        Dir open = PieceDB.GetOpenings(pt, rot);
                        if ((open & req) != req) continue;
                        if ((open & forb) != 0) continue;
                        goldSpent += PieceDB.GoldCosts[pt];
                        grid[(nx, ny, nfl)] = new MazePiece { Type = pt, X = nx, Y = ny, Floor = nfl, Rotation = rot };
                        usedTypes.Add(pt);
                        if (pt == PieceType.Cross) crossCount++;
                        return true;
                    }
                }
                return false;
            }

            // Pass 1: per-floor fill (floors 2, 1, 0, -1, -2) — try to reach 15 pieces on each floor
            foreach (int targetFloor in new[] { 2, 1, 0, -1, -2 })
            {
                int safetyIter = 0;
                while (goldSpent < (int)(targetBudget * 0.85f) && safetyIter++ < 500)
                {
                    int fc = grid.Count(kv => kv.Key.Item3 == targetFloor);
                    if (fc >= 15) break;

                    bool anyPlaced = false;
                    // Collect all empty neighbor cells on this floor
                    var candidates2 = new List<(int x, int y, int fl)>();
                    foreach (var (key, _) in grid)
                    {
                        if (key.Item3 != targetFloor) continue;
                        for (int d = 0; d < 4; d++)
                        {
                            var (dx2, dy2) = DirDelta[d];
                            int nx2 = key.Item1 + dx2, ny2 = key.Item2 + dy2;
                            if (!InBounds(nx2, ny2, targetFloor)) continue;
                            if (targetFloor == 0 && ny2 == 0) continue;
                            if (ny2 == GridH - 1) continue;
                            if (!grid.ContainsKey((nx2, ny2, targetFloor)))
                                candidates2.Add((nx2, ny2, targetFloor));
                        }
                    }
                    candidates2 = candidates2.Distinct().ToList();
                    Shuffle(candidates2, rng);
                    foreach (var (nx2, ny2, nfl2) in candidates2)
                    {
                        if (TryPlaceGreedy(nx2, ny2, nfl2)) { anyPlaced = true; break; }
                    }
                    if (!anyPlaced) break;
                }
            }

            // Pass 2: full-grid aggressive scan across ALL cells on all floors
            if (goldSpent < (int)(targetBudget * 0.85f))
            {
                var allEmpty = new List<(int x, int y, int fl)>();
                foreach (var (key, _) in grid)
                {
                    for (int d = 0; d < 4; d++)
                    {
                        var (dx, dy) = DirDelta[d];
                        int nx = key.Item1 + dx, ny = key.Item2 + dy, nfl = key.Item3;
                        if (!InBounds(nx, ny, nfl)) continue;
                        if (nfl == 0 && ny == 0) continue;
                        if (ny == GridH - 1) continue;
                        if (!grid.ContainsKey((nx, ny, nfl)))
                            allEmpty.Add((nx, ny, nfl));
                    }
                }
                // Also add all empty cells adjacent to stair landings on all non-ground floors
                for (int fl = FloorMin; fl <= FloorMax; fl++)
                {
                    for (int x = 0; x < GridW; x++)
                    for (int y = 1; y <= GridH - 2; y++)
                    {
                        if (!grid.ContainsKey((x, y, fl))) continue;
                        for (int d = 0; d < 4; d++)
                        {
                            var (dx, dy) = DirDelta[d];
                            int nx = x + dx, ny2 = y + dy;
                            if (!InBounds(nx, ny2, fl)) continue;
                            if (ny2 == 0 || ny2 == GridH - 1) continue;
                            if (!grid.ContainsKey((nx, ny2, fl)))
                                allEmpty.Add((nx, ny2, fl));
                        }
                    }
                }

                allEmpty = allEmpty.Distinct().ToList();
                // Sort: upper floors first, then shuffle within floor
                allEmpty = allEmpty
                    .OrderByDescending(c => c.fl)
                    .ThenBy(_ => rng.Next())
                    .ToList();

                bool anyProgress = true;
                while (anyProgress && goldSpent < (int)(targetBudget * 0.85f))
                {
                    anyProgress = false;
                    foreach (var (nx, ny, nfl) in allEmpty.ToList())
                    {
                        if (goldSpent >= targetBudget) break;
                        if (TryPlaceGreedy(nx, ny, nfl)) anyProgress = true;
                    }
                    // Refresh list
                    allEmpty = allEmpty.Where(c => !grid.ContainsKey((c.x, c.y, c.fl))).ToList();
                }
            }

            // Report if still underspent
            if (goldSpent < (int)(targetBudget * 0.85f))
            {
                int emptyNeighbors = 0;
                bool gridSat = true;
                foreach (var (key, _) in grid)
                {
                    for (int d = 0; d < 4; d++)
                    {
                        var (dx, dy) = DirDelta[d];
                        int nx = key.Item1 + dx, ny = key.Item2 + dy, nfl = key.Item3;
                        if (!InBounds(nx, ny, nfl)) continue;
                        if (ny == 0 || ny == GridH - 1) continue;
                        if (!grid.ContainsKey((nx, ny, nfl))) { emptyNeighbors++; gridSat = false; }
                    }
                }
                GD.Print($"[MapGen] BudgetGap: spent {goldSpent}/{targetBudget} ({goldSpent*100/targetBudget}%). " +
                         $"EmptyNeighbors={emptyNeighbors} GridSaturated={gridSat}");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  POST-PROCESS: Dead-end extension
        // ─────────────────────────────────────────────────────────────────────
        ExtendDeadEnds(grid, usedTypes, rng, ref goldSpent, targetBudget);

        // ─────────────────────────────────────────────────────────────────────
        //  POST-PROCESS: Loop injection (up to 20 loops)
        // ─────────────────────────────────────────────────────────────────────
        InjectLoops(grid, rng, 20);

        // ─────────────────────────────────────────────────────────────────────
        //  POST-PROCESS: Stair validation
        // ─────────────────────────────────────────────────────────────────────
        ValidateAndFixStairs(grid, usedTypes, ref goldSpent);

        // ─────────────────────────────────────────────────────────────────────
        //  EXIT PLACEMENT
        // ─────────────────────────────────────────────────────────────────────
        var bfsDistances = BfsDistances(grid);
        if (bfsDistances.Count == 0) { goldSpentOut = goldSpent; return null; }

        (int exitCol, int exitFloorChosen) = PickExitAnchor(bfsDistances, startX, grid);
        EnsureReachableSpine(grid, exitCol, exitFloorChosen, bfsDistances, usedTypes, ref goldSpent);

        // Ensure row GridH-2 opens south toward exit
        var rowAboveKey = (exitCol, GridH - 2, exitFloorChosen);
        if (grid.TryGetValue(rowAboveKey, out var abovePiece))
        {
            Dir aboveOpen = PieceDB.GetOpenings(abovePiece.Type, abovePiece.Rotation);
            if ((aboveOpen & Dir.S) == 0)
                grid[rowAboveKey] = new MazePiece { Type = PieceType.Straight, X = exitCol, Y = GridH - 2, Floor = exitFloorChosen, Rotation = 0 };
        }
        else
        {
            grid[rowAboveKey] = new MazePiece { Type = PieceType.Straight, X = exitCol, Y = GridH - 2, Floor = exitFloorChosen, Rotation = 0 };
            usedTypes.Add(PieceType.Straight);
            goldSpent += PieceDB.GoldCosts[PieceType.Straight];
        }

        // Remove any old Exit pieces
        foreach (var oldExit in grid.Where(kv => kv.Value.Type == PieceType.Exit).Select(kv => kv.Key).ToList())
            grid.Remove(oldExit);

        var exitKey = (exitCol, GridH - 1, exitFloorChosen);
        grid[exitKey] = new MazePiece { Type = PieceType.Exit, X = exitCol, Y = GridH - 1, Floor = exitFloorChosen, Rotation = 0 };
        usedTypes.Add(PieceType.Exit);

        // ─────────────────────────────────────────────────────────────────────
        //  BFS PRUNE: remove unreachable pieces
        // ─────────────────────────────────────────────────────────────────────
        bool pruned = true;
        while (pruned)
        {
            var reachable = BfsReachable(grid);
            pruned = false;
            foreach (var key in grid.Keys.ToList())
            {
                if (reachable.Contains(key)) continue;
                var pp = grid[key];
                if (pp.Type == PieceType.Start) continue;
                // Reclaim gold from pruned pieces
                goldSpent = Math.Max(0, goldSpent - PieceDB.GoldCosts[pp.Type]);
                grid.Remove(key);
                pruned = true;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Final reachability check
        // ─────────────────────────────────────────────────────────────────────
        if (!BfsReachesExit(grid, out int bfsDist, out int reachableCount))
        {
            goldSpentOut = goldSpent;
            return null;
        }

        GD.Print($"[MapGen] slot={slot} pieces={grid.Count} reachable={reachableCount} exitDist={bfsDist} gold={goldSpent}/{targetBudget}");

        goldSpentOut = goldSpent;
        var data = new MazeData
        {
            Name      = $"Gen-{slot}",
            GoldSpent = goldSpent,
            Pieces    = grid.Values.ToList(),
        };
        return data;
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  STAIR DENSIFICATION HELPER
    // ══════════════════════════════════════════════════════════════════════════
    static void TryAddStairCandidates(
        int cx, int cy, int cfl,
        Dir requiredOpen, Dir forbiddenOpen,
        Dictionary<(int,int,int), MazePiece> grid,
        List<(PieceType type, int rot, int score)> candidates,
        bool verticalMode,
        int stairCount,
        int goldSpent,
        int targetBudget)
    {
        int stairCost = PieceDB.GoldCosts[PieceType.StairsUp];
        if (targetBudget > 0 && goldSpent + stairCost > targetBudget) return;

        PieceType[] stairTypes = { PieceType.StairsUp, PieceType.StairsDown };
        foreach (var st in stairTypes)
        {
            if (st == PieceType.StairsDown && cfl <= FloorMin) continue;
            if (st == PieceType.StairsUp   && cfl >= FloorMax) continue;

            int delta       = PieceDB.StairFloorDelta(st);
            int targetFloor = cfl + delta;

            for (int rot = 0; rot < 4; rot++)
            {
                Dir open = PieceDB.GetOpenings(st, rot);
                if ((open & requiredOpen) != requiredOpen) continue;
                if ((open & forbiddenOpen) != 0) continue;

                Dir crossDir = PieceDB.GetStairCrossDir(st, rot);
                int ci = Array.IndexOf(AllDirs, crossDir);
                var (cdx, cdy) = DirDelta[ci];
                int lx = cx + cdx, ly = cy + cdy;

                if (!InBounds(lx, ly, targetFloor)) continue;
                if (ly <= 0 || ly >= GridH - 1) continue;
                if (grid.ContainsKey((lx, ly, targetFloor))) continue;
                if (grid.ContainsKey((cx, cy, targetFloor))) continue;
                // The stair's shadow floor at (cx, cy, targetFloor) must not be reserved
                var reserved = ComputeReserved(grid);
                if (reserved.Contains((lx, ly, targetFloor))) continue;
                if (reserved.Contains((cx, cy, targetFloor))) continue;

                // Reject side-by-side stair: no same-floor cardinal neighbor may be a stair
                bool adjStairFound = false;
                for (int di2 = 0; di2 < 4 && !adjStairFound; di2++)
                {
                    var (adx2, ady2) = DirDelta[di2];
                    if (grid.TryGetValue((cx + adx2, cy + ady2, cfl), out var adjPc) && PieceDB.IsStair(adjPc.Type))
                        adjStairFound = true;
                }
                if (adjStairFound) continue;

                int sc = 60;
                if (verticalMode) sc += 80;
                if (stairCount < 2) sc += 30;
                // No penalty for high stair counts — let the grid and budget decide
                if (cfl != 0) sc += 20; // bonus for placing stairs on non-ground floors

                // Budget scoring for stairs
                if (targetBudget > 0)
                {
                    float ratio = (goldSpent + stairCost) / (float)targetBudget;
                    if (ratio <= 0.95f) sc += 50;
                    else                sc -= 30;
                }

                candidates.Add((st, rot, sc));
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  BFS WITH DISTANCES
    // ══════════════════════════════════════════════════════════════════════════
    static Dictionary<(int,int,int), int> BfsDistances(Dictionary<(int,int,int), MazePiece> grid)
    {
        var startPiece = grid.Values.FirstOrDefault(p => p.Type == PieceType.Start);
        if (startPiece == null) return new Dictionary<(int,int,int), int>();

        var dist  = new Dictionary<(int,int,int), int>();
        var queue = new Queue<(int,int,int)>();
        var startKey = (startPiece.X, startPiece.Y, startPiece.Floor);
        dist[startKey] = 0;
        queue.Enqueue(startKey);

        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            var curPiece = grid[cur];
            Dir open = PieceDB.GetOpenings(curPiece.Type, curPiece.Rotation);

            for (int d = 0; d < 4; d++)
            {
                Dir dir = AllDirs[d];
                if ((open & dir) == 0) continue;

                var (dx, dy) = DirDelta[d];
                Dir back = OppDirs[d];
                int nfl  = cur.Item3;

                if (PieceDB.IsStair(curPiece.Type) && dir == PieceDB.GetStairCrossDir(curPiece.Type, curPiece.Rotation))
                    nfl = cur.Item3 + PieceDB.StairFloorDelta(curPiece.Type);

                var nKey = (cur.Item1 + dx, cur.Item2 + dy, nfl);
                if (dist.ContainsKey(nKey)) continue;
                if (!grid.TryGetValue(nKey, out var nb)) continue;
                if ((PieceDB.GetOpenings(nb.Type, nb.Rotation) & back) == 0) continue;

                dist[nKey] = dist[cur] + 1;
                queue.Enqueue(nKey);
            }
        }
        return dist;
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  SPINE VIABILITY
    // ══════════════════════════════════════════════════════════════════════════
    // Returns true when no stair shadow cell sits in rows 1..GridH-2 at (col, floor).
    // If a shadow cell blocks those rows the exit spine cannot be carved through there.
    static bool IsSpineViable(int col, int floor,
        Dictionary<(int,int,int), MazePiece> grid)
    {
        var reserved = ComputeReserved(grid);
        for (int y = 1; y <= GridH - 2; y++)
            if (reserved.Contains((col, y, floor))) return false;
        return true;
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  PICK EXIT ANCHOR
    // ══════════════════════════════════════════════════════════════════════════
    static (int col, int floor) PickExitAnchor(
        Dictionary<(int,int,int), int> bfsDist,
        int startX,
        Dictionary<(int,int,int), MazePiece> grid)
    {
        // Prefer exit on floors 0 or 1 (findable by player), fall back to any floor.
        // Skip cells whose column+floor has a stair shadow that would block the spine.
        // Such cells are "backward up-connections" — dead ends, not valid exit anchors.
        int bestDistPreferred = -1;
        int bestDistAny       = -1;
        (int col, int floor) bestPreferred = (startX, 0);
        (int col, int floor) bestAny       = (startX, 0);

        foreach (var (key, d) in bfsDist)
        {
            var (x, y, fl) = key;
            if (y < 1 || y > GridH - 2) continue;
            if (!IsSpineViable(x, fl, grid)) continue;

            if (d > bestDistAny)
            {
                bestDistAny = d;
                bestAny = (x, fl);
            }

            if ((fl == 0 || fl == 1) && d > bestDistPreferred)
            {
                bestDistPreferred = d;
                bestPreferred = (x, fl);
            }
        }

        if (bestDistPreferred >= 0) return bestPreferred;
        return bestDistAny < 0 ? (startX, 0) : bestAny;
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  ENSURE REACHABLE SPINE
    // ══════════════════════════════════════════════════════════════════════════
    static void EnsureReachableSpine(
        Dictionary<(int,int,int), MazePiece> grid,
        int col, int floor,
        Dictionary<(int,int,int), int> bfsDist,
        HashSet<PieceType> usedTypes,
        ref int goldSpent)
    {
        int topReachableY = -1;
        foreach (var (key, _) in bfsDist)
        {
            if (key.Item1 == col && key.Item3 == floor && key.Item2 >= 1 && key.Item2 <= GridH - 2)
            {
                if (topReachableY < 0 || key.Item2 < topReachableY)
                    topReachableY = key.Item2;
            }
        }
        if (topReachableY < 0) topReachableY = 1;

        var spineReserved = ComputeReserved(grid);
        for (int y = topReachableY; y <= GridH - 2; y++)
        {
            var key = (col, y, floor);
            if (spineReserved.Contains(key)) break; // stair shadow blocks spine — anchor should have been viable
            if (grid.TryGetValue(key, out var existing))
            {
                bool needsS = (y < GridH - 2);
                bool needsN = (y > topReachableY);
                Dir existOpen = PieceDB.GetOpenings(existing.Type, existing.Rotation);
                if ((needsS && (existOpen & Dir.S) == 0) || (needsN && (existOpen & Dir.N) == 0))
                {
                    goldSpent = Math.Max(0, goldSpent - PieceDB.GoldCosts[existing.Type]);
                    grid[key] = new MazePiece { Type = PieceType.Straight, X = col, Y = y, Floor = floor, Rotation = 0 };
                    goldSpent += PieceDB.GoldCosts[PieceType.Straight];
                    usedTypes.Add(PieceType.Straight);
                }
            }
            else
            {
                grid[key] = new MazePiece { Type = PieceType.Straight, X = col, Y = y, Floor = floor, Rotation = 0 };
                usedTypes.Add(PieceType.Straight);
                goldSpent += PieceDB.GoldCosts[PieceType.Straight];
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  BFS REACHES EXIT
    // ══════════════════════════════════════════════════════════════════════════
    static bool BfsReachesExit(
        Dictionary<(int,int,int), MazePiece> grid,
        out int distToExit,
        out int reachableCount)
    {
        distToExit    = -1;
        reachableCount = 0;

        var exitPiece = grid.Values.FirstOrDefault(p => p.Type == PieceType.Exit);
        if (exitPiece == null) return false;

        var dist = BfsDistances(grid);
        reachableCount = dist.Count;

        var exitKey = (exitPiece.X, exitPiece.Y, exitPiece.Floor);
        if (!dist.TryGetValue(exitKey, out distToExit))
        {
            distToExit = -1;
            return false;
        }
        return true;
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  DEAD-END EXTENSION
    // ══════════════════════════════════════════════════════════════════════════
    static void ExtendDeadEnds(
        Dictionary<(int,int,int), MazePiece> grid,
        HashSet<PieceType> usedTypes,
        Random rng,
        ref int goldSpent,
        int targetBudget)
    {
        var deadEnds = grid.Values
            .Where(p => !PieceDB.IsStair(p.Type) &&
                        p.Type != PieceType.Start &&
                        p.Type != PieceType.Exit &&
                        CountBits((int)PieceDB.GetOpenings(p.Type, p.Rotation)) == 1)
            .ToList();

        foreach (var de in deadEnds)
        {
            Dir open   = PieceDB.GetOpenings(de.Type, de.Rotation);
            Dir extDir = Dir.None;
            for (int d = 0; d < 4; d++)
                if ((open & AllDirs[d]) != 0) { extDir = AllDirs[d]; break; }

            int ex = de.X, ey = de.Y, efl = de.Floor;
            int di = Array.IndexOf(AllDirs, extDir);
            var (ddx, ddy) = DirDelta[di];

            int distToJunction = DistanceToNearestJunction(ex, ey, efl, grid);
            if (distToJunction >= 4) continue;

            int extensions = rng.Next(2, 6); // 2-5 extensions
            for (int ext = 0; ext < extensions; ext++)
            {
                int nx = ex + ddx, ny = ey + ddy;
                if (!InBounds(nx, ny, efl)) break;
                if (ny == 0 || ny == GridH - 1) break;
                if (grid.ContainsKey((nx, ny, efl))) break;

                Dir backDir = OppDirs[di];
                bool isLast = (ext == extensions - 1);
                Dir placedOpen = isLast ? backDir : (backDir | extDir);

                var (ptype, prot) = ChoosePieceForOpenings(placedOpen);
                int cost = PieceDB.GoldCosts[ptype];
                if (targetBudget > 0 && goldSpent + cost > targetBudget) break;

                goldSpent += cost;
                grid[(nx, ny, efl)] = new MazePiece { Type = ptype, X = nx, Y = ny, Floor = efl, Rotation = prot };
                usedTypes.Add(ptype);
                ex = nx; ey = ny;
            }
        }
    }

    static int DistanceToNearestJunction(int sx, int sy, int sfl,
                                          Dictionary<(int,int,int), MazePiece> grid)
    {
        var visited = new HashSet<(int,int,int)>();
        var q = new Queue<((int,int,int) cell, int dist)>();
        q.Enqueue(((sx, sy, sfl), 0));
        visited.Add((sx, sy, sfl));

        while (q.Count > 0)
        {
            var ((cx, cy, cfl), dist) = q.Dequeue();
            if (!grid.TryGetValue((cx, cy, cfl), out var p)) continue;
            Dir open = PieceDB.GetOpenings(p.Type, p.Rotation);
            if (CountBits((int)open) >= 3 && dist > 0) return dist;

            for (int d = 0; d < 4; d++)
            {
                if ((open & AllDirs[d]) == 0) continue;
                var (dx, dy) = DirDelta[d];
                var next = (cx+dx, cy+dy, cfl);
                if (visited.Contains(next)) continue;
                visited.Add(next);
                if (grid.ContainsKey(next)) q.Enqueue((next, dist + 1));
            }
        }
        return 999;
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  LOOP INJECTION
    // ══════════════════════════════════════════════════════════════════════════
    static void InjectLoops(Dictionary<(int,int,int), MazePiece> grid, Random rng, int maxLoops)
    {
        var pairs = new List<((int,int,int) a, (int,int,int) b, Dir dirAtoB, Dir dirBtoA)>();

        foreach (var key in grid.Keys)
        {
            var (x, y, fl) = key;
            for (int d = 0; d < 4; d++)
            {
                var (dx, dy) = DirDelta[d];
                int nx = x + dx, ny = y + dy;
                var nkey = (nx, ny, fl);
                if (!grid.ContainsKey(nkey)) continue;
                if (nx < x || (nx == x && ny < y)) continue;

                Dir dirAtoB = AllDirs[d];
                Dir dirBtoA = OppDirs[d];

                var pa = grid[key];
                var pb = grid[nkey];

                Dir openA = PieceDB.GetOpenings(pa.Type, pa.Rotation);
                Dir openB = PieceDB.GetOpenings(pb.Type, pb.Rotation);

                bool aConnects = (openA & dirAtoB) != 0;
                bool bConnects = (openB & dirBtoA) != 0;

                if (aConnects || bConnects) continue;
                pairs.Add((key, nkey, dirAtoB, dirBtoA));
            }
        }

        Shuffle(pairs, rng);
        int loopsAdded = 0;

        foreach (var (a, b, dirAtoB, dirBtoA) in pairs)
        {
            if (loopsAdded >= maxLoops) break;

            var pa = grid[a];
            var pb = grid[b];

            if (PieceDB.IsStair(pa.Type) || PieceDB.IsStair(pb.Type)) continue;

            var newTypeA = UpgradePieceForOpenings(pa.Type, pa.Rotation, dirAtoB, out int newRotA);
            var newTypeB = UpgradePieceForOpenings(pb.Type, pb.Rotation, dirBtoA, out int newRotB);

            if (newTypeA == null || newTypeB == null) continue;

            grid[a] = new MazePiece { Type = newTypeA.Value, X = a.Item1, Y = a.Item2, Floor = a.Item3, Rotation = newRotA };
            grid[b] = new MazePiece { Type = newTypeB.Value, X = b.Item1, Y = b.Item2, Floor = b.Item3, Rotation = newRotB };
            loopsAdded++;
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  STAIR VALIDATION AND FIX
    // ══════════════════════════════════════════════════════════════════════════
    static void ValidateAndFixStairs(
        Dictionary<(int,int,int), MazePiece> grid,
        HashSet<PieceType> usedTypes,
        ref int goldSpent)
    {
        // Pass 0: demote any stair that is still side-by-side with another stair on the same floor.
        // Keep the one with lower Y (closer to Start); demote the other to its flat-direction piece.
        bool anyDemoted = true;
        while (anyDemoted)
        {
            anyDemoted = false;
            foreach (var stair in grid.Values.Where(p => PieceDB.IsStair(p.Type)).OrderBy(p => p.Y).ToList())
            {
                var key = (stair.X, stair.Y, stair.Floor);
                if (!grid.ContainsKey(key) || !PieceDB.IsStair(grid[key].Type)) continue;
                bool adjToStair = false;
                for (int di3 = 0; di3 < 4; di3++)
                {
                    var (adx3, ady3) = DirDelta[di3];
                    var adjKey3 = (stair.X + adx3, stair.Y + ady3, stair.Floor);
                    if (grid.TryGetValue(adjKey3, out var adjPc3) && PieceDB.IsStair(adjPc3.Type))
                    { adjToStair = true; break; }
                }
                if (!adjToStair) continue;
                // Demote: replace with a Straight along the stair's flat axis.
                goldSpent = Math.Max(0, goldSpent - PieceDB.GoldCosts[stair.Type]);
                Dir flatDir3 = PieceDB.GetStairFlatDir(stair.Type, stair.Rotation);
                var (flatType3, flatRot3) = ChoosePieceForOpenings(flatDir3 | PieceDB.Opposite(flatDir3));
                goldSpent += PieceDB.GoldCosts[flatType3];
                grid[key] = new MazePiece { Type = flatType3, X = stair.X, Y = stair.Y, Floor = stair.Floor, Rotation = flatRot3 };
                usedTypes.Add(flatType3);
                anyDemoted = true;
                break; // restart after each demotion to keep OrderBy correct
            }
        }

        foreach (var stair in grid.Values.Where(p => PieceDB.IsStair(p.Type)).ToList())
        {
            var info = PieceDB.GetStairInfo(stair.Type, stair.Rotation);
            int crossFloor = stair.Floor + info.FloorDelta;

            if (crossFloor < FloorMin || crossFloor > FloorMax) continue;

            int ci = Array.IndexOf(AllDirs, info.CrossDir);
            var (cdx, cdy) = DirDelta[ci];
            int lx = stair.X + cdx, ly = stair.Y + cdy;

            if (!InBounds(lx, ly, crossFloor)) continue;

            var landKey  = (lx, ly, crossFloor);
            Dir backNeeded = OppDirs[ci];

            if (grid.TryGetValue(landKey, out var landing))
            {
                Dir landOpen = PieceDB.GetOpenings(landing.Type, landing.Rotation);
                if ((landOpen & backNeeded) == 0 && !PieceDB.IsStair(landing.Type))
                {
                    var upgraded = UpgradePieceForOpenings(landing.Type, landing.Rotation,
                                                            backNeeded, out int newRot);
                    if (upgraded != null)
                    {
                        goldSpent = Math.Max(0, goldSpent - PieceDB.GoldCosts[landing.Type]);
                        goldSpent += PieceDB.GoldCosts[upgraded.Value];
                        grid[landKey] = new MazePiece
                        {
                            Type = upgraded.Value, X = lx, Y = ly,
                            Floor = crossFloor, Rotation = newRot
                        };
                        usedTypes.Add(upgraded.Value);
                    }
                }
            }
            else
            {
                var (ptype, prot) = ChoosePieceForOpenings(backNeeded);
                goldSpent += PieceDB.GoldCosts[ptype];
                grid[landKey] = new MazePiece { Type = ptype, X = lx, Y = ly, Floor = crossFloor, Rotation = prot };
                usedTypes.Add(ptype);
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  BFS REACHABILITY
    // ══════════════════════════════════════════════════════════════════════════
    static HashSet<(int,int,int)> BfsReachable(Dictionary<(int,int,int), MazePiece> grid)
    {
        var startPiece = grid.Values.FirstOrDefault(p => p.Type == PieceType.Start);
        if (startPiece == null) return new HashSet<(int,int,int)>();

        var visited = new HashSet<(int,int,int)>();
        var queue   = new Queue<MazePiece>();
        visited.Add((startPiece.X, startPiece.Y, startPiece.Floor));
        queue.Enqueue(startPiece);

        while (queue.Count > 0)
        {
            var cur  = queue.Dequeue();
            Dir open = PieceDB.GetOpenings(cur.Type, cur.Rotation);

            for (int d = 0; d < 4; d++)
            {
                Dir dir = AllDirs[d];
                if ((open & dir) == 0) continue;

                var (dx, dy) = DirDelta[d];
                Dir back = OppDirs[d];
                int nfl  = cur.Floor;

                if (PieceDB.IsStair(cur.Type) && dir == PieceDB.GetStairCrossDir(cur.Type, cur.Rotation))
                    nfl = cur.Floor + PieceDB.StairFloorDelta(cur.Type);

                var nKey = (cur.X + dx, cur.Y + dy, nfl);
                if (visited.Contains(nKey)) continue;
                if (!grid.TryGetValue(nKey, out var nb)) continue;
                if ((PieceDB.GetOpenings(nb.Type, nb.Rotation) & back) == 0) continue;

                visited.Add(nKey);
                queue.Enqueue(nb);
            }
        }
        return visited;
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  VALIDATOR
    // ══════════════════════════════════════════════════════════════════════════
    static List<string> Validate(MazeData data, int targetBudget)
    {
        var violations = new List<string>();
        var pieces     = data.Pieces;

        // Rule 1: Exactly one Start at row 0, floor 0, rot 0
        var starts = pieces.Where(p => p.Type == PieceType.Start).ToList();
        if (starts.Count == 0)     violations.Add("No Start piece");
        else if (starts.Count > 1) violations.Add($"Multiple Start: {starts.Count}");
        else
        {
            var s = starts[0];
            if (s.Y != 0)        violations.Add($"Start at row {s.Y}, expected 0");
            if (s.Floor != 0)    violations.Add($"Start at floor {s.Floor}, expected 0");
            if (s.Rotation != 0) violations.Add($"Start rotation {s.Rotation}, expected 0");
        }

        // Rule 2: Exactly one Exit at row 9
        var exits = pieces.Where(p => p.Type == PieceType.Exit).ToList();
        if (exits.Count == 0)     violations.Add("No Exit piece");
        else if (exits.Count > 1) violations.Add($"Multiple Exit: {exits.Count}");
        else
        {
            var e = exits[0];
            if (e.Y != GridH - 1) violations.Add($"Exit at row {e.Y}, expected {GridH - 1}");
        }

        // Rule 3: No duplicate cells
        var seen = new HashSet<(int,int,int)>();
        foreach (var p in pieces)
            if (!seen.Add((p.X, p.Y, p.Floor)))
                violations.Add($"Duplicate at ({p.X},{p.Y},F{p.Floor})");

        // Rule 4: All floors in valid range
        foreach (var p in pieces)
            if (p.Floor < FloorMin || p.Floor > FloorMax)
                violations.Add($"{p.Type} at floor {p.Floor} out of range [{FloorMin},{FloorMax}]");

        // Rule 5: Row 0 reserved for Start only
        foreach (var p in pieces)
            if (p.Y == 0 && p.Type != PieceType.Start)
                violations.Add($"{p.Type} at row 0 col {p.X} floor {p.Floor}");

        // Rule 6: Grid bounds
        foreach (var p in pieces)
        {
            if (p.X < 0 || p.X >= GridW) violations.Add($"{p.Type} X={p.X} out of bounds");
            if (p.Y < 0 || p.Y >= GridH) violations.Add($"{p.Type} Y={p.Y} out of bounds");
        }

        // Rule 7: StairsDown not on FloorMin; StairsUp not on FloorMax
        foreach (var p in pieces.Where(p => p.Type == PieceType.StairsDown))
            if (p.Floor <= FloorMin)
                violations.Add($"StairsDown at ({p.X},{p.Y},F{p.Floor}) needs floor below");
        foreach (var p in pieces.Where(p => p.Type == PieceType.StairsUp || p.Type == PieceType.Stairs))
            if (p.Floor >= FloorMax)
                violations.Add($"{p.Type} at ({p.X},{p.Y},F{p.Floor}) needs floor above");

        // Rule 8: Stair cross-floor connections valid
        var lookup = pieces.ToDictionary(p => (p.X, p.Y, p.Floor));
        foreach (var stair in pieces.Where(p => PieceDB.IsStair(p.Type)))
        {
            Dir crossDir   = PieceDB.GetStairCrossDir(stair.Type, stair.Rotation);
            int di         = Array.IndexOf(AllDirs, crossDir);
            var (dx, dy)   = DirDelta[di];
            int crossFloor = stair.Floor + PieceDB.StairFloorDelta(stair.Type);

            if (crossFloor < FloorMin || crossFloor > FloorMax)
            {
                violations.Add($"{stair.Type} at ({stair.X},{stair.Y},F{stair.Floor}) cross-floor F{crossFloor} out of range");
                continue;
            }

            int nx = stair.X + dx, ny = stair.Y + dy;
            Dir back = OppDirs[di];
            bool landed = lookup.TryGetValue((nx, ny, crossFloor), out var landing) &&
                          (PieceDB.GetOpenings(landing!.Type, landing.Rotation) & back) != 0;
            if (!landed)
                violations.Add($"{stair.Type} at ({stair.X},{stair.Y},F{stair.Floor}) cross-exit {crossDir}->({nx},{ny},F{crossFloor}) unconnected");
        }

        // Rule 9: Minimum 20 pieces
        if (pieces.Count < 20)
            violations.Add($"Only {pieces.Count} pieces (minimum 20)");

        // Rule 10: BFS from Start must reach Exit
        if (starts.Count == 1 && exits.Count == 1 && violations.All(v => !v.StartsWith("No ")))
        {
            var reachable = BfsReachable(pieces.ToDictionary(p => (p.X, p.Y, p.Floor)));
            var exitPiece = exits[0];
            if (!reachable.Contains((exitPiece.X, exitPiece.Y, exitPiece.Floor)))
                violations.Add("Exit not reachable from Start via BFS");
        }

        // Rule 11: Budget utilization >= 70% of target
        if (targetBudget > 0)
        {
            int goldSpent = pieces.Sum(p => PieceDB.GoldCosts[p.Type]);
            if (goldSpent < (int)(targetBudget * 0.70f))
                violations.Add($"Gold spent {goldSpent}g < 70% of budget {targetBudget}g ({goldSpent * 100 / targetBudget}%)");
        }

        // Rule 12: No side-by-side same-floor stairs
        var stairPositions = new HashSet<(int,int,int)>(
            pieces.Where(p => PieceDB.IsStair(p.Type)).Select(p => (p.X, p.Y, p.Floor)));
        foreach (var p in pieces.Where(p => PieceDB.IsStair(p.Type)))
        {
            foreach (var (ddx, ddy) in new[] {(0,-1),(1,0),(0,1),(-1,0)})
            {
                if (stairPositions.Contains((p.X + ddx, p.Y + ddy, p.Floor)))
                {
                    violations.Add($"Side-by-side stairs: {p.Type} at ({p.X},{p.Y},F{p.Floor})");
                    break;
                }
            }
        }

        return violations;
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  UTILITY
    // ══════════════════════════════════════════════════════════════════════════
    static void Shuffle<T>(List<T> list, Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
