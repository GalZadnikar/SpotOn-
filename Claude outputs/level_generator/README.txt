ExpandPuzzle level generator (Python 3, needs nothing but the standard library; fit2.py-style refits need numpy)

core.py      port of Rules + Solver from GridLogic.cs / Solver.cs (same move order, same pump rule), Levels.txt reader/writer
graph.py     explores every reachable position of a level and measures it; predict() is the difficulty estimate
gen.py       random rooms -> play forward -> pick a goal -> place marks and forbidden tiles -> measure
pick.py      quality filters, est() = final difficulty estimate, tiers per difficulty
choose.py    picks a varied set for a list of wanted difficulties (SLOTS)
assemble.py  writes Levels.txt (rated levels kept, unrated ones replaced) and re-checks every level with the solver
view.py      prints candidates with their solution outline

Typical run (from this folder, with the current Levels.txt copied next to it as levels_now.txt):
  mkdir pool2
  python3 gen.py 1 101 200 pool2/c1.jsonl      (colours, seed, seconds, output)
  python3 gen.py 2 202 200 pool2/c2.jsonl
  python3 gen.py 3 303 200 pool2/c3.jsonl
  python3 choose.py                            (writes chosen.json)
  python3 assemble.py                          (writes Levels.txt; edit the output path at the bottom first)

Difficulty estimate, fitted to the designer's ratings of levels 1-15 (average miss about half a step):
  model = -1.73 + 0.33*log2(positions) + 0.24*R + 0.24*pumps + 0.39*log2(1 + walking moves)
  R     = -log2(chance that pressing pumps blindly still wins), capped at 6
  est   = max(model, 1.5 + 0.6*min(trap bits, 4.5))
'trap bits' counts, along the shortest solution, how much has to be known to avoid presses that can never be undone.
When more levels have been rated, refit the numbers in graph.predict().

Roominess: the designer's own levels have marks on about 12% of the floor. choose.py keeps generated levels
at 20% or less with at least 24 floor tiles (16 for the easiest), except the few slots listed in COMPACT.
assemble.py never touches a level that has a rating; only unrated levels are replaced.
