using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ExpandPuzzle
{
    /// <summary>One level: the map, plus an optional colour layer of the same size.</summary>
    public struct LevelDef
    {
        /// <summary>Fewest moves the level can be solved in. Decides the star rating.</summary>
        public int par;
        public string[] rows;
        public string[] colors;
        /// <summary>Free text for the designer (what the level teaches, where it came from).</summary>
        public string note;
        /// <summary>The designer's own difficulty rating: "" (not rated), "1".."5" or "X" (bad level).</summary>
        public string rating;
        /// <summary>Which list the level belongs to: "" or "Base" = the campaign, "Test" = experiments. One word.</summary>
        public string section;

        public LevelDef(int par, string[] rows, string[] colors = null)
        {
            this.par = par;
            this.rows = rows;
            this.colors = colors;
            this.note = "";
            this.rating = "";
            this.section = "";
        }
    }

    /// <summary>
    /// The level list. Levels live in Assets/Resources/Levels.txt, which the level editor
    /// (Tools > Expand Puzzle > Level Editor) reads and writes. The list in this file is only the
    /// fallback for when that file is missing. See <see cref="LevelData.Parse"/> for the legend.
    /// </summary>
    public static class Levels
    {
        /// <summary>Name of the level file inside a Resources folder (without ".txt").</summary>
        public const string ResourceName = "Levels";
        /// <summary>Editor only: PlayerPrefs key holding one level (as text) to play-test on the next start.</summary>
        public const string TestLevelKey = "ExpandPuzzle.TestLevel";

        static List<LevelDef> s_List;
        static int s_Temporary = -1;

        /// <summary>All levels, loaded on first use.</summary>
        public static List<LevelDef> List
        {
            get
            {
                if (s_List == null) Reload();
                return s_List;
            }
        }

        /// <summary>Reads the level file again (the editor calls this after saving).</summary>
        public static void Reload()
        {
            s_Temporary = -1;
            s_List = null;
            TextAsset asset = Resources.Load<TextAsset>(ResourceName);
            if (asset != null) s_List = LevelText.Parse(asset.text);
            if (s_List == null || s_List.Count == 0) s_List = new List<LevelDef>(BuiltIn);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay()
        {
            s_List = null;
            s_Temporary = -1;
        }

        /// <summary>
        /// Adds a level for this session only (play-testing from the editor); it is never saved.
        /// A second call replaces the first. Returns its index.
        /// </summary>
        public static int SetTemporary(LevelDef def)
        {
            if (s_Temporary >= 0 && s_Temporary < List.Count) List[s_Temporary] = def;
            else
            {
                List.Add(def);
                s_Temporary = List.Count - 1;
            }
            return s_Temporary;
        }

        public static bool IsTemporary(int index)
        {
            return index == s_Temporary;
        }

        /// <summary>Fallback levels, used only when Resources/Levels.txt is missing.</summary>
        public static readonly LevelDef[] BuiltIn =
        {
            // 1 - walk to the pump, push into it once, park on the marks.
            new LevelDef(7, new[]
            {
                "######v##",
                "#H.....T#",
                "#......T#",
                "#########",
            }),
            // 2 - pump more than once, then carry the long shape.
            new LevelDef(7, new[]
            {
                "#########",
                "#H......#",
                ">.......#",
                "#.......#",
                "#..TTT..#",
                "#########",
            }),
            // 3 - two pumps, two directions: make a square that fits the shaft.
            new LevelDef(13, new[]
            {
                "###v#####",
                "#..H....#",
                "#.......<",
                "#.......#",
                "###..####",
                "###TT####",
                "###TT####",
                "#########",
            }),
            // 4 - forbidden tiles: bigger is not always better.
            new LevelDef(13, new[]
            {
                "#########",
                "#H......#",
                "#.......<",
                "#.......#",
                "#.XTTTX.#",
                "#########",
            }),
            // 5 - a wall stops part of the growth: use it to sculpt an L.
            new LevelDef(16, new[]
            {
                "####v####",
                "#...H...#",
                "#.....#.<",
                "#.......#",
                "#.......#",
                "#XT######",
                "#TT######",
                "#########",
            }),
            // 6 - order matters: only a flat shape fits through the door.
            new LevelDef(13, new[]
            {
                "##########v####",
                "#...#.........#",
                ">H......XTTTX.#",
                "#...#...XTTTX.#",
                "#...#.........#",
                "###############",
            }),
            // 7 - two moulds, one T.
            new LevelDef(23, new[]
            {
                "###########",
                "#....H....#",
                ">....P....<",
                "#.#....#.##",
                "#..XTTTX..#",
                "#####T#####",
                "###########",
            }),
            // 8 - first coloured pump: new cells come out green, and the marks want matching colours.
            new LevelDef(13, new[]
            {
                "#########",
                "#H......#",
                "#.......<",
                "#.......#",
                "#..TT...#",
                "#########",
            }, new[]
            {
                ".........",
                ".........",
                "........1",
                ".........",
                "...10....",
                ".........",
            }),
            // 9 - plain pump copies the colour next to it, green pump paints: the order decides the pattern.
            new LevelDef(17, new[]
            {
                "###########",
                "#H........<",
                "#.........<",
                "#.........#",
                "#.XTTTX...#",
                "###########",
            }, new[]
            {
                "...........",
                "...........",
                "..........1",
                "...........",
                "...100.....",
                "...........",
            }),
            // 10 - sculpt and paint at once: a green foot on the L.
            new LevelDef(16, new[]
            {
                "####v####",
                "#...H...#",
                "#.....#.<",
                "#.......#",
                "#.......<",
                "#XT######",
                "#TT######",
                "#########",
            }, new[]
            {
                ".........",
                ".........",
                "........1",
                ".........",
                ".........",
                "..0......",
                ".10......",
                ".........",
            }),

            // ---------------------------------------------------------------------------------
            // 11-40: generated levels, every one checked by the solver (par = fewest moves).
            // Ordered by the generator's difficulty guess; 35-40 use colour.
            // ---------------------------------------------------------------------------------
            // 11 - generated, difficulty guess 6.2
            new LevelDef(6, new[]
            {
                "##########",
                "##.<######",
                "#.....####",
                "#.HP..####",
                "#.vXX....#",
                "#..TT#...#",
                "#.XTT.####",
                "##########",
            }),
            // 12 - generated, difficulty guess 6.8
            new LevelDef(6, new[]
            {
                "######",
                "#....#",
                "#H.v##",
                "#..XT#",
                "#.#XT#",
                ">.#XT#",
                "#.#..#",
                "######",
            }),
            // 13 - generated, difficulty guess 7.6
            new LevelDef(7, new[]
            {
                "######",
                "#TTT.<",
                "#XXX.#",
                "#.H..#",
                "######",
            }),
            // 14 - generated, difficulty guess 8.3
            new LevelDef(9, new[]
            {
                "#######",
                "#.....#",
                "#.....#",
                ">.....#",
                "#X....#",
                "#TTT..#",
                "###H###",
                "#######",
            }),
            // 15 - generated, difficulty guess 9.1
            new LevelDef(8, new[]
            {
                "######",
                ">....#",
                "#.H..#",
                "#...X#",
                "#.TTT#",
                "#.X.X#",
                "##.###",
                "#...##",
                "#...##",
                ">...##",
                "######",
            }),
            // 16 - generated, difficulty guess 9.9
            new LevelDef(10, new[]
            {
                "########",
                "#....X.#",
                "#....T.#",
                "#...XT.#",
                "#....T.#",
                "#.H..T.#",
                "#......#",
                "######^#",
            }),
            // 17 - generated, difficulty guess 10.7
            new LevelDef(12, new[]
            {
                "#########",
                "#.......#",
                "#..#....#",
                "#XTTTTX.#",
                "#..#....<",
                "#.......#",
                "#.H.....#",
                "#.>.....#",
                "#########",
            }),
            // 18 - generated, difficulty guess 11.5
            new LevelDef(11, new[]
            {
                "###########",
                "#.......###",
                "#.X..H....#",
                "#TTTTTTT###",
                "#..X....<##",
                "#.......###",
                "#....######",
                "###########",
            }),
            // 19 - generated, difficulty guess 12.3
            new LevelDef(11, new[]
            {
                "#######",
                "###..##",
                "###..##",
                "#....T#",
                "#....T#",
                "#....T#",
                "#....T#",
                "#.H..X#",
                "#.....#",
                "#.....#",
                "#####^#",
            }),
            // 20 - generated, difficulty guess 13.1
            new LevelDef(10, new[]
            {
                "###v###v##",
                "#...T...H#",
                "#..XT....#",
                "#...T..#.#",
                "#...T..#.#",
                "#...TX.#.#",
                "######^###",
            }),
            // 21 - generated, difficulty guess 13.8
            new LevelDef(9, new[]
            {
                "######v#",
                "#......#",
                "##.....#",
                "#.....T#",
                "#.....T#",
                "#..H..T#",
                "#..^..T#",
                "#####.T#",
                "########",
            }),
            // 22 - generated, difficulty guess 14.6
            new LevelDef(7, new[]
            {
                "########",
                "#.....v#",
                ">H.XTTT#",
                "#...TTT#",
                "##.XTTT#",
                "########",
            }),
            // 23 - generated, difficulty guess 15.4
            new LevelDef(17, new[]
            {
                "####v#",
                "#....#",
                "#....#",
                "#....#",
                "#....#",
                "#....#",
                "#...X#",
                "#..XT#",
                "##.HT#",
                "##..T#",
                "##..T#",
                "######",
            }),
            // 24 - generated, difficulty guess 16.2
            new LevelDef(6, new[]
            {
                "######",
                "##vHX#",
                "#.TTT<",
                "#.#TT#",
                "#.>TT#",
                "######",
            }),
            // 25 - generated, difficulty guess 16.9
            new LevelDef(7, new[]
            {
                "######v##",
                "##..XTT##",
                "##...TTX#",
                "#..#HTT.<",
                "######^##",
            }),
            // 26 - generated, difficulty guess 17.7
            new LevelDef(11, new[]
            {
                "######",
                "#>..##",
                "##..v#",
                "#....#",
                "#HX..#",
                "#.TTT#",
                "#XTTT#",
                "#.TTT#",
                "#....<",
                "######",
            }),
            // 27 - generated, difficulty guess 18.5
            new LevelDef(7, new[]
            {
                "#####v####",
                "#...XTTT.#",
                "#####XTTH<",
                "####..XX.#",
                "######^###",
            }),
            // 28 - generated, difficulty guess 19.3
            new LevelDef(8, new[]
            {
                "########v##",
                "#..XXXHP..#",
                "#.XTTTT#..#",
                "####TTTT<.#",
                "#######^###",
            }),
            // 29 - generated, difficulty guess 20.1
            new LevelDef(14, new[]
            {
                "#v####",
                "#....#",
                "##.X.#",
                "#.XTT#",
                ">..XT#",
                "##.H##",
                "##..##",
                "##..##",
                "######",
            }),
            // 30 - generated, difficulty guess 20.8
            new LevelDef(10, new[]
            {
                "######",
                "#..#.<",
                "#X...#",
                "#T^.H#",
                ">TTTT#",
                "#X#.X#",
                "######",
            }),
            // 31 - generated, difficulty guess 21.6
            new LevelDef(16, new[]
            {
                "#######",
                "#..<..#",
                "#X....#",
                "#TX...#",
                "#T#.H.#",
                "#TTX..#",
                "#^#####",
            }),
            // 32 - generated, difficulty guess 22.4
            new LevelDef(15, new[]
            {
                "#######",
                "#..H..<",
                ">.....#",
                "#.....#",
                "#.X...#",
                "#TTT.##",
                "#TTT.##",
                "#TTTTX#",
                "#####^#",
            }),
            // 33 - generated, difficulty guess 23.1
            new LevelDef(17, new[]
            {
                "########",
                "#TTT####",
                ">TTTTX.#",
                "#HX....#",
                "#.#....#",
                "#.#....#",
                "######^#",
            }),
            // 34 - generated, difficulty guess 24.0
            new LevelDef(14, new[]
            {
                "#######v#",
                "#..H....#",
                "#.....#.#",
                "#.X.....<",
                "#XT.....#",
                "#TTX....#",
                "#######^#",
            }),
            // 35 - generated, difficulty guess 11.0
            new LevelDef(9, new[]
            {
                "######v###",
                "#TX.....##",
                "#TX.....##",
                "#T....H..#",
                "##########",
            }, new[]
            {
                "......1...",
                ".0........",
                ".1........",
                ".1........",
                "..........",
            }),
            // 36 - generated, difficulty guess 13.8
            new LevelDef(7, new[]
            {
                "##v#######",
                "#.X....#.#",
                "#.T......#",
                "#XT...H..#",
                "#XT...P..#",
                "#######^##",
            }, new[]
            {
                "..........",
                "..........",
                "..1.......",
                "..0.......",
                "..0.......",
                ".......1..",
            }),
            // 37 - generated, difficulty guess 16.5
            new LevelDef(11, new[]
            {
                "###v####",
                "#..X#..<",
                "#..TTX.#",
                "#..TT.H#",
                "########",
            }, new[]
            {
                "........",
                ".......1",
                "...10...",
                "...10...",
                "........",
            }),
            // 38 - generated, difficulty guess 19.4
            new LevelDef(9, new[]
            {
                "########",
                "###..###",
                "##>..<##",
                "###H.###",
                "#......#",
                "#XXX...#",
                ">TTTX..#",
                "########",
            }, new[]
            {
                "........",
                "........",
                "..1.....",
                "........",
                "........",
                "........",
                ".011....",
                "........",
            }),
            // 39 - generated, difficulty guess 22.5
            new LevelDef(15, new[]
            {
                "#######v###",
                "#.PTT.#.###",
                "#.HTTTX...<",
                "#..TTT....#",
                "######^####",
            }, new[]
            {
                "...........",
                "...11......",
                "...110....1",
                "...110.....",
                "......1....",
            }),
            // 40 - generated, difficulty guess 25.0
            new LevelDef(9, new[]
            {
                "#######",
                "####.##",
                "###v.##",
                "#>TTTX#",
                "##TTT##",
                "#HTTX##",
                "#.XX.##",
                "#....##",
                "#######",
            }, new[]
            {
                ".......",
                ".......",
                "...1...",
                "..000..",
                "..111..",
                "..11...",
                ".......",
                ".......",
                ".......",
            }),

            // ---------------------------------------------------------------------------------
            // 41-54: generated colour levels. 41-49 use two colours, 50-54 use three
            // (0 = coral, 1 = green, 2 = purple). Each block is ordered by difficulty guess.
            // ---------------------------------------------------------------------------------
            // 41 - generated, two colours, difficulty guess 10.1
            new LevelDef(6, new[]
            {
                "######",
                "#....#",
                "#....#",
                "#....#",
                "##...#",
                "##...#",
                "##...#",
                ">...##",
                "#XHX##",
                "#TTT##",
                "######",
            }, new[]
            {
                "......",
                "......",
                "......",
                "......",
                "......",
                "......",
                "......",
                "1.....",
                "......",
                ".011..",
                "......",
            }),
            // 42 - generated, two colours, difficulty guess 11.8
            new LevelDef(7, new[]
            {
                "########",
                "###....#",
                "###.TTT#",
                ">.H...X#",
                "#...#.##",
                "###..###",
                "########",
            }, new[]
            {
                "........",
                "........",
                "....011.",
                "1.......",
                "........",
                "........",
                "........",
            }),
            // 43 - generated, two colours, difficulty guess 13.8
            new LevelDef(13, new[]
            {
                "########",
                "##vTX..#",
                "#..T...#",
                "#..T#..#",
                "##.T...#",
                "##.T...#",
                "##...H.#",
                "########",
            }, new[]
            {
                "........",
                "..10....",
                "...1....",
                "...1....",
                "...1....",
                "...1....",
                "........",
                "........",
            }),
            // 44 - generated, two colours, difficulty guess 15.6
            new LevelDef(8, new[]
            {
                "#####v####",
                "#.H..TX..#",
                "#...XT#..#",
                "#....T.#.#",
                "####^#####",
            }, new[]
            {
                ".....1....",
                ".....0....",
                ".....0....",
                ".....1....",
                "..........",
            }),
            // 45 - generated, two colours, difficulty guess 17.5
            new LevelDef(9, new[]
            {
                "###v#####",
                "#....TH.#",
                "#...XT.##",
                "#....T..#",
                "#.......#",
                "###^#####",
            }, new[]
            {
                ".........",
                ".....1...",
                ".....0...",
                ".....0...",
                ".........",
                "...1.....",
            }),
            // 46 - generated, two colours, difficulty guess 19.4
            new LevelDef(12, new[]
            {
                "#######",
                "#T..v##",
                "#T....#",
                "#TX...#",
                "#TX...#",
                "#X....#",
                "#H..^.#",
                "#######",
            }, new[]
            {
                ".......",
                ".0..1..",
                ".0.....",
                ".0.....",
                ".1.....",
                ".......",
                ".......",
                ".......",
            }),
            // 47 - generated, two colours, difficulty guess 21.2
            new LevelDef(16, new[]
            {
                "####v######",
                "#.......###",
                "#.........#",
                "#.TX......#",
                "#.T.......#",
                "#.TH.....##",
                "#.T.....###",
                "#.T.....###",
                "#####^#####",
            }, new[]
            {
                "....1......",
                "...........",
                "...........",
                "..0........",
                "..0........",
                "..1........",
                "..1........",
                "..1........",
                "...........",
            }),
            // 48 - generated, two colours, difficulty guess 23.2
            new LevelDef(11, new[]
            {
                "##########",
                "##>...####",
                "###..Xv###",
                "#..##TTT.<",
                "#....#TTX#",
                "#..PH.X..#",
                "##########",
            }, new[]
            {
                "..........",
                "..........",
                "..........",
                ".....100.1",
                "......10..",
                "..........",
                "..........",
            }),
            // 49 - generated, two colours, difficulty guess 25.0
            new LevelDef(12, new[]
            {
                "#######",
                "#XT...#",
                "#XT...#",
                "#TT...#",
                "#.....#",
                "###>H.#",
                "#.....<",
                "###^#.#",
                "#####.#",
                "#######",
            }, new[]
            {
                ".......",
                "..1....",
                "..1....",
                ".00....",
                ".......",
                ".......",
                ".......",
                "...1...",
                ".......",
                ".......",
            }),
            // 50 - generated, three colours, difficulty guess 14.9
            new LevelDef(7, new[]
            {
                "##v#####",
                "#.XTX..#",
                "#.XT.v##",
                "##.T.H.#",
                "##^#####",
            }, new[]
            {
                "..2.....",
                "...1....",
                "...0.2..",
                "...2....",
                "..1.....",
            }),
            // 51 - generated, three colours, difficulty guess 17.2
            new LevelDef(11, new[]
            {
                "##v#########",
                "#T.........#",
                "#TX........#",
                "#TX......H.#",
                "########^###",
            }, new[]
            {
                "..2.........",
                ".1..........",
                ".0..........",
                ".2..........",
                "........1...",
            }),
            // 52 - generated, three colours, difficulty guess 20.5
            new LevelDef(10, new[]
            {
                "##########",
                "#H...XTv.#",
                "#.....TX.#",
                "#....XT..#",
                "###^##^###",
            }, new[]
            {
                "..........",
                "......12..",
                "......0...",
                "......2...",
                "...0..1...",
            }),
            // 53 - generated, three colours, difficulty guess 23.8
            new LevelDef(14, new[]
            {
                "######",
                "#H####",
                "#P...#",
                "#.X..#",
                "#.TT.<",
                "#XTTX#",
                "#^TT##",
                "##TT##",
                "##..##",
                "######",
            }, new[]
            {
                "......",
                "......",
                "......",
                "......",
                "..22.1",
                "..22..",
                ".210..",
                "..10..",
                "......",
                "......",
            }),
            // 54 - generated, three colours, difficulty guess 27.1
            new LevelDef(21, new[]
            {
                "##v########",
                "##...H.X..#",
                "#....TTTX.#",
                "#.<#.TTTX.#",
                "#>.....T..#",
                "###########",
            }, new[]
            {
                "..1........",
                "...........",
                ".....022...",
                ".....111...",
                ".2.....1...",
                "...........",
            }),
        };

        public const string BaseSection = "Base";

        /// <summary>True for the campaign: levels without a section, or marked "Base".</summary>
        public static bool IsBase(string section)
        {
            return string.IsNullOrEmpty(section) || section == BaseSection;
        }

        /// <summary>The list a level belongs to: "Base" for the campaign, "Test" for experiments.</summary>
        public static string SectionOf(int index)
        {
            if (index < 0 || index >= List.Count) return BaseSection;
            string s = List[index].section;
            return IsBase(s) ? BaseSection : s;
        }

        /// <summary>1-based number of a level inside its own section.</summary>
        public static int NumberInSection(int index)
        {
            string section = SectionOf(index);
            int n = 0;
            for (int i = 0; i <= index && i < List.Count; i++)
                if (SectionOf(i) == section) n++;
            return Mathf.Max(1, n);
        }

        /// <summary>The level after this one in the same section (after the last one comes the section's first).</summary>
        public static int NextInSection(int index)
        {
            string section = SectionOf(index);
            for (int step = 1; step <= List.Count; step++)
            {
                int i = (index + step) % List.Count;
                if (i != s_Temporary && SectionOf(i) == section) return i;
            }
            return index;
        }

        /// <summary>"Level 12" for the campaign, "Test 3" for a level of the Test section.</summary>
        public static string DisplayName(int index)
        {
            string section = SectionOf(index);
            return (section == BaseSection ? "Level" : section) + " " + NumberInSection(index);
        }

        public static int Count { get { return List.Count; } }

        public static LevelData Load(int index)
        {
            int count = List.Count;
            index = ((index % count) + count) % count;
            LevelData level = LevelData.Parse(DisplayName(index), List[index].rows, List[index].colors);
            level.par = List[index].par;
            return level;
        }
    }

    /// <summary>
    /// Levels as plain text, so they can live in a file and be edited by hand:
    /// <code>
    /// === par=7 rating=2 note=walk to the pump
    /// ######v##
    /// #H.....T#
    /// #########
    /// ---            (optional colour layer, same size)
    /// ......1..
    /// </code>
    /// Lines starting with ';' are comments.
    /// </summary>
    public static class LevelText
    {
        public static List<LevelDef> Parse(string text)
        {
            var list = new List<LevelDef>();
            if (string.IsNullOrEmpty(text)) return list;

            bool open = false, inColors = false;
            LevelDef current = new LevelDef();
            var rows = new List<string>();
            var colors = new List<string>();

            string[] lines = text.Replace("\r", "").Split('\n');
            for (int i = 0; i <= lines.Length; i++)
            {
                string line = i < lines.Length ? lines[i].TrimEnd() : "===";
                if (line.StartsWith(";")) continue;

                if (line.StartsWith("==="))
                {
                    if (open && rows.Count > 0)
                    {
                        current.rows = rows.ToArray();
                        current.colors = colors.Count > 0 ? colors.ToArray() : null;
                        list.Add(current);
                    }
                    open = true;
                    inColors = false;
                    rows.Clear();
                    colors.Clear();
                    current = ParseHeader(line);
                    continue;
                }

                if (!open || line.Length == 0) continue;
                if (line.StartsWith("---")) { inColors = true; continue; }
                (inColors ? colors : rows).Add(line);
            }
            return list;
        }

        static LevelDef ParseHeader(string line)
        {
            var def = new LevelDef { note = "", rating = "", section = "" };
            string body = line.Substring(3).Trim();

            int noteAt = body.IndexOf("note=");
            if (noteAt >= 0)
            {
                def.note = body.Substring(noteAt + 5).Trim();
                body = body.Substring(0, noteAt);
            }
            foreach (string token in body.Split(' '))
            {
                int eq = token.IndexOf('=');
                if (eq <= 0) continue;
                string key = token.Substring(0, eq), value = token.Substring(eq + 1);
                if (key == "par") int.TryParse(value, out def.par);
                else if (key == "rating") def.rating = value;
                else if (key == "section") def.section = value;
            }
            return def;
        }

        public static string Write(IList<LevelDef> levels)
        {
            var sb = new StringBuilder();
            sb.Append("; ExpandPuzzle levels. Edit with Tools > Expand Puzzle > Level Editor (or by hand).\n");
            sb.Append("; '=== par=N rating=R note=...' starts a level, then the map rows; '---' starts the optional colour layer.\n");
            sb.Append("; 'section=Test' in the header puts a level in the Test list (experiments); without it the level is in Base.\n");
            sb.Append("; Map: # wall, . floor, H blob head, P blob body, T target, X forbidden, ^ v < > pump.\n");
            sb.Append(";      W movable wall (drag it), B loose blob body (push into it to merge), * trap (destroys one blob cell).\n");
            sb.Append("; Colour layer: a digit on a pump, target or blob cell (0 coral, 1 green, 2 purple, 3 orange).\n\n");
            for (int i = 0; i < levels.Count; i++)
            {
                LevelDef d = levels[i];
                sb.Append("=== par=").Append(d.par).Append(" rating=").Append(d.rating ?? "").Append(Levels.IsBase(d.section) ? "" : " section=" + d.section.Replace(" ", ""))
                  .Append(" note=").Append((d.note ?? "").Replace("\n", " ")).Append('\n');
                if (d.rows != null)
                    for (int r = 0; r < d.rows.Length; r++) sb.Append(d.rows[r]).Append('\n');
                if (d.colors != null && d.colors.Length > 0)
                {
                    sb.Append("---\n");
                    for (int r = 0; r < d.colors.Length; r++) sb.Append(d.colors[r]).Append('\n');
                }
                sb.Append('\n');
            }
            return sb.ToString();
        }
    }
}
