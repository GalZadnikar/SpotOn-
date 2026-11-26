using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ExpandPuzzle.EditorTools
{
    /// <summary>
    /// Tools > Expand Puzzle > Level Editor
    /// Paint a level on a grid, see at once whether it can be solved and in how many moves,
    /// try it in Play mode, and save the whole list to Assets/Resources/Levels.txt.
    /// </summary>
    public class LevelEditorWindow : EditorWindow
    {
        const string LevelFile = "Assets/Resources/Levels.txt";
        const int SolveLimit = 40000;
        const float PanelWidth = 264f;      // left column
        const float PanelInner = 246f;      // what fits in it next to a vertical scrollbar

        enum Tool { Wall, Floor, Head, Body, Target, Forbidden, PumpUp, PumpDown, PumpLeft, PumpRight, MovableWall, LooseBody, Trap, Recolor }

        static readonly string[] ToolNames =
        {
            "Wall", "Floor", "Blob head", "Blob body", "Target", "Forbidden",
            "Pump up", "Pump down", "Pump left", "Pump right", "Movable wall", "Loose body", "Trap", "Colour only"
        };
        static readonly char[] ToolChars = { '#', '.', 'H', 'P', 'T', 'X', '^', 'v', '<', '>', 'W', 'B', '*', ' ' };
        static readonly string[] Ratings = { "not rated", "1", "2", "3", "4", "5", "X (bad)" };
        static readonly string[] RatingValues = { "", "1", "2", "3", "4", "5", "X" };
        static readonly string[] SectionNames = { "Base", "Test (experiments)" };

        // Same colours as the game's default theme, so the grid reads like the level.
        static readonly Color[] CellColors =
        {
            Hex(0xFF5A4F), Hex(0x3FCB6E), Hex(0xA26BF5), Hex(0xFF9F2E)
        };
        static readonly string[] ColorNames = { "coral", "green", "purple", "orange" };

        class EditLevel
        {
            public int w, h;
            public char[,] tile;    // [x, row], row 0 = top
            public int[,] color;    // -1 = none
            public int par;
            public string note = "";
            public string rating = "";
            public string section = "";   // "" = Base
        }

        // Survives script reloads and entering Play mode, so unsaved work is never lost by pressing Test.
        [SerializeField] string m_Text;
        [SerializeField] int m_Index;
        [SerializeField] bool m_Dirty;
        [SerializeField] Tool m_Tool = Tool.Floor;
        [SerializeField] int m_Color = -1;
        [SerializeField] bool m_ShowSolution;
        [SerializeField] bool m_KeepBorder = true;

        readonly List<EditLevel> m_Levels = new List<EditLevel>();
        readonly List<string> m_Undo = new List<string>();
        Vector2 m_Scroll;
        bool m_NeedSolve = true;
        string m_Status = "";
        string m_Warning = "";
        string m_Solution = "";
        bool m_Solvable;
        bool m_Painting;
        GUIStyle m_Glyph;

        [MenuItem("Tools/Expand Puzzle/Level Editor")]
        public static void Open()
        {
            var window = GetWindow<LevelEditorWindow>("Level Editor");
            window.minSize = new Vector2(700f, 600f);
            window.Show();
        }

        void OnEnable()
        {
            if (!string.IsNullOrEmpty(m_Text)) FromText(m_Text);
            else LoadFromFile();
            m_NeedSolve = true;
        }

        void OnFocus()
        {
            // Nothing unsaved here? Then show the file as it is now (it may have been changed outside).
            if (!m_Dirty && m_Levels.Count > 0)
            {
                LoadFromFile();
                m_NeedSolve = true;
            }
        }

        // ------------------------------------------------------------ data

        void LoadFromFile()
        {
            // Read the file itself, not the game's list: that one may hold a test level while the game runs.
            List<LevelDef> defs = File.Exists(LevelFile) ? LevelText.Parse(File.ReadAllText(LevelFile)) : null;
            if (defs == null || defs.Count == 0) defs = new List<LevelDef>(Levels.BuiltIn);
            m_Levels.Clear();
            foreach (LevelDef def in defs) m_Levels.Add(FromDef(def));
            if (m_Levels.Count == 0) m_Levels.Add(NewLevel(7, 6));
            m_Index = Mathf.Clamp(m_Index, 0, m_Levels.Count - 1);
            m_Dirty = false;
            m_Undo.Clear();
            Sync();
        }

        void FromText(string text)
        {
            m_Levels.Clear();
            foreach (LevelDef def in LevelText.Parse(text)) m_Levels.Add(FromDef(def));
            if (m_Levels.Count == 0) m_Levels.Add(NewLevel(7, 6));
            m_Index = Mathf.Clamp(m_Index, 0, m_Levels.Count - 1);
        }

        string ToText()
        {
            var defs = new List<LevelDef>();
            foreach (EditLevel l in m_Levels) defs.Add(ToDef(l));
            return LevelText.Write(defs);
        }

        /// <summary>Keeps the serialized copy up to date (call after every change).</summary>
        void Sync()
        {
            m_Text = ToText();
        }

        EditLevel Current { get { return m_Levels[Mathf.Clamp(m_Index, 0, m_Levels.Count - 1)]; } }

        static EditLevel NewLevel(int w, int h)
        {
            var l = new EditLevel { w = w, h = h, tile = new char[w, h], color = new int[w, h] };
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                {
                    bool edge = x == 0 || y == 0 || x == w - 1 || y == h - 1;
                    l.tile[x, y] = edge ? '#' : '.';
                    l.color[x, y] = -1;
                }
            l.tile[1, 1] = 'H';
            return l;
        }

        static EditLevel FromDef(LevelDef def)
        {
            int h = def.rows != null ? def.rows.Length : 0;
            int w = 0;
            for (int r = 0; r < h; r++) w = Mathf.Max(w, def.rows[r].Length);
            if (w < 3 || h < 3) return NewLevel(7, 6);

            var l = new EditLevel { w = w, h = h, tile = new char[w, h], color = new int[w, h] };
            l.par = def.par;
            l.note = def.note ?? "";
            l.rating = def.rating ?? "";
            l.section = def.section ?? "";
            for (int r = 0; r < h; r++)
                for (int x = 0; x < w; x++)
                {
                    char ch = x < def.rows[r].Length ? def.rows[r][x] : '#';
                    l.tile[x, r] = System.Array.IndexOf(ToolChars, ch) >= 0 && ch != ' ' ? ch : '#';
                    int col = -1;
                    if (def.colors != null && r < def.colors.Length && x < def.colors[r].Length)
                    {
                        char cc = def.colors[r][x];
                        if (cc >= '0' && cc <= '9') col = cc - '0';
                    }
                    l.color[x, r] = col;
                }
            return l;
        }

        static LevelDef ToDef(EditLevel l)
        {
            var def = new LevelDef { par = l.par, note = l.note, rating = l.rating, section = l.section, rows = new string[l.h] };
            bool anyColor = false;
            var colors = new string[l.h];
            for (int r = 0; r < l.h; r++)
            {
                var row = new char[l.w];
                var crow = new char[l.w];
                for (int x = 0; x < l.w; x++)
                {
                    row[x] = l.tile[x, r];
                    bool takesColor = TakesColor(l.tile[x, r]);
                    int col = takesColor ? l.color[x, r] : -1;
                    crow[x] = col >= 0 ? (char)('0' + col) : '.';
                    if (col >= 0) anyColor = true;
                }
                def.rows[r] = new string(row);
                colors[r] = new string(crow);
            }
            def.colors = anyColor ? colors : null;
            return def;
        }

        static bool TakesColor(char ch)
        {
            return ch == 'H' || ch == 'P' || ch == 'B' || ch == 'T' || ch == '^' || ch == 'v' || ch == '<' || ch == '>';
        }

        // ------------------------------------------------------------ editing

        void PushUndo()
        {
            m_Undo.Add(ToText());
            if (m_Undo.Count > 40) m_Undo.RemoveAt(0);
        }

        void UndoLast()
        {
            if (m_Undo.Count == 0) return;
            string text = m_Undo[m_Undo.Count - 1];
            m_Undo.RemoveAt(m_Undo.Count - 1);
            FromText(text);
            Changed();
        }

        void Changed()
        {
            m_Dirty = true;
            m_NeedSolve = true;
            Sync();
            Repaint();
        }

        void Paint(int x, int y, Tool tool)
        {
            EditLevel l = Current;
            if (x < 0 || y < 0 || x >= l.w || y >= l.h) return;

            if (tool == Tool.Recolor)
            {
                if (!TakesColor(l.tile[x, y]) || l.color[x, y] == m_Color) return;
                l.color[x, y] = m_Color;
                Changed();
                return;
            }

            char ch = ToolChars[(int)tool];
            int col = TakesColor(ch) ? m_Color : -1;
            if (l.tile[x, y] == ch && l.color[x, y] == col) return;

            if (ch == 'H')
            {
                // Only one head: the old one becomes floor.
                for (int i = 0; i < l.w; i++)
                    for (int j = 0; j < l.h; j++)
                        if (l.tile[i, j] == 'H') { l.tile[i, j] = '.'; l.color[i, j] = -1; }
            }
            l.tile[x, y] = ch;
            l.color[x, y] = col;
            Changed();
        }

        /// <summary>
        /// Adds (+1) or removes (-1) a row or column on one side. side: 0 top, 1 bottom, 2 left, 3 right.
        /// With "keep the outer wall" on, the change happens just inside the outermost row or column,
        /// so the ring of wall around the level stays where it is.
        /// </summary>
        void Resize(int side, int delta)
        {
            EditLevel l = Current;
            bool column = side >= 2;
            bool first = side == 0 || side == 2;
            int count = column ? l.w : l.h;

            if (delta > 0)
            {
                if (m_KeepBorder) InsertLine(column, first ? 1 : count - 1, first ? 1 : count - 2);
                else InsertLine(column, first ? 0 : count, -1);
            }
            else
            {
                if (m_KeepBorder) RemoveLine(column, first ? 1 : count - 2);
                else RemoveLine(column, first ? 0 : count - 1);
            }
        }

        /// <summary>Deletes one whole column (or row) anywhere in the level.</summary>
        void RemoveLine(bool column, int index)
        {
            EditLevel l = Current;
            int nw = l.w - (column ? 1 : 0);
            int nh = l.h - (column ? 0 : 1);
            if (nw < 3 || nh < 3 || index < 0 || index >= (column ? l.w : l.h)) return;
            PushUndo();

            var tile = new char[nw, nh];
            var color = new int[nw, nh];
            for (int x = 0; x < nw; x++)
                for (int y = 0; y < nh; y++)
                {
                    int sx = column && x >= index ? x + 1 : x;
                    int sy = !column && y >= index ? y + 1 : y;
                    tile[x, y] = l.tile[sx, sy];
                    color[x, y] = l.color[sx, sy];
                }
            l.w = nw;
            l.h = nh;
            l.tile = tile;
            l.color = color;
            Changed();
        }

        /// <summary>
        /// Inserts a column (or row) so that it ends up at 'index'. shapeFrom is the existing column (or row)
        /// whose shape it continues: floor where that one is open, wall where it is wall. -1 = all wall.
        /// </summary>
        void InsertLine(bool column, int index, int shapeFrom)
        {
            EditLevel l = Current;
            int nw = l.w + (column ? 1 : 0);
            int nh = l.h + (column ? 0 : 1);
            if (nw > 16 || nh > 16 || index < 0 || index > (column ? l.w : l.h)) return;
            PushUndo();

            var tile = new char[nw, nh];
            var color = new int[nw, nh];
            for (int x = 0; x < nw; x++)
                for (int y = 0; y < nh; y++)
                {
                    int pos = column ? x : y;
                    if (pos == index)
                    {
                        char like = shapeFrom < 0 ? '#' : column ? l.tile[shapeFrom, y] : l.tile[x, shapeFrom];
                        tile[x, y] = IsFloorChar(like) ? '.' : '#';
                        color[x, y] = -1;
                        continue;
                    }
                    int sx = column && x > index ? x - 1 : x;
                    int sy = !column && y > index ? y - 1 : y;
                    tile[x, y] = l.tile[sx, sy];
                    color[x, y] = l.color[sx, sy];
                }
            l.w = nw;
            l.h = nh;
            l.tile = tile;
            l.color = color;
            Changed();
        }

        /// <summary>Turns the whole level a quarter turn. Pumps turn with it, so they keep blowing the same way relative to the layout.</summary>
        void Rotate(bool clockwise)
        {
            EditLevel l = Current;
            PushUndo();

            int nw = l.h, nh = l.w;
            var tile = new char[nw, nh];
            var color = new int[nw, nh];
            const string turn = "^>v<";   // clockwise order
            for (int x = 0; x < nw; x++)
                for (int y = 0; y < nh; y++)
                {
                    int sx = clockwise ? y : l.w - 1 - y;
                    int sy = clockwise ? l.h - 1 - x : x;
                    char ch = l.tile[sx, sy];
                    int dir = turn.IndexOf(ch);
                    if (dir >= 0) ch = turn[(dir + (clockwise ? 1 : 3)) % 4];
                    tile[x, y] = ch;
                    color[x, y] = l.color[sx, sy];
                }
            l.w = nw;
            l.h = nh;
            l.tile = tile;
            l.color = color;
            Changed();
        }

        // ------------------------------------------------------------ solver

        void Solve()
        {
            m_NeedSolve = false;
            m_Solvable = false;
            m_Solution = "";
            m_Warning = "";
            EditLevel l = Current;

            int heads = 0, targets = 0, pumps = 0;
            bool floorOnEdge = false;
            string deadPump = null;
            for (int x = 0; x < l.w; x++)
                for (int y = 0; y < l.h; y++)
                {
                    char ch = l.tile[x, y];
                    if (ch == 'H') heads++;
                    if (ch == 'T') targets++;
                    if (ch != '#' && ch != '^' && ch != 'v' && ch != '<' && ch != '>' &&
                        (x == 0 || y == 0 || x == l.w - 1 || y == l.h - 1)) floorOnEdge = true;

                    int dx = ch == '<' ? -1 : ch == '>' ? 1 : 0;
                    int dy = ch == '^' ? -1 : ch == 'v' ? 1 : 0;
                    if (dx == 0 && dy == 0) continue;
                    pumps++;
                    int nx = x + dx, ny = y + dy;
                    bool blowsOnFloor = nx >= 0 && ny >= 0 && nx < l.w && ny < l.h && IsFloorChar(l.tile[nx, ny]);
                    if (!blowsOnFloor && deadPump == null) deadPump = "The pump in column " + (x + 1) + ", row " + (y + 1) + " blows into a wall.";
                }

            if (deadPump != null) m_Warning = deadPump;
            else if (floorOnEdge) m_Warning = "Floor touches the edge of the grid - leave a ring of wall around the level.";

            if (heads != 1) { m_Status = "Place the blob head (exactly one)."; return; }
            if (targets == 0) { m_Status = "Place at least one target."; return; }

            LevelDef def = ToDef(l);
            LevelData data = LevelData.Parse("edit", def.rows, def.colors);
            SolveResult result = Solver.Solve(data, SolveLimit);
            if (result.solvable)
            {
                m_Solvable = true;
                if (l.par != result.par) { l.par = result.par; Sync(); }
                int presses = 0;
                var sb = new System.Text.StringBuilder();
                // Count pump presses by replaying the solution.
                BoardState replay = BoardState.Start(data);
                foreach (SolveMove step in result.steps)
                {
                    if (step.block >= 0)
                    {
                        // A movable wall is dragged. Rows are counted from the top, the way the grid shows them.
                        sb.Append("[wall to column " + (step.to.x + 1) + ", row " + (data.height - step.to.y) + "] ");
                        replay.blocks[step.block] = step.to;
                        continue;
                    }
                    Vector2Int d = step.dir;
                    sb.Append(d == Vector2Int.up ? "U " : d == Vector2Int.down ? "D " : d == Vector2Int.left ? "L " : "R ");
                    if (Play.Step(data, replay, d).kind == StepKind.Pump) presses++;
                }
                m_Solution = sb.ToString().Trim();
                m_Status = "Solvable.  Par " + result.par + " moves, " + presses + " pump " + (presses == 1 ? "press" : "presses") +
                           ".  " + result.states + " positions checked.";
            }
            else if (!result.complete)
            {
                m_Status = "Too open to check (more than " + SolveLimit + " positions). Add walls or shrink the level.";
            }
            else
            {
                m_Status = "NOT solvable.  (" + result.states + " positions checked" + (pumps == 0 ? ", there is no pump" : "") + ")";
            }
        }

        static bool IsFloorChar(char ch)
        {
            return ch == '.' || ch == 'H' || ch == 'P' || ch == 'T' || ch == 'X' || ch == 'W' || ch == 'B' || ch == '*';
        }

        // ------------------------------------------------------------ file + play

        void Save()
        {
            if (m_NeedSolve) Solve();
            string dir = Path.GetDirectoryName(LevelFile);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(LevelFile, ToText());
            AssetDatabase.ImportAsset(LevelFile);
            if (!EditorApplication.isPlaying) Levels.Reload();
            m_Dirty = false;
            ShowNotification(new GUIContent("Saved " + m_Levels.Count + " levels"));
        }

        void Test()
        {
            var one = new List<LevelDef> { ToDef(Current) };
            if (EditorApplication.isPlaying)
            {
                var manager = FindFirstObjectByType<GameManager>();
                if (manager != null) manager.PlayTestLevel(one[0]);
                else ShowNotification(new GUIContent("No game in the running scene"));
                return;
            }

            if (FindFirstObjectByType<GameManager>() == null)
            {
                EditorUtility.DisplayDialog("Level Editor", "Open the game scene first (the one with the ExpandPuzzle object), then press Test again.", "OK");
                return;
            }
            PlayerPrefs.SetString(Levels.TestLevelKey, LevelText.Write(one));
            PlayerPrefs.Save();
            EditorApplication.isPlaying = true;
        }

        // ------------------------------------------------------------ GUI

        void OnGUI()
        {
            if (m_Levels.Count == 0) LoadFromFile();
            if (m_Glyph == null)
            {
                m_Glyph = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter };
                m_Glyph.normal.textColor = Color.white;
            }

            Event e = Event.current;
            if (e.type == EventType.KeyDown && (e.control || e.command) && e.keyCode == KeyCode.Z)
            {
                UndoLast();
                e.Use();
            }

            DrawToolbar();
            if (m_NeedSolve && e.type == EventType.Layout) Solve();

            EditorGUILayout.BeginHorizontal();
            // Left column: fixed width, everything inside is kept to that width, so it only ever scrolls
            // up and down (and only when the window is too short to show it all).
            EditorGUILayout.BeginVertical(GUILayout.Width(PanelWidth));
            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUIStyle.none);
            m_Scroll.x = 0f;
            EditorGUILayout.BeginVertical(GUILayout.Width(PanelInner));
            float oldLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 74f;
            DrawSidePanel();
            EditorGUIUtility.labelWidth = oldLabelWidth;
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical();
            DrawStatus();
            DrawGrid();
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
        }

        void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            using (new EditorGUI.DisabledScope(m_Index <= 0))
                if (GUILayout.Button("<", EditorStyles.toolbarButton, GUILayout.Width(24f))) Select(m_Index - 1);
            GUILayout.Label("Level", GUILayout.Width(36f));
            int typed = EditorGUILayout.DelayedIntField(m_Index + 1, GUILayout.Width(36f));
            if (typed != m_Index + 1) Select(typed - 1);
            GUILayout.Label("of " + m_Levels.Count, GUILayout.Width(44f));
            GUILayout.Label(Levels.IsBase(Current.section) ? "Base" : Current.section, EditorStyles.miniBoldLabel, GUILayout.Width(40f));
            using (new EditorGUI.DisabledScope(m_Index >= m_Levels.Count - 1))
                if (GUILayout.Button(">", EditorStyles.toolbarButton, GUILayout.Width(24f))) Select(m_Index + 1);

            GUILayout.Space(8f);
            if (GUILayout.Button("New", EditorStyles.toolbarButton))
            {
                PushUndo();
                var fresh = NewLevel(7, 6);
                fresh.section = Current.section;   // a new level joins the list (Base / Test) it was made in
                m_Levels.Insert(m_Index + 1, fresh);
                m_Index++;
                Changed();
            }
            if (GUILayout.Button("Duplicate", EditorStyles.toolbarButton))
            {
                PushUndo();
                EditLevel copy = FromDef(ToDef(Current));
                copy.note = Current.note;
                copy.rating = "";
                m_Levels.Insert(m_Index + 1, copy);
                m_Index++;
                Changed();
            }
            using (new EditorGUI.DisabledScope(m_Levels.Count <= 1))
                if (GUILayout.Button("Delete", EditorStyles.toolbarButton) &&
                    EditorUtility.DisplayDialog("Delete level", "Delete level " + (m_Index + 1) + "?", "Delete", "Cancel"))
                {
                    PushUndo();
                    m_Levels.RemoveAt(m_Index);
                    m_Index = Mathf.Clamp(m_Index, 0, m_Levels.Count - 1);
                    Changed();
                }
            using (new EditorGUI.DisabledScope(m_Index <= 0))
                if (GUILayout.Button("Move earlier", EditorStyles.toolbarButton)) Move(-1);
            using (new EditorGUI.DisabledScope(m_Index >= m_Levels.Count - 1))
                if (GUILayout.Button("Move later", EditorStyles.toolbarButton)) Move(1);

            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(m_Undo.Count == 0))
                if (GUILayout.Button("Undo", EditorStyles.toolbarButton)) UndoLast();
            using (new EditorGUI.DisabledScope(!m_Dirty))
                if (GUILayout.Button("Revert", EditorStyles.toolbarButton) &&
                    EditorUtility.DisplayDialog("Revert", "Throw away all unsaved changes?", "Revert", "Cancel"))
                {
                    m_Text = null;
                    LoadFromFile();
                    m_NeedSolve = true;
                }
            if (GUILayout.Button(m_Dirty ? "Save *" : "Save", EditorStyles.toolbarButton, GUILayout.Width(56f))) Save();

            EditorGUILayout.EndHorizontal();
        }

        void Select(int index)
        {
            m_Index = Mathf.Clamp(index, 0, m_Levels.Count - 1);
            m_NeedSolve = true;
            GUI.FocusControl(null);
            Repaint();
        }

        void Move(int delta)
        {
            PushUndo();
            EditLevel l = Current;
            m_Levels.RemoveAt(m_Index);
            m_Index += delta;
            m_Levels.Insert(m_Index, l);
            Changed();
        }

        void DrawSidePanel()
        {
            EditLevel l = Current;

            GUILayout.Label("Paint", EditorStyles.boldLabel);
            DrawToolButtons();
            Hint("Left mouse paints, right mouse paints floor.");

            GUILayout.Space(6f);
            GUILayout.Label("Colour (targets, pumps, blob)", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Toggle(m_Color < 0, "none", EditorStyles.miniButton, GUILayout.Width(44f))) m_Color = -1;
            for (int i = 0; i < CellColors.Length; i++)
            {
                Color old = GUI.backgroundColor;
                GUI.backgroundColor = CellColors[i];
                if (GUILayout.Toggle(m_Color == i, new GUIContent(i.ToString(), ColorNames[i]), EditorStyles.miniButton, GUILayout.Width(34f))) m_Color = i;
                GUI.backgroundColor = old;
            }
            EditorGUILayout.EndHorizontal();
            Hint(m_Color < 0
                ? "none: target takes any colour, pump copies the colour next to it, blob is coral."
                : "Painting with " + ColorNames[m_Color] + " (colour " + m_Color + ").");

            GUILayout.Space(6f);
            GUILayout.Label("Size  " + l.w + " x " + l.h, EditorStyles.boldLabel);
            m_KeepBorder = EditorGUILayout.ToggleLeft("Keep the outer wall", m_KeepBorder, GUILayout.Width(PanelInner - 4f));
            ResizeRow("Top", 0);
            ResizeRow("Bottom", 1);
            ResizeRow("Left", 2);
            ResizeRow("Right", 3);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Rotate", GUILayout.Width(56f));
            if (GUILayout.Button(new GUIContent("90\u00B0 left", "Turn the whole level a quarter turn anticlockwise"), EditorStyles.miniButtonLeft, GUILayout.Width(74f))) Rotate(false);
            if (GUILayout.Button(new GUIContent("90\u00B0 right", "Turn the whole level a quarter turn clockwise"), EditorStyles.miniButtonRight, GUILayout.Width(74f))) Rotate(true);
            EditorGUILayout.EndHorizontal();
            Hint(m_KeepBorder
                ? "+ and - change the row or column just inside the wall ring, so the ring stays. The small x buttons around the grid delete any row or column."
                : "+ and - change the outermost row or column. The small x buttons around the grid delete any row or column.");

            GUILayout.Space(6f);
            GUILayout.Label("About this level", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            int rating = Mathf.Max(0, System.Array.IndexOf(RatingValues, l.rating));
            rating = EditorGUILayout.Popup("My rating", rating, Ratings);
            int sectionWas = Levels.IsBase(l.section) ? 0 : 1;
            int sectionNow = EditorGUILayout.Popup("Section", sectionWas, SectionNames);
            GUILayout.Label("Note");
            string note = EditorGUILayout.TextArea(l.note, WrappedArea, GUILayout.Width(PanelInner - 4f), GUILayout.MinHeight(52f));
            if (EditorGUI.EndChangeCheck())
            {
                l.rating = RatingValues[rating];
                if (sectionNow != sectionWas) l.section = sectionNow == 0 ? "" : "Test";
                l.note = note.Replace("\n", " ");
                m_Dirty = true;
                Sync();
            }

            GUILayout.Space(8f);
            Color oldBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.55f, 0.9f, 0.6f);
            if (GUILayout.Button(EditorApplication.isPlaying ? "Test this level now" : "Test (enter Play mode)",
                    GUILayout.Width(PanelInner - 4f), GUILayout.Height(30f))) Test();
            GUI.backgroundColor = oldBg;
            Hint("Testing does not need a save. A level under test restarts when you win it.");
        }

        GUIStyle m_Hint, m_WrappedArea, m_ToolLabel, m_LineButton;

        /// <summary>The paint tools as buttons with a small picture of what each one paints, in the chosen colour.</summary>
        void DrawToolButtons()
        {
            const int columns = 2;
            const float height = 28f, gap = 2f, icon = 20f;
            float width = Mathf.Floor((PanelInner - 4f - gap * (columns - 1)) / columns);
            if (m_ToolLabel == null) m_ToolLabel = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleLeft };
            bool repaint = Event.current.type == EventType.Repaint;
            if (repaint) m_Glyph.fontSize = 11;

            for (int i = 0; i < ToolNames.Length; i += columns)
            {
                EditorGUILayout.BeginHorizontal();
                for (int j = i; j < i + columns && j < ToolNames.Length; j++)
                {
                    if (j > i) GUILayout.Space(gap);
                    Rect r = GUILayoutUtility.GetRect(width, height, GUILayout.Width(width), GUILayout.Height(height));
                    bool on = (int)m_Tool == j;
                    if (GUI.Toggle(r, on, GUIContent.none, GUI.skin.button) && !on) m_Tool = (Tool)j;
                    if (!repaint) continue;

                    var box = new Rect(r.x + 5f, r.y + Mathf.Round((r.height - icon) * 0.5f), icon, icon);
                    if ((Tool)j == Tool.Recolor)
                    {
                        // Four swatches: this one changes the colour and nothing else.
                        float half = icon * 0.5f;
                        for (int c = 0; c < 4 && c < CellColors.Length; c++)
                            EditorGUI.DrawRect(new Rect(box.x + (c % 2) * half, box.y + (c / 2) * half, half - 1f, half - 1f), CellColors[c]);
                    }
                    else
                    {
                        char ch = ToolChars[j];
                        DrawCell(box, ch, TakesColor(ch) ? m_Color : -1);
                    }
                    GUI.Label(new Rect(box.xMax + 5f, r.y, r.xMax - box.xMax - 7f, r.height), ToolNames[j], m_ToolLabel);
                }
                EditorGUILayout.EndHorizontal();
                GUILayout.Space(gap);
            }
        }

        GUIStyle WrappedArea
        {
            get
            {
                if (m_WrappedArea == null) m_WrappedArea = new GUIStyle(EditorStyles.textArea) { wordWrap = true };
                return m_WrappedArea;
            }
        }

        /// <summary>Small grey explanation that wraps inside the left column.</summary>
        void Hint(string text)
        {
            if (m_Hint == null) m_Hint = new GUIStyle(EditorStyles.wordWrappedMiniLabel);
            GUILayout.Label(text, m_Hint, GUILayout.Width(PanelInner - 4f));
        }

        void ResizeRow(string label, int side)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(56f));
            if (GUILayout.Button("+", EditorStyles.miniButtonLeft, GUILayout.Width(30f))) Resize(side, 1);
            if (GUILayout.Button("-", EditorStyles.miniButtonRight, GUILayout.Width(30f))) Resize(side, -1);
            EditorGUILayout.EndHorizontal();
        }

        void DrawStatus()
        {
            MessageType type = m_Solvable ? MessageType.Info : MessageType.Warning;
            EditorGUILayout.HelpBox(m_Status, type);
            if (!string.IsNullOrEmpty(m_Warning)) EditorGUILayout.HelpBox(m_Warning, MessageType.Warning);
            if (m_Solvable)
            {
                m_ShowSolution = EditorGUILayout.ToggleLeft("Show solution", m_ShowSolution);
                if (m_ShowSolution) EditorGUILayout.SelectableLabel(m_Solution, EditorStyles.wordWrappedLabel, GUILayout.MinHeight(34f));
            }
        }

        void DrawGrid()
        {
            EditLevel l = Current;
            Rect area = GUILayoutUtility.GetRect(100f, 100f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            const float gutter = 18f;   // room above and left of the grid for the delete buttons
            float size = Mathf.Floor(Mathf.Min(56f, Mathf.Min((area.width - 16f - gutter) / l.w, (area.height - 16f - gutter) / l.h)));
            if (size < 6f) return;
            var origin = new Vector2(area.x + gutter + Mathf.Floor((area.width - gutter - size * l.w) * 0.5f), area.y + 8f + gutter);
            Event e = Event.current;

            if (e.type == EventType.Repaint)
            {
                m_Glyph.fontSize = Mathf.RoundToInt(size * 0.5f);
                for (int x = 0; x < l.w; x++)
                    for (int y = 0; y < l.h; y++)
                        DrawCell(new Rect(origin.x + x * size, origin.y + y * size, size - 1f, size - 1f), l.tile[x, y], l.color[x, y]);
            }

            // Painting: one undo step per stroke.
            var gridRect = new Rect(origin.x, origin.y, size * l.w, size * l.h);
            if (e.type == EventType.MouseUp) m_Painting = false;
            bool press = e.type == EventType.MouseDown && gridRect.Contains(e.mousePosition);
            bool drag = e.type == EventType.MouseDrag && m_Painting;
            if ((press || drag) && (e.button == 0 || e.button == 1))
            {
                if (press)
                {
                    PushUndo();
                    m_Painting = true;
                    GUI.FocusControl(null);
                }
                int cx = Mathf.FloorToInt((e.mousePosition.x - origin.x) / size);
                int cy = Mathf.FloorToInt((e.mousePosition.y - origin.y) / size);
                Paint(cx, cy, e.button == 1 ? Tool.Floor : m_Tool);
                e.Use();
            }

            // A small x above every column and left of every row deletes exactly that one.
            if (size < 12f) return;
            if (m_LineButton == null)
                m_LineButton = new GUIStyle(EditorStyles.miniButton)
                {
                    padding = new RectOffset(0, 0, 0, 0), margin = new RectOffset(0, 0, 0, 0),
                    fontSize = 9, alignment = TextAnchor.MiddleCenter, fixedHeight = 0f
                };
            float b = Mathf.Min(16f, size - 2f);
            int removeColumn = -1, removeRow = -1;
            using (new EditorGUI.DisabledScope(l.w <= 3))
                for (int x = 0; x < l.w; x++)
                {
                    var r = new Rect(origin.x + x * size + Mathf.Floor((size - 1f - b) * 0.5f), origin.y - gutter, b, gutter - 3f);
                    if (GUI.Button(r, new GUIContent("x", "Delete column " + (x + 1)), m_LineButton)) removeColumn = x;
                }
            using (new EditorGUI.DisabledScope(l.h <= 3))
                for (int y = 0; y < l.h; y++)
                {
                    var r = new Rect(origin.x - gutter, origin.y + y * size + Mathf.Floor((size - 1f - b) * 0.5f), gutter - 3f, b);
                    if (GUI.Button(r, new GUIContent("x", "Delete row " + (y + 1)), m_LineButton)) removeRow = y;
                }
            if (removeColumn >= 0) RemoveLine(true, removeColumn);
            else if (removeRow >= 0) RemoveLine(false, removeRow);
        }

        void DrawCell(Rect r, char ch, int color)
        {
            Color wall = new Color(0.33f, 0.38f, 0.44f);
            Color floor = new Color(0.82f, 0.88f, 0.84f);
            bool pump = ch == '^' || ch == 'v' || ch == '<' || ch == '>';
            EditorGUI.DrawRect(r, ch == '#' || pump ? wall : floor);

            float pad = Mathf.Round(r.width * 0.12f);
            Rect inner = new Rect(r.x + pad, r.y + pad, r.width - pad * 2f, r.height - pad * 2f);
            Color own = CellColors[Mathf.Clamp(color < 0 ? 0 : color, 0, CellColors.Length - 1)];

            if (ch == 'T')
            {
                EditorGUI.DrawRect(inner, new Color(0.98f, 0.96f, 0.89f));
                float d = Mathf.Round(r.width * 0.3f);
                EditorGUI.DrawRect(new Rect(r.center.x - d * 0.5f, r.center.y - d * 0.5f, d, d),
                    color < 0 ? new Color(1f, 0.78f, 0.18f) : own);
            }
            else if (ch == 'X')
            {
                EditorGUI.DrawRect(inner, new Color(0.37f, 0.44f, 0.49f));
                Glyph(r, "X", new Color(1f, 0.35f, 0.4f));
            }
            else if (ch == 'H' || ch == 'P')
            {
                EditorGUI.DrawRect(inner, own);
                if (ch == 'H')
                {
                    float eye = Mathf.Max(2f, Mathf.Round(r.width * 0.11f));
                    float off = Mathf.Round(r.width * 0.14f);
                    EditorGUI.DrawRect(new Rect(r.center.x - off - eye * 0.5f, r.center.y - eye * 0.5f, eye, eye), new Color(0.15f, 0.14f, 0.18f));
                    EditorGUI.DrawRect(new Rect(r.center.x + off - eye * 0.5f, r.center.y - eye * 0.5f, eye, eye), new Color(0.15f, 0.14f, 0.18f));
                }
            }
            else if (ch == 'B')
            {
                // Loose body: blob colour, smaller and paler, no face.
                float inset = Mathf.Round(r.width * 0.22f);
                EditorGUI.DrawRect(new Rect(r.x + inset, r.y + inset, r.width - inset * 2f, r.height - inset * 2f), Color.Lerp(own, Color.white, 0.25f));
            }
            else if (ch == 'W')
            {
                EditorGUI.DrawRect(inner, new Color(0.25f, 0.42f, 0.33f));
                Glyph(r, "W", new Color(1f, 0.78f, 0.18f));
            }
            else if (ch == '*')
            {
                EditorGUI.DrawRect(inner, new Color(0.23f, 0.2f, 0.25f));
                Glyph(r, "*", Color.white);
            }
            else if (pump)
            {
                EditorGUI.DrawRect(inner, color < 0 ? new Color(0.29f, 0.65f, 0.97f) : own);
                Glyph(r, ch.ToString(), Color.white);
            }
        }

        void Glyph(Rect r, string text, Color color)
        {
            m_Glyph.normal.textColor = color;
            GUI.Label(r, text, m_Glyph);
        }

        static Color Hex(int rgb)
        {
            return new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
        }
    }
}
