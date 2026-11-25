using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ExpandPuzzle
{
    /// <summary>
    /// Runs the game: loads levels, applies the rules to player input, keeps undo history
    /// and tells the views and effects what happened.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        [Header("Scene references (all optional - missing ones are created or skipped)")]
        public GameInput input;
        public GameUI ui;
        public CameraFramer cameraFramer;
        public GameFx fx;
        public Transform levelRoot;

        [Header("Look")]
        public GameTheme theme = new GameTheme();

        [Header("Flow")]
        public int startLevel = 0;
        [Tooltip("Continue from the last level reached instead of Start Level.")]
        public bool rememberProgress = false;
        [Tooltip("Level overview: a level stays locked until the one before it is solved. Off while testing.")]
        public bool lockLevels = false;
        [Tooltip("How long the celebration lasts before the next level.")]
        public float nextLevelDelay = 1.5f;
        [Tooltip("Turn each level the way that shows it biggest on this screen.")]
        public bool fitLevelToScreen = true;
        [Tooltip("Phones default to 30 fps. 60 makes the jelly feel smooth.")]
        public int targetFrameRate = 60;

        [Header("Feel")]
        [Tooltip("Sounds. Found on this object when left empty.")]
        public GameAudio sound;
        [Tooltip("When only one target is still open, it pulses.")]
        public bool hintLastTarget = true;

        const string ProgressKey = "ExpandPuzzle.Level";
        const string StarsKey = "ExpandPuzzle.Stars.";

        /// <summary>Best star rating reached on a level so far (0 = never solved).</summary>
        public static int BestStars(int index)
        {
            return PlayerPrefs.GetInt(StarsKey + index, 0);
        }

        static void SaveBestStars(int index, int stars)
        {
            if (stars <= BestStars(index)) return;
            PlayerPrefs.SetInt(StarsKey + index, stars);
            PlayerPrefs.Save();
        }

        /// <summary>With <see cref="lockLevels"/> on, a level opens once the one before it is solved.</summary>
        public bool IsUnlocked(int index)
        {
            return !lockLevels || index <= 0 || BestStars(index - 1) > 0;
        }

        struct Snapshot
        {
            public Vector2Int[] cells;
            public int[] colors;
            public Vector2Int head;
        }

        LevelData m_Level;
        int m_LevelIndex;
        readonly Dictionary<Vector2Int, int> m_Cells = new Dictionary<Vector2Int, int>();
        Vector2Int m_Head;
        readonly Stack<Snapshot> m_Undo = new Stack<Snapshot>();
        int m_Moves;
        bool m_Solved;

        readonly Dictionary<Vector2Int, CellStatus> m_Status = new Dictionary<Vector2Int, CellStatus>();
        readonly List<BadgeInfo> m_Badges = new List<BadgeInfo>();
        readonly HashSet<Vector2Int> m_Covered = new HashSet<Vector2Int>();

        BoardView m_Board;
        BlobView m_Blob;

        public int LevelIndex { get { return m_LevelIndex; } }
        public bool Solved { get { return m_Solved; } }
        public int Moves { get { return m_Moves; } }
        public int Stars { get { return m_Level != null ? Rules.StarsFor(m_Level, m_Moves) : 3; } }
        public LevelData Level { get { return m_Level; } }
        public IEnumerable<Vector2Int> Cells { get { return m_Cells.Keys; } }

        void Awake()
        {
            if (targetFrameRate > 0) Application.targetFrameRate = targetFrameRate;
            theme.Prepare();
            if (input == null) input = GetComponent<GameInput>();
            if (input == null) input = gameObject.AddComponent<GameInput>();
            if (cameraFramer == null) cameraFramer = GetComponent<CameraFramer>();
            if (cameraFramer == null) cameraFramer = gameObject.AddComponent<CameraFramer>();
            if (fx == null) fx = GetComponent<GameFx>();
            if (sound == null) sound = GetComponent<GameAudio>();
            if (sound == null) sound = gameObject.AddComponent<GameAudio>();   // no clips: silent, but safe to call
            if (levelRoot == null) levelRoot = transform;
        }

        void OnEnable()
        {
            if (ui != null)
            {
                ui.UndoClicked += Undo;
                ui.RestartClicked += Restart;
                ui.LevelPicked += JumpToLevel;
                ui.WinStarShown += OnWinStar;
            }
            PressScale.Pressed += OnButtonPressed;
        }

        void OnButtonPressed()
        {
            sound.Play(sound.click);
            Haptics.Light();
        }

        void OnWinStar(int index)
        {
            sound.Play(sound.winStar, 1f + 0.12f * index);
            Haptics.Light();
        }

        void OnDisable()
        {
            if (ui != null)
            {
                ui.UndoClicked -= Undo;
                ui.RestartClicked -= Restart;
                ui.LevelPicked -= JumpToLevel;
                ui.WinStarShown -= OnWinStar;
            }
            PressScale.Pressed -= OnButtonPressed;
        }

        void Start()
        {
            int index = startLevel;
            if (rememberProgress) index = PlayerPrefs.GetInt(ProgressKey, startLevel);
#if UNITY_EDITOR
            // The level editor leaves a level here when its "Test" button starts Play mode.
            string test = PlayerPrefs.GetString(Levels.TestLevelKey, "");
            if (test.Length > 0)
            {
                PlayerPrefs.DeleteKey(Levels.TestLevelKey);
                var parsed = LevelText.Parse(test);
                if (parsed.Count > 0) index = Levels.SetTemporary(parsed[0]);
            }
#endif
            if (ui != null)
            {
                ui.BuildPicker(Levels.Count, i => Levels.Load(i).UsesColor, BestStars, IsUnlocked);
                ui.AddPickerToggle("Swipe controls", () => GameInput.SwipeMode, () => GameInput.SwipeMode = !GameInput.SwipeMode);
                ui.AddPickerToggle("Sound", () => GameAudio.Enabled, () => GameAudio.Enabled = !GameAudio.Enabled);
                ui.AddPickerToggle("Vibration", () => Haptics.Enabled, () => { Haptics.Enabled = !Haptics.Enabled; Haptics.Medium(); });
                if (fx != null)
                    ui.AddPickerToggle("Star particles on win", () => fx.starParticlesOnWin, () => fx.starParticlesOnWin = !fx.starParticlesOnWin);
            }
            LoadLevel(index, true);
        }

        void Update()
        {
            Vector2Int dir;
            bool isRepeat;
            input.cellPixels = CellPixels();
            bool stepped = input.TryGetStep(out dir, out isRepeat);

            if (input.ToggleFxPressed && fx != null) fx.starParticlesOnWin = !fx.starParticlesOnWin;
            if (ui != null && ui.PickerOpen) return;

            if (input.RestartPressed) { Restart(); return; }
            if (input.UndoPressed) { Undo(); return; }
            if (input.SkipPressed) { LoadLevel(m_LevelIndex + 1, true); return; }
            if (input.BackPressed) { LoadLevel(m_LevelIndex - 1, true); return; }

            if (stepped) Step(dir, isRepeat);
        }

        /// <summary>
        /// How big one cell looks on screen right now (pixels; x = a step sideways, y = a step up).
        /// Swipe controls use it so that dragging a finger one cell far moves the blob one cell.
        /// </summary>
        Vector2 CellPixels()
        {
            Camera cam = cameraFramer != null && cameraFramer.cam != null ? cameraFramer.cam : Camera.main;
            if (cam == null || m_Board == null) return Vector2.zero;
            Vector3 here = cam.WorldToScreenPoint(m_Board.CellToWorld(m_Head, 0f));
            Vector3 side = cam.WorldToScreenPoint(m_Board.CellToWorld(m_Head + Vector2Int.right, 0f));
            Vector3 up = cam.WorldToScreenPoint(m_Board.CellToWorld(m_Head + Vector2Int.up, 0f));
            if (here.z <= 0f || side.z <= 0f || up.z <= 0f) return Vector2.zero;
            return new Vector2(((Vector2)(side - here)).magnitude, ((Vector2)(up - here)).magnitude);
        }

        // ---------------------------------------------------------------- levels

        /// <summary>Level editor: play a level that is not in the saved list (replaces the previous test level).</summary>
        public void PlayTestLevel(LevelDef def)
        {
            LoadLevel(Levels.SetTemporary(def), true);
        }

        /// <summary>Debug: go straight to a level (0-based).</summary>
        public void JumpToLevel(int index)
        {
            LoadLevel(index, true);
        }

        public void LoadLevel(int index, bool instantCamera)
        {
            StopAllCoroutines();
            if (ui != null) ui.SetFade(Color.white, 0f);
            BuildLevel(index, instantCamera);
        }

        void BuildLevel(int index, bool instantCamera)
        {
            m_LevelIndex = ((index % Levels.Count) + Levels.Count) % Levels.Count;
            if (rememberProgress)
            {
                PlayerPrefs.SetInt(ProgressKey, m_LevelIndex);
                PlayerPrefs.Save();
            }

            m_Level = Levels.Load(m_LevelIndex);
            if (fitLevelToScreen) m_Level = BestOrientation(m_Level);

            if (m_Board != null) Destroy(m_Board.gameObject);
            m_Blob = null;

            // The board colour changes every few levels.
            theme.ApplyPalette(m_LevelIndex);
            Color surface = theme.SurfaceColor(m_LevelIndex);

            var boardGo = new GameObject("Board - " + m_Level.name);
            boardGo.transform.SetParent(levelRoot, false);
            m_Board = boardGo.AddComponent<BoardView>();
            m_Board.Build(m_Level, theme);

            m_Cells.Clear();
            for (int i = 0; i < m_Level.startCells.Count; i++)
                m_Cells[m_Level.startCells[i]] = m_Level.startColors[i];
            m_Head = m_Level.startCells[0];
            m_Undo.Clear();
            m_Moves = 0;
            m_Solved = false;
            m_Status.Clear();

            var blobGo = new GameObject("Blob");
            blobGo.transform.SetParent(boardGo.transform, false);
            m_Blob = blobGo.AddComponent<BlobView>();
            m_Blob.Init(theme, m_Cells, m_Head);

            if (cameraFramer != null) cameraFramer.Frame(m_Board.WorldBounds, m_Board.PumpBounds, instantCamera);
            var mainCam = Camera.main;
            if (mainCam != null)
            {
                mainCam.clearFlags = CameraClearFlags.SolidColor;
                mainCam.backgroundColor = surface;
            }

            if (ui != null)
            {
                ui.SetLevel(m_LevelIndex + 1, Levels.Count);
                ui.SetInk(surface);
            }
            UpdateMovesUi();

            m_PumpStreak = 0;
            m_LastStars = 3;
            RefreshFeedback(false);
            m_Blob.DropIn();
            m_Board.Ripple(m_Head, 0.06f, 16f);
            sound.Play(sound.levelStart);
        }

        int m_PumpStreak;    // pump presses in a row: each one sounds a little higher
        int m_LastStars = 3;

        /// <summary>Keeps the level as authored or turns it a quarter, whichever shows the board bigger.</summary>
        LevelData BestOrientation(LevelData level)
        {
            LevelData turned = level.RotatedClockwise();
            if (cameraFramer == null)
            {
                bool portrait = Screen.height > Screen.width;
                return (level.height > level.width) == portrait ? level : turned;
            }
            Rect a = BoardView.LayoutRect(level, theme.pumpSupplyBlocks);
            Rect b = BoardView.LayoutRect(turned, theme.pumpSupplyBlocks);
            float sa = cameraFramer.EstimateScale(a.size);
            float sb = cameraFramer.EstimateScale(b.size);
            return sb > sa * 1.02f ? turned : level;
        }

        public void Restart()
        {
            LoadLevel(m_LevelIndex, false);
            sound.Play(sound.restart);
        }

        // ---------------------------------------------------------------- rules

        /// <summary>One push of the stick. Returns true when something changed.</summary>
        public bool Step(Vector2Int dir, bool isRepeat)
        {
            if (m_Solved || m_Level == null) return false;

            if (Rules.CanMove(m_Level, m_Cells, dir))
            {
                PushUndo();
                var moved = new List<KeyValuePair<Vector2Int, int>>(m_Cells.Count);
                foreach (var c in m_Cells) moved.Add(new KeyValuePair<Vector2Int, int>(c.Key + dir, c.Value));
                m_Cells.Clear();
                foreach (var c in moved) m_Cells[c.Key] = c.Value;
                m_Head += dir;
                m_Blob.MoveTo(m_Head, dir);
                m_PumpStreak = 0;
                sound.Play(sound.move);
                Haptics.Tick();   // one tick per cell, so steps can be counted by feel
                AfterChange();
                return true;
            }

            // Blocked. Pushing into a pump we are touching inflates us - but only on a fresh push,
            // so holding the stick against the wall cannot over-inflate by accident.
            int pumpIndex = Rules.FindPushedPump(m_Level, m_Cells, dir);
            if (pumpIndex >= 0)
            {
                if (isRepeat) return false;

                PumpData pump = m_Level.pumps[pumpIndex];
                PumpView view = m_Board.Pumps[pumpIndex];
                List<KeyValuePair<Vector2Int, int>> added = Rules.GrowCells(m_Level, m_Cells, pump);
                view.Fire(added.Count > 0);
                if (fx != null) fx.Play(fx.pumpPuff, view.NozzlePosition, view.BlowDirection);

                if (added.Count > 0)
                {
                    PushUndo();
                    foreach (var c in added) m_Cells[c.Key] = c.Value;
                    m_Blob.Grow(m_Cells, m_Head, pump.dir, pump.color);
                    m_PumpStreak++;
                    float rise = 1f + sound.pumpPitchStep * Mathf.Min(m_PumpStreak - 1, 10);
                    sound.Play(sound.pump, rise);
                    sound.Play(sound.grow, rise);
                    Haptics.Medium();
                    if (fx != null)
                    {
                        int shown = Mathf.Min(added.Count, 8);
                        for (int i = 0; i < shown; i++)
                            fx.Play(fx.growPop, m_Board.CellToWorld(added[i].Key, m_Blob.height));
                    }
                    if (cameraFramer != null) cameraFramer.Punch(0.25f);
                    AfterChange();
                    return true;
                }
            }

            if (!isRepeat)
            {
                m_Blob.Bump(dir);
                sound.Play(sound.bump);
                Haptics.Light();
                if (fx != null)
                {
                    // Puff where the blob meets the wall.
                    foreach (var c in m_Cells)
                    {
                        if (m_Level.IsFloor(c.Key + dir)) continue;
                        Vector3 p = m_Board.CellToWorld(c.Key, m_Blob.height * 0.6f) + new Vector3(dir.x, 0f, dir.y) * 0.5f;
                        fx.Play(fx.bump, p, new Vector3(-dir.x, 0.6f, -dir.y));
                        break;
                    }
                }
            }
            return false;
        }

        void AfterChange()
        {
            m_Moves++;
            UpdateMovesUi();
            RefreshFeedback(true);

            // A move that costs a star is felt and heard (the HUD drops the star).
            int stars = Rules.StarsFor(m_Level, m_Moves);
            if (stars < m_LastStars)
            {
                sound.Play(sound.starLost);
                Haptics.Fail();
            }
            m_LastStars = stars;

            if (Rules.IsSolved(m_Level, m_Cells))
            {
                m_Solved = true;
                if (!Levels.IsTemporary(m_LevelIndex)) SaveBestStars(m_LevelIndex, Rules.StarsFor(m_Level, m_Moves));
                StartCoroutine(WinRoutine());
            }
        }

        /// <summary>Updates pump hints and the marks on top of the blob; sparks on newly hit tiles.</summary>
        void RefreshFeedback(bool withEffects)
        {
            for (int i = 0; i < m_Level.pumps.Count; i++)
                m_Board.Pumps[i].SetArmed(m_Cells.ContainsKey(m_Level.pumps[i].Nozzle));

            m_Badges.Clear();
            m_Covered.Clear();
            int newOk = 0, newForbidden = 0, newWrong = 0;
            foreach (var c in m_Cells)
            {
                CellStatus status = Rules.StatusOf(m_Level, c.Key, c.Value);
                if (status == CellStatus.Plain) continue;
                if (status == CellStatus.TargetOk) m_Covered.Add(c.Key);

                int need = LevelData.AnyColor;
                m_Level.targets.TryGetValue(c.Key, out need);
                m_Badges.Add(new BadgeInfo
                {
                    cell = c.Key,
                    status = status,
                    needColor = status == CellStatus.TargetWrongColor ? need : LevelData.AnyColor,
                    cellColor = c.Value
                });

                if (withEffects)
                {
                    CellStatus before;
                    bool had = m_Status.TryGetValue(c.Key, out before);
                    if (!had || before != status)
                    {
                        if (status == CellStatus.TargetOk) newOk++;
                        else if (status == CellStatus.Forbidden) newForbidden++;
                        else if (status == CellStatus.TargetWrongColor) newWrong++;

                        if (fx != null)
                        {
                            Vector3 top = m_Board.CellToWorld(c.Key, m_Blob.height + 0.05f);
                            if (status == CellStatus.TargetOk) fx.Play(fx.targetHit, top);
                            else if (status == CellStatus.Forbidden) fx.Play(fx.forbiddenHit, top);
                        }
                    }
                }
            }

            // One sound per step, the most important one: trouble first, then progress.
            if (newForbidden > 0)
            {
                sound.Play(sound.forbidden);
                Haptics.Medium();
            }
            else if (newWrong > 0) sound.Play(sound.wrongColor);
            else if (newOk > 0)
            {
                sound.Play(sound.targetCovered, 1f + sound.targetPitchStep * Mathf.Min(m_Covered.Count - 1, 12));
                Haptics.Light();
            }

            // The board shows which targets are done; the blob learns where the nearest open one is.
            m_Board.SetCovered(m_Covered, hintLastTarget && !m_Solved);
            Vector2 interest = Vector2.zero;
            float nearest = float.MaxValue;
            foreach (var t in m_Level.targets)
            {
                if (m_Covered.Contains(t.Key)) continue;
                Vector2 d = t.Key - m_Head;
                if (d.sqrMagnitude < nearest) { nearest = d.sqrMagnitude; interest = d; }
            }
            if (m_Blob != null) m_Blob.interest = interest;

            m_Status.Clear();
            for (int i = 0; i < m_Badges.Count; i++) m_Status[m_Badges[i].cell] = m_Badges[i].status;
            m_Blob.SetBadges(m_Badges);
        }

        void UpdateMovesUi()
        {
            if (ui == null) return;
            int stars = Rules.StarsFor(m_Level, m_Moves);
            ui.SetMoves(m_Moves, Rules.MoveLimit(m_Level, stars), stars);
        }

        // ---------------------------------------------------------------- winning

        IEnumerator WinRoutine()
        {
            // 1. The click: everything settles into place.
            yield return new WaitForSeconds(0.16f);
            m_Blob.Settle();
            sound.Play(sound.winSettle);
            Haptics.Heavy();
            m_Board.SetCovered(m_Covered, false);
            if (cameraFramer != null) cameraFramer.Punch(0.45f);
            m_Board.Ripple(m_Head, 0.17f, 9f);

            // 2. Every target lights up, nearest first.
            var targets = new List<Vector2Int>(m_Level.targets.Keys);
            Vector2Int head = m_Head;
            targets.Sort((a, b) => (a - head).sqrMagnitude.CompareTo((b - head).sqrMagnitude));
            float gap = Mathf.Min(0.09f, 0.55f / Mathf.Max(1, targets.Count));
            for (int i = 0; i < targets.Count; i++)
            {
                m_Blob.PopBadge(targets[i]);
                if (fx != null) fx.Play(fx.WinSparkle, m_Board.CellToWorld(targets[i], m_Blob.height + 0.05f));
                yield return new WaitForSeconds(gap);
            }
            yield return new WaitForSeconds(0.18f);

            // 3. The party.
            m_Blob.Celebrate();
            sound.Play(sound.winJingle);
            Haptics.Success();
            if (cameraFramer != null) cameraFramer.Punch(1f);
            if (fx != null)
            {
                Vector3 top = m_Blob.TopCenter;
                fx.Play(fx.WinBurst, top + Vector3.up * 0.2f);
                fx.Play(fx.winConfetti, top + Vector3.up * 0.4f);
            }

            // Let the party be seen for a moment before the result card covers the middle.
            yield return new WaitForSeconds(0.55f);
            if (ui != null) ui.ShowWin(m_LevelIndex + 1, Rules.StarsFor(m_Level, m_Moves), m_Moves);
            yield return new WaitForSeconds(nextLevelDelay + (ui != null ? ui.WinExtraTime : 0f));

            // 4. Wipe to the next level (in its own colour). A level under test simply starts again.
            int next = Levels.IsTemporary(m_LevelIndex) ? m_LevelIndex : (m_LevelIndex + 1) % Levels.Count;
            Color wipe = theme.SurfaceColor(next);
            yield return Fade(wipe, 0f, 1f, 0.24f);
            BuildLevel(next, true);
            yield return Fade(wipe, 1f, 0f, 0.3f);
        }

        IEnumerator Fade(Color color, float from, float to, float duration)
        {
            if (ui == null) yield break;
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                ui.SetFade(color, Mathf.SmoothStep(from, to, t / duration));
                yield return null;
            }
            ui.SetFade(color, to);
        }

        // ---------------------------------------------------------------- undo

        void PushUndo()
        {
            var snap = new Snapshot { cells = new Vector2Int[m_Cells.Count], colors = new int[m_Cells.Count], head = m_Head };
            int i = 0;
            foreach (var c in m_Cells)
            {
                snap.cells[i] = c.Key;
                snap.colors[i] = c.Value;
                i++;
            }
            m_Undo.Push(snap);
        }

        public void Undo()
        {
            if (m_Solved || m_Undo.Count == 0) return;
            Snapshot snap = m_Undo.Pop();
            m_Cells.Clear();
            for (int i = 0; i < snap.cells.Length; i++) m_Cells[snap.cells[i]] = snap.colors[i];
            m_Head = snap.head;
            m_Moves++;   // taking a move back is not free: it counts as a move
            m_Blob.Snap(m_Cells, m_Head);
            m_PumpStreak = 0;
            sound.Play(sound.undo);
            UpdateMovesUi();
            RefreshFeedback(false);
            m_LastStars = Rules.StarsFor(m_Level, m_Moves);
        }
    }
}
