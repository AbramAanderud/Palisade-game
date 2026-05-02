// test/StressTest.cs
// Maze stress-test generator and validator.
// Run as a [Tool] scene in Godot editor, or attach to a Node and call _Ready().
// Writes 10 JSON maze files directly to the Godot user-data folder and prints
// a full validation report to the Godot output panel.
//
// To run: create a temporary scene with a Node, attach this script, run the scene.
// All 10 mazes are written and validated; results appear in the Output panel.

using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

[Tool]
public partial class StressTest : Node
{
    // ── Constants mirrored from MapEditorMain ────────────────────────────────
    const int FloorMin   = -3;
    const int FloorMax   =  3;
    const int StartFloor =  0;
    const int GridW      = 10;
    const int GridH      = 10;

    // ── Dir helpers (mirrors MapEditorMain) ──────────────────────────────────
    static readonly Dir[]       AllDirs  = { Dir.N, Dir.E, Dir.S, Dir.W };
    static readonly (int, int)[] DirDelta = { (0,-1),(1,0),(0,1),(-1,0) };
    static readonly Dir[]       Opposite  = { Dir.S, Dir.W, Dir.N, Dir.E };

    // Windows user-data path (no Godot ProjectSettings needed at tool-time)
    const string UserDataPath = "C:/Users/Abe/AppData/Roaming/Godot/app_userdata/Palisade-godot";

    // ══════════════════════════════════════════════════════════════════════════
    public override void _Ready()
    {
        GD.Print("=== StressTest: generating 10 maze files (slots 20-29) ===");

        // Ensure output directory exists
        System.IO.Directory.CreateDirectory(UserDataPath);

        var maps = new List<(string Description, MazeData Data)>
        {
            ("Map 0: dense floor 0 only, exit on floor 0",
             BuildMap0()),
            ("Map 1: two floors (0 and 1), exit on floor 1",
             BuildMap1()),
            ("Map 2: two floors (0 and -1), exit on floor -1",
             BuildMap2()),
            ("Map 3: three floors (0,1,2), exit on floor 2 — upward chain",
             BuildMap3()),
            ("Map 4: three floors (0,-1,-2), exit on floor -2 — downward chain",
             BuildMap4()),
            ("Map 5: mixed floors (-1,0,1), exit on floor -1",
             BuildMap5()),
            ("Map 6: mixed floors (-2,0,2), exit on floor 2 — max spread",
             BuildMap6()),
            ("Map 7: dense multi-floor (-1,0,1,2), exit on floor 2",
             BuildMap7()),
            ("Map 8: full vertical range (-3 to +3, 7 floors), exit on floor 3",
             BuildMap8()),
            ("Map 9: adversarial — exit on floor -3, start floor 0, deep descent",
             BuildMap9()),
        };

        for (int i = 0; i < maps.Count; i++)
        {
            var (desc, data) = maps[i];
            data.Name = $"Test Map {i + 1}";
            var violations = Validate(data);
            bool pass = violations.Count == 0;

            GD.Print($"\n--- {desc} ---");
            PrintFloorCounts(data);
            GD.Print($"Total pieces: {data.Pieces.Count}");
            GD.Print(pass ? "RESULT: PASS" : $"RESULT: FAIL  ({violations.Count} violation(s))");
            foreach (var v in violations)
                GD.Print($"  VIOLATION: {v}");

            int slot = 20 + i;
            if (pass)
                WriteJson(slot, data);
            else
                GD.Print($"  (file NOT written for slot {slot} — fix violations first)");
        }

        GD.Print("\n=== StressTest complete ===");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  MAP BUILDERS
    // ══════════════════════════════════════════════════════════════════════════

    // Helper: add Start + corridor + Exit on one floor, no floor transitions.
    // col: x column for the main corridor.
    // startRow: row for Start (must be 0).
    // exitRow:  row for Exit (must be GridH-1 = 9).
    // floor:    floor for all pieces.
    // fillerTypes: optional filler piece types to scatter on unused cells.
    static MazeData SingleFloorCorridor(int col, int floor,
        List<(PieceType type, int x, int y, int f, int rot)>? extras = null)
    {
        var data = new MazeData();
        // Start at (col, 0, floor=0, rot=2)  — rot=2 gives S opening
        data.Pieces.Add(new MazePiece { Type = PieceType.Start, X = col, Y = 0, Floor = 0, Rotation = 2 });
        // Corridor rows 1..GridH-2
        for (int y = 1; y <= GridH - 2; y++)
            data.Pieces.Add(new MazePiece { Type = PieceType.Straight, X = col, Y = y, Floor = floor, Rotation = 0 });
        // Exit at (col, 9, floor, rot=0) — rot=0 gives N opening
        data.Pieces.Add(new MazePiece { Type = PieceType.Exit, X = col, Y = GridH - 1, Floor = floor, Rotation = 0 });
        if (extras != null)
            foreach (var (type, x, y, f, rot) in extras)
                data.Pieces.Add(new MazePiece { Type = type, X = x, Y = y, Floor = f, Rotation = rot });
        return data;
    }

    // Map 0: dense floor 0 only, exit on floor 0.
    // Main corridor col 5.  Fill cols 0-4 and 6-9 rows 1-8 with L-halls / T-junctions.
    static MazeData BuildMap0()
    {
        var data = new MazeData();
        data.Pieces.Add(new MazePiece { Type = PieceType.Start, X = 5, Y = 0, Floor = 0, Rotation = 2 });
        for (int y = 1; y <= 8; y++)
            data.Pieces.Add(new MazePiece { Type = PieceType.Straight, X = 5, Y = y, Floor = 0, Rotation = 0 });
        data.Pieces.Add(new MazePiece { Type = PieceType.Exit, X = 5, Y = 9, Floor = 0, Rotation = 0 });

        // Dense filler — fill all free cells with L-halls
        for (int x = 0; x < GridW; x++)
        for (int y = 1; y <= 8; y++)
        {
            if (x == 5) continue;  // main corridor
            // row 0 is reserved for Start only; skip
            data.Pieces.Add(new MazePiece { Type = PieceType.LHall, X = x, Y = y, Floor = 0, Rotation = (x + y) % 4 });
        }
        return data;
    }

    // Map 1: floors 0 → 1, exit on floor 1.
    // Corridor: Start(5,0,0) → Straights(5,1-3,0) → StairsUp(5,4,0,rot=2) → Straight(5,5,1) → Straights(5,6-8,1) → Exit(5,9,1)
    // StairsUp rot=2: openings N|S, crossDir=S → connects to (5,5,1) which needs N opening (Straight rot=0 has N|S).
    static MazeData BuildMap1()
    {
        var data = new MazeData();
        data.Pieces.Add(new MazePiece { Type = PieceType.Start,    X = 5, Y = 0, Floor = 0, Rotation = 2 });
        for (int y = 1; y <= 3; y++)
            data.Pieces.Add(new MazePiece { Type = PieceType.Straight, X = 5, Y = y, Floor = 0, Rotation = 0 });
        // StairsUp at (5,4,0) rot=2: cross-floor exit = S → (5,5,1)
        data.Pieces.Add(new MazePiece { Type = PieceType.StairsUp,  X = 5, Y = 4, Floor = 0, Rotation = 2 });
        // Floor 1: receiver at (5,5,1) — Straight rot=0 has N|S; N face is the cross-floor landing
        data.Pieces.Add(new MazePiece { Type = PieceType.Straight, X = 5, Y = 5, Floor = 1, Rotation = 0 });
        for (int y = 6; y <= 8; y++)
            data.Pieces.Add(new MazePiece { Type = PieceType.Straight, X = 5, Y = y, Floor = 1, Rotation = 0 });
        data.Pieces.Add(new MazePiece { Type = PieceType.Exit,      X = 5, Y = 9, Floor = 1, Rotation = 0 });
        // Filler on floor 0 rows 5-8
        for (int y = 5; y <= 8; y++)
            data.Pieces.Add(new MazePiece { Type = PieceType.LHall, X = 3, Y = y, Floor = 0, Rotation = 1 });
        return data;
    }

    // Map 2: floors 0 → -1, exit on floor -1.
    // StairsDown(5,4,0,rot=0): crossDir=S → (5,5,-1) needs N opening.
    static MazeData BuildMap2()
    {
        var data = new MazeData();
        data.Pieces.Add(new MazePiece { Type = PieceType.Start,      X = 5, Y = 0, Floor = 0,  Rotation = 2 });
        for (int y = 1; y <= 3; y++)
            data.Pieces.Add(new MazePiece { Type = PieceType.Straight, X = 5, Y = y, Floor = 0, Rotation = 0 });
        // StairsDown at (5,4,0) rot=0: crossDir=S → (5,5,-1)
        data.Pieces.Add(new MazePiece { Type = PieceType.StairsDown, X = 5, Y = 4, Floor = 0,  Rotation = 0 });
        // Floor -1: receiver at (5,5,-1) — N opening needed
        data.Pieces.Add(new MazePiece { Type = PieceType.Straight, X = 5, Y = 5, Floor = -1, Rotation = 0 });
        for (int y = 6; y <= 8; y++)
            data.Pieces.Add(new MazePiece { Type = PieceType.Straight, X = 5, Y = y, Floor = -1, Rotation = 0 });
        data.Pieces.Add(new MazePiece { Type = PieceType.Exit,       X = 5, Y = 9, Floor = -1, Rotation = 0 });
        return data;
    }

    // Map 3: floors 0 → 1 → 2, exit on floor 2. Upward chain test.
    // StairsUp(5,3,0,rot=2) → (5,4,1); StairsUp(5,5,1,rot=2) → (5,6,2)
    static MazeData BuildMap3()
    {
        var data = new MazeData();
        data.Pieces.Add(new MazePiece { Type = PieceType.Start,    X = 5, Y = 0, Floor = 0, Rotation = 2 });
        for (int y = 1; y <= 2; y++)
            data.Pieces.Add(new MazePiece { Type = PieceType.Straight, X = 5, Y = y, Floor = 0, Rotation = 0 });
        // First stair: 0→1
        data.Pieces.Add(new MazePiece { Type = PieceType.StairsUp,  X = 5, Y = 3, Floor = 0, Rotation = 2 });
        // Floor 1
        data.Pieces.Add(new MazePiece { Type = PieceType.Straight, X = 5, Y = 4, Floor = 1, Rotation = 0 });
        // Second stair: 1→2
        data.Pieces.Add(new MazePiece { Type = PieceType.StairsUp,  X = 5, Y = 5, Floor = 1, Rotation = 2 });
        // Floor 2
        data.Pieces.Add(new MazePiece { Type = PieceType.Straight, X = 5, Y = 6, Floor = 2, Rotation = 0 });
        for (int y = 7; y <= 8; y++)
            data.Pieces.Add(new MazePiece { Type = PieceType.Straight, X = 5, Y = y, Floor = 2, Rotation = 0 });
        data.Pieces.Add(new MazePiece { Type = PieceType.Exit,      X = 5, Y = 9, Floor = 2, Rotation = 0 });
        // Filler on floor 1 and 0
        for (int y = 7; y <= 8; y++)
            data.Pieces.Add(new MazePiece { Type = PieceType.LHall, X = 3, Y = y, Floor = 1, Rotation = 0 });
        for (int y = 4; y <= 8; y++)
            data.Pieces.Add(new MazePiece { Type = PieceType.LHall, X = 3, Y = y, Floor = 0, Rotation = 0 });
        return data;
    }

    // Map 4: floors 0 → -1 → -2, exit on floor -2. Downward chain test.
    // StairsDown(5,3,0,rot=0) → (5,4,-1); StairsDown(5,5,-1,rot=0) → (5,6,-2)
    static MazeData BuildMap4()
    {
        var data = new MazeData();
        data.Pieces.Add(new MazePiece { Type = PieceType.Start,      X = 5, Y = 0, Floor = 0,  Rotation = 2 });
        for (int y = 1; y <= 2; y++)
            data.Pieces.Add(new MazePiece { Type = PieceType.Straight, X = 5, Y = y, Floor = 0, Rotation = 0 });
        // First stair: 0→-1
        data.Pieces.Add(new MazePiece { Type = PieceType.StairsDown, X = 5, Y = 3, Floor = 0,  Rotation = 0 });
        // Floor -1
        data.Pieces.Add(new MazePiece { Type = PieceType.Straight, X = 5, Y = 4, Floor = -1, Rotation = 0 });
        // Second stair: -1→-2
        data.Pieces.Add(new MazePiece { Type = PieceType.StairsDown, X = 5, Y = 5, Floor = -1, Rotation = 0 });
        // Floor -2
        data.Pieces.Add(new MazePiece { Type = PieceType.Straight, X = 5, Y = 6, Floor = -2, Rotation = 0 });
        for (int y = 7; y <= 8; y++)
            data.Pieces.Add(new MazePiece { Type = PieceType.Straight, X = 5, Y = y, Floor = -2, Rotation = 0 });
        data.Pieces.Add(new MazePiece { Type = PieceType.Exit,       X = 5, Y = 9, Floor = -2, Rotation = 0 });
        return data;
    }

    // Map 5: mixed floors (-1, 0, 1), exit on floor -1.
    // Go up to floor 1 first, then come back down via StairsDown twice.
    // Start(5,0,0) → Straight(5,1,0) → StairsUp(5,2,0,rot=2) → [F1]
    // Straight(5,3,1) → StairsDown(5,4,1,rot=0) → [F0] (crossDir S → (5,5,0))
    // Straight(5,5,0) → StairsDown(5,6,0,rot=0) → [F-1] (5,7,-1)
    // Straight(5,7,-1) → Straight(5,8,-1) → Exit(5,9,-1)
    static MazeData BuildMap5()
    {
        var data = new MazeData();
        data.Pieces.Add(new MazePiece { Type = PieceType.Start,      X = 5, Y = 0, Floor = 0,  Rotation = 2 });
        data.Pieces.Add(new MazePiece { Type = PieceType.Straight,   X = 5, Y = 1, Floor = 0,  Rotation = 0 });
        // 0→1
        data.Pieces.Add(new MazePiece { Type = PieceType.StairsUp,   X = 5, Y = 2, Floor = 0,  Rotation = 2 });
        data.Pieces.Add(new MazePiece { Type = PieceType.Straight,   X = 5, Y = 3, Floor = 1,  Rotation = 0 });
        // 1→0
        data.Pieces.Add(new MazePiece { Type = PieceType.StairsDown, X = 5, Y = 4, Floor = 1,  Rotation = 0 });
        data.Pieces.Add(new MazePiece { Type = PieceType.Straight,   X = 5, Y = 5, Floor = 0,  Rotation = 0 });
        // 0→-1
        data.Pieces.Add(new MazePiece { Type = PieceType.StairsDown, X = 5, Y = 6, Floor = 0,  Rotation = 0 });
        data.Pieces.Add(new MazePiece { Type = PieceType.Straight,   X = 5, Y = 7, Floor = -1, Rotation = 0 });
        data.Pieces.Add(new MazePiece { Type = PieceType.Straight,   X = 5, Y = 8, Floor = -1, Rotation = 0 });
        data.Pieces.Add(new MazePiece { Type = PieceType.Exit,       X = 5, Y = 9, Floor = -1, Rotation = 0 });
        return data;
    }

    // Map 6: floors -2, 0, 2, exit on floor 2 — max spread (skips floors).
    // Start(5,0,0) → Straight(5,1,0) → StairsDown(5,2,0,rot=0) → (5,3,-1) ... but we skip -1.
    // We can't skip floors; stairs only go ±1. So: 0→-1→-2 going down, then -2→-1→0→1→2 going up.
    // That's 6 stair steps. Let's do:
    // Down: StairsDown(5,2,0)→(5,3,-1)  StairsDown(5,4,-1)→(5,5,-2)
    // Turnaround on -2
    // Up: StairsUp(5,6,-2)→(5,7,-1) via crossDir S at rot=2... wait, StairsUp crossDir at rot=2 is S → (x,y+1, F+1)
    // StairsUp(5,6,-2,rot=2)→(5,7,-1); StairsUp(5,7,-1,rot=2)→(5,8,0)
    // Then need 0→1→2 more stairs but only have row 9 left for exit...
    //
    // Revised: shorter path skipping floors isn't possible.  Use path:
    // 0→1 at row 2; 1→2 at row 4; exit F2 at row 9.
    // Then for floor -2 filler: add L-halls. Exit on floor 2.
    static MazeData BuildMap6()
    {
        var data = new MazeData();
        data.Pieces.Add(new MazePiece { Type = PieceType.Start,    X = 5, Y = 0, Floor = 0, Rotation = 2 });
        data.Pieces.Add(new MazePiece { Type = PieceType.Straight, X = 5, Y = 1, Floor = 0, Rotation = 0 });
        // 0→1
        data.Pieces.Add(new MazePiece { Type = PieceType.StairsUp, X = 5, Y = 2, Floor = 0, Rotation = 2 });
        data.Pieces.Add(new MazePiece { Type = PieceType.Straight, X = 5, Y = 3, Floor = 1, Rotation = 0 });
        // 1→2
        data.Pieces.Add(new MazePiece { Type = PieceType.StairsUp, X = 5, Y = 4, Floor = 1, Rotation = 2 });
        for (int y = 5; y <= 8; y++)
            data.Pieces.Add(new MazePiece { Type = PieceType.Straight, X = 5, Y = y, Floor = 2, Rotation = 0 });
        data.Pieces.Add(new MazePiece { Type = PieceType.Exit,     X = 5, Y = 9, Floor = 2, Rotation = 0 });

        // Filler on floor -2 (stress density)
        for (int x = 0; x < GridW; x++)
        for (int y = 2; y <= 8; y++)
            data.Pieces.Add(new MazePiece { Type = PieceType.LHall, X = x, Y = y, Floor = -2, Rotation = (x + y) % 4 });
        return data;
    }

    // Map 7: dense multi-floor (-1, 0, 1, 2), exit on floor 2.
    // Main path: 0→1→2 like map3.  Filler on -1 and extra filler on all floors.
    static MazeData BuildMap7()
    {
        var data = new MazeData();
        data.Pieces.Add(new MazePiece { Type = PieceType.Start,    X = 5, Y = 0, Floor = 0, Rotation = 2 });
        for (int y = 1; y <= 2; y++)
            data.Pieces.Add(new MazePiece { Type = PieceType.Straight, X = 5, Y = y, Floor = 0, Rotation = 0 });
        // 0→1
        data.Pieces.Add(new MazePiece { Type = PieceType.StairsUp, X = 5, Y = 3, Floor = 0, Rotation = 2 });
        data.Pieces.Add(new MazePiece { Type = PieceType.Straight, X = 5, Y = 4, Floor = 1, Rotation = 0 });
        // 1→2
        data.Pieces.Add(new MazePiece { Type = PieceType.StairsUp, X = 5, Y = 5, Floor = 1, Rotation = 2 });
        data.Pieces.Add(new MazePiece { Type = PieceType.Straight, X = 5, Y = 6, Floor = 2, Rotation = 0 });
        for (int y = 7; y <= 8; y++)
            data.Pieces.Add(new MazePiece { Type = PieceType.Straight, X = 5, Y = y, Floor = 2, Rotation = 0 });
        data.Pieces.Add(new MazePiece { Type = PieceType.Exit,     X = 5, Y = 9, Floor = 2, Rotation = 0 });

        // Dense filler on floors -1, 0, 1, 2 (avoid main corridor col 5 and row 0)
        int[] fillerFloors = { -1, 0, 1, 2 };
        foreach (int fl in fillerFloors)
        for (int x = 0; x < GridW; x++)
        for (int y = 1; y <= 8; y++)
        {
            if (x == 5) continue;
            if (data.Pieces.Any(p => p.X == x && p.Y == y && p.Floor == fl)) continue;
            // Skip stair ghost cells: StairsUp on fl-1 or fl+1 at same (x,y)
            bool stairGhostBelow = data.Pieces.Any(p =>
                PieceDB.IsStair(p.Type) && p.Type != PieceType.StairsDown &&
                p.Floor == fl - 1 && p.X == x && p.Y == y);
            bool stairGhostAbove = data.Pieces.Any(p =>
                p.Type == PieceType.StairsDown &&
                p.Floor == fl + 1 && p.X == x && p.Y == y);
            if (stairGhostBelow || stairGhostAbove) continue;
            data.Pieces.Add(new MazePiece { Type = PieceType.LHall, X = x, Y = y, Floor = fl, Rotation = (x * 3 + y) % 4 });
        }
        return data;
    }

    // Map 8: full vertical range (-3 to +3, 7 floors), exit on floor 3.
    // Chain: 0→1→2→3, then filler on -1,-2,-3.
    static MazeData BuildMap8()
    {
        var data = new MazeData();
        data.Pieces.Add(new MazePiece { Type = PieceType.Start, X = 5, Y = 0, Floor = 0, Rotation = 2 });
        // 0→1 at row 2
        data.Pieces.Add(new MazePiece { Type = PieceType.Straight, X = 5, Y = 1, Floor = 0, Rotation = 0 });
        data.Pieces.Add(new MazePiece { Type = PieceType.StairsUp, X = 5, Y = 2, Floor = 0, Rotation = 2 });
        data.Pieces.Add(new MazePiece { Type = PieceType.Straight, X = 5, Y = 3, Floor = 1, Rotation = 0 });
        // 1→2 at row 4
        data.Pieces.Add(new MazePiece { Type = PieceType.StairsUp, X = 5, Y = 4, Floor = 1, Rotation = 2 });
        data.Pieces.Add(new MazePiece { Type = PieceType.Straight, X = 5, Y = 5, Floor = 2, Rotation = 0 });
        // 2→3 at row 6
        data.Pieces.Add(new MazePiece { Type = PieceType.StairsUp, X = 5, Y = 6, Floor = 2, Rotation = 2 });
        data.Pieces.Add(new MazePiece { Type = PieceType.Straight, X = 5, Y = 7, Floor = 3, Rotation = 0 });
        data.Pieces.Add(new MazePiece { Type = PieceType.Straight, X = 5, Y = 8, Floor = 3, Rotation = 0 });
        data.Pieces.Add(new MazePiece { Type = PieceType.Exit,     X = 5, Y = 9, Floor = 3, Rotation = 0 });

        // Filler on -3, -2, -1
        int[] negFloors = { -3, -2, -1 };
        foreach (int fl in negFloors)
        for (int x = 1; x <= 8; x++)
        for (int y = 2; y <= 7; y++)
            data.Pieces.Add(new MazePiece { Type = PieceType.LHall, X = x, Y = y, Floor = fl, Rotation = (x + y * 2) % 4 });
        return data;
    }

    // Map 9: adversarial — Exit on floor -3 (minimum), start floor 0, deep descent.
    // Chain: 0→-1→-2→-3 using StairsDown, 4 stair steps needed.
    // StairsDown(5,2,0)→(5,3,-1); StairsDown(5,4,-1)→(5,5,-2); StairsDown(5,6,-2)→(5,7,-3); Exit(5,9,-3)
    static MazeData BuildMap9()
    {
        var data = new MazeData();
        data.Pieces.Add(new MazePiece { Type = PieceType.Start,      X = 5, Y = 0, Floor = 0,  Rotation = 2 });
        data.Pieces.Add(new MazePiece { Type = PieceType.Straight,   X = 5, Y = 1, Floor = 0,  Rotation = 0 });
        // 0→-1
        data.Pieces.Add(new MazePiece { Type = PieceType.StairsDown, X = 5, Y = 2, Floor = 0,  Rotation = 0 });
        data.Pieces.Add(new MazePiece { Type = PieceType.Straight,   X = 5, Y = 3, Floor = -1, Rotation = 0 });
        // -1→-2
        data.Pieces.Add(new MazePiece { Type = PieceType.StairsDown, X = 5, Y = 4, Floor = -1, Rotation = 0 });
        data.Pieces.Add(new MazePiece { Type = PieceType.Straight,   X = 5, Y = 5, Floor = -2, Rotation = 0 });
        // -2→-3
        data.Pieces.Add(new MazePiece { Type = PieceType.StairsDown, X = 5, Y = 6, Floor = -2, Rotation = 0 });
        data.Pieces.Add(new MazePiece { Type = PieceType.Straight,   X = 5, Y = 7, Floor = -3, Rotation = 0 });
        data.Pieces.Add(new MazePiece { Type = PieceType.Straight,   X = 5, Y = 8, Floor = -3, Rotation = 0 });
        data.Pieces.Add(new MazePiece { Type = PieceType.Exit,       X = 5, Y = 9, Floor = -3, Rotation = 0 });
        return data;
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  VALIDATOR
    // ══════════════════════════════════════════════════════════════════════════
    static List<string> Validate(MazeData data)
    {
        var violations = new List<string>();
        var pieces = data.Pieces;

        // Rule 1: Exactly one Start at row 0, floor 0
        var starts = pieces.Where(p => p.Type == PieceType.Start).ToList();
        if (starts.Count == 0)
            violations.Add("No Start piece");
        else if (starts.Count > 1)
            violations.Add($"Multiple Start pieces: {starts.Count}");
        else
        {
            var s = starts[0];
            if (s.Y != 0)        violations.Add($"Start at row {s.Y}, expected row 0");
            if (s.Floor != 0)    violations.Add($"Start at floor {s.Floor}, expected floor 0");
        }

        // Rule 2: Exactly one Exit at row GridH-1, floor in [FloorMin..FloorMax]
        var exits = pieces.Where(p => p.Type == PieceType.Exit).ToList();
        if (exits.Count == 0)
            violations.Add("No Exit piece");
        else if (exits.Count > 1)
            violations.Add($"Multiple Exit pieces: {exits.Count}");
        else
        {
            var e = exits[0];
            if (e.Y != GridH - 1)
                violations.Add($"Exit at row {e.Y}, expected row {GridH - 1}");
            if (e.Floor < FloorMin || e.Floor > FloorMax)
                violations.Add($"Exit at floor {e.Floor}, out of range [{FloorMin}..{FloorMax}]");
        }

        // Rule 3: No duplicate (X, Y, Floor)
        var seen = new HashSet<(int, int, int)>();
        foreach (var p in pieces)
        {
            var key = (p.X, p.Y, p.Floor);
            if (!seen.Add(key))
                violations.Add($"Duplicate piece at ({p.X},{p.Y},F{p.Floor}) type={p.Type}");
        }

        // Rule 4: All floors in [FloorMin..FloorMax]
        foreach (var p in pieces)
            if (p.Floor < FloorMin || p.Floor > FloorMax)
                violations.Add($"Piece {p.Type} at ({p.X},{p.Y}) on floor {p.Floor} out of range");

        // Rule 5: No piece at row 0 except Start
        foreach (var p in pieces)
            if (p.Y == 0 && p.Type != PieceType.Start)
                violations.Add($"Non-Start piece {p.Type} at row 0, col {p.X}, floor {p.Floor}");

        // Rule 6: All pieces within grid bounds
        foreach (var p in pieces)
        {
            if (p.X < 0 || p.X >= GridW)
                violations.Add($"Piece {p.Type} at ({p.X},{p.Y},F{p.Floor}) X out of grid [0..{GridW-1}]");
            if (p.Y < 0 || p.Y >= GridH)
                violations.Add($"Piece {p.Type} at ({p.X},{p.Y},F{p.Floor}) Y out of grid [0..{GridH-1}]");
        }

        // Rule 7: StairsDown cannot be on FloorMin (needs a floor below)
        foreach (var p in pieces.Where(p => p.Type == PieceType.StairsDown))
            if (p.Floor <= FloorMin)
                violations.Add($"StairsDown at ({p.X},{p.Y},F{p.Floor}) on floor {p.Floor} — needs floor below (min is {FloorMin})");

        // Rule 8: Stair cross-floor connections — each stair must have a valid landing piece
        // matching the cross-floor exit direction.
        var lookup = new Dictionary<(int, int, int), MazePiece>();
        foreach (var p in pieces) lookup[(p.X, p.Y, p.Floor)] = p;

        foreach (var stair in pieces.Where(p => PieceDB.IsStair(p.Type)))
        {
            Dir crossDir   = PieceDB.GetStairCrossDir(stair.Type, stair.Rotation);
            int di         = Array.IndexOf(AllDirs, crossDir);
            var (dx, dy)   = DirDelta[di];
            int crossFloor = stair.Floor + PieceDB.StairFloorDelta(stair.Type);

            if (crossFloor < FloorMin || crossFloor > FloorMax)
            {
                violations.Add($"Stair {stair.Type} at ({stair.X},{stair.Y},F{stair.Floor}) cross-floor target F{crossFloor} is out of range");
                continue;
            }

            int nx = stair.X + dx, ny = stair.Y + dy;
            Dir oppDir = Opposite[di];

            bool landed = lookup.TryGetValue((nx, ny, crossFloor), out var landing) &&
                          (PieceDB.GetOpenings(landing!.Type, landing.Rotation) & oppDir) != 0;

            if (!landed)
                violations.Add($"Stair {stair.Type} at ({stair.X},{stair.Y},F{stair.Floor}) rot={stair.Rotation}: " +
                               $"cross-floor exit {crossDir} → ({nx},{ny},F{crossFloor}) has no matching {oppDir} opening");
        }

        return violations;
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  OUTPUT HELPERS
    // ══════════════════════════════════════════════════════════════════════════
    static void PrintFloorCounts(MazeData data)
    {
        var byFloor = data.Pieces.GroupBy(p => p.Floor).OrderBy(g => g.Key);
        foreach (var g in byFloor)
            GD.Print($"  Floor {g.Key,3}: {g.Count(),3} pieces  [{string.Join(", ", g.GroupBy(p => p.Type).Select(t => $"{t.Key}×{t.Count()}"))}]");
    }

    static void WriteJson(int slot, MazeData data)
    {
        // Compute gold
        data.GoldSpent = data.Pieces.Sum(p => PieceDB.GoldCosts[p.Type]);

        string path = $"{UserDataPath}/maze_slot_{slot}.json";
        var opts = new JsonSerializerOptions { WriteIndented = false };
        string json = JsonSerializer.Serialize(data, opts);
        System.IO.File.WriteAllText(path, json);
        GD.Print($"  Written → {path}  ({json.Length} bytes)");
    }
}
