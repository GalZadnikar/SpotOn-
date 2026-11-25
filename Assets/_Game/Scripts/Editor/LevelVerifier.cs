using System.Text;
using UnityEditor;
using UnityEngine;

namespace ExpandPuzzle.EditorTools
{
    /// <summary>Runs the solver over every built-in level and reports unsolvable levels and wrong par values.</summary>
    public static class LevelVerifier
    {
        [MenuItem("Tools/Expand Puzzle/Verify Levels")]
        public static void VerifyAll()
        {
            Debug.Log(Report());
        }

        public static string Report()
        {
            if (!Application.isPlaying) Levels.Reload();   // pick up the file as it is on disk right now
            var sb = new StringBuilder();
            int problems = 0;
            for (int i = 0; i < Levels.Count; i++)
            {
                LevelData lvl = Levels.Load(i);
                SolveResult r = Solver.Solve(lvl);
                string note = "ok";
                if (!r.solvable) { note = r.complete ? "NOT SOLVABLE" : "search limit reached"; problems++; }
                else if (r.par != lvl.par) { note = "PAR SHOULD BE " + r.par; problems++; }
                sb.AppendLine("Level " + (i + 1) + ": par " + lvl.par + ", solver " + (r.solvable ? r.par.ToString() : "-") +
                              ", " + r.states + " positions, " + note);
            }
            sb.Insert(0, "Verified " + Levels.Count + " levels, " + problems + " problem(s)\n");
            return sb.ToString();
        }
    }
}
