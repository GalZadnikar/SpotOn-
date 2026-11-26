using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ExpandPuzzle
{
    /// <summary>
    /// The booster half of the game:
    ///  - Hint: arrows show the next moves of the shortest solution.
    ///  - Step back: the last move is taken back, move count included.
    ///  - Bomb: tap any block of the blob; a bomb flies from the button and destroys it.
    ///  - Star shower: one tap, and a star flies to every forbidden tile and turns it into plain floor.
    /// Using a booster never costs a move.
    /// </summary>
    public partial class GameManager
    {
        [Header("Boosters")]
        [Tooltip("Show the booster bar at the bottom. The undo button then leaves the top bar (Step back replaces it).")]
        public bool boosters = true;
        [Tooltip("PLACEHOLDER until ads or a shop exist: tapping an empty booster gives one for free.")]
        public bool freeRefillWhenEmpty = true;
        [Tooltip("How many moves one hint shows, one after another, as long as the player follows them.")]
        [Range(1, 6)] public int hintMoves = 3;

        BoosterBar m_Bar;
        bool m_Aiming, m_BoosterBusy;
        int m_InFlight;      // bombs and stars on their way
        BoosterType m_Aim;
        readonly List<Vector2Int> m_AimCells = new List<Vector2Int>();
        readonly List<float> m_AimHeights = new List<float>();

        // Forbidden tiles removed by the star stay removed when the level is restarted.
        readonly HashSet<Vector2Int> m_Cleared = new HashSet<Vector2Int>();
        int m_ClearedLevel = -1;

        readonly List<Vector2Int> m_Hint = new List<Vector2Int>();
        Transform m_HintArrow;
        Transform[] m_Chevrons;
        Mesh m_ChevronMesh;
        bool m_HintOnBlob;

        // ---------------------------------------------------------------- hooks called by the main part

        void SetupBoosters()
        {
            if (!boosters || ui == null) return;
            m_Bar = BoosterBar.Create(ui);
            if (m_Bar == null) return;
            m_Bar.Pressed += OnBoosterPressed;
            m_Bar.Tapped += OnBoardTapped;
            m_Bar.Cancelled += CancelAim;

            // Step back replaces the undo button; only restart stays at the top.
            if (ui.undoButton != null) ui.undoButton.gameObject.SetActive(false);
            // Keep the board clear of the bar.
            if (cameraFramer != null && cameraFramer.padBottom < 0.135f) cameraFramer.padBottom = 0.135f;
        }

        void OnDestroy()
        {
            if (m_Bar != null)
            {
                m_Bar.Pressed -= OnBoosterPressed;
                m_Bar.Tapped -= OnBoardTapped;
                m_Bar.Cancelled -= CancelAim;
            }
            if (m_ChevronMesh != null) Destroy(m_ChevronMesh);
        }

        /// <summary>True while a booster is being aimed or is in flight: the blob does not take steps then.</summary>
        bool BoostersBlockInput()
        {
            if (m_Bar == null) return false;
            if (m_Aiming)
            {
                var kb = Keyboard.current;
                if (kb != null && kb.escapeKey.wasPressedThisFrame) CancelAim();
                return true;
            }
            return m_BoosterBusy;
        }

        /// <summary>Right after the level data is loaded, before the board is built.</summary>
        void BoostersBeforeBoard()
        {
            if (m_ClearedLevel != m_LevelIndex)
            {
                m_Cleared.Clear();
                m_ClearedLevel = m_LevelIndex;
            }
            foreach (var c in m_Cleared) m_Level.forbidden.Remove(c);
        }

        void BoostersAfterLevelBuilt()
        {
            m_Aiming = false;
            m_BoosterBusy = false;
            m_InFlight = 0;
            m_Hint.Clear();
            m_HintArrow = null;   // it lived on the old board
            if (m_Bar == null) return;
            m_Bar.EndTargeting();
            m_Bar.SetVisible(true);
            UpdateBoosterAvailability();
        }

        void BoostersOnSolved()
        {
            if (m_Aiming) CancelAim();
            ClearHint();
            if (m_Bar != null) m_Bar.SetVisible(false);
        }

        void BoostersAfterUndo()
        {
            ClearHint();
            UpdateBoosterAvailability();
        }

        void UpdateBoosterAvailability()
        {
            if (m_Bar == null || m_Level == null) return;
            m_Bar.SetAvailable(BoosterType.StepBack, m_Undo.Count > 0);
            m_Bar.SetAvailable(BoosterType.Pop, PopTargets(null) > 0);
            m_Bar.SetAvailable(BoosterType.Clear, m_Level.forbidden.Count > 0);
        }

        // ---------------------------------------------------------------- pressing a booster

        void OnBoosterPressed(BoosterType type)
        {
            if (m_Solved || m_BoosterBusy || m_Level == null) return;
            if (ui != null && ui.PickerOpen) return;

            if (m_Aiming)
            {
                bool same = m_Aim == type;
                CancelAim();
                if (same) return;   // tapping the glowing button again puts it away
            }

            // Would it do anything right now? Say so before anything is spent or refilled.
            if (type == BoosterType.StepBack && m_Undo.Count == 0)
            {
                Deny(type, "Nothing to take back", "You have not moved yet.");
                return;
            }
            if (type == BoosterType.Pop && PopTargets(null) == 0)
            {
                Deny(type, "No block to spare", "The blob needs at least two blocks.");
                return;
            }
            if (type == BoosterType.Clear && m_Level.forbidden.Count == 0)
            {
                Deny(type, "Nothing to remove", "This level has no forbidden tiles.");
                return;
            }

            if (Boosters.Count(type) <= 0)
            {
                if (!freeRefillWhenEmpty)
                {
                    Deny(type, "None left", "You are out of this booster.");
                    return;
                }
                Boosters.Add(type, 1);
                m_Bar.Punch(type);
                sound.Play(sound.boosterRefill);
                Haptics.Light();
                m_Bar.Toast(type, "+1 " + Boosters.Title(type), "Free for now. Tap again to use it.");
                return;
            }

            switch (type)
            {
                case BoosterType.Hint:
                    UseHint();
                    break;

                case BoosterType.StepBack:
                    Boosters.Spend(type);
                    m_Bar.Punch(type);
                    Haptics.Medium();
                    Undo();
                    if (fx != null && m_Blob != null) fx.Play(fx.targetHit, m_Blob.TopCenter);
                    break;

                case BoosterType.Clear:
                    StarShower();
                    break;

                default:
                    BeginAim(type);
                    break;
            }
        }

        /// <summary>One star per forbidden tile, launched one after another; every tile becomes plain floor.</summary>
        void StarShower()
        {
            Camera cam = cameraFramer != null && cameraFramer.cam != null ? cameraFramer.cam : Camera.main;
            if (cam == null || !Boosters.Spend(BoosterType.Clear)) return;

            var tiles = new List<Vector2Int>(m_Level.forbidden);
            Vector2Int head = m_Head;
            tiles.Sort((a, b) => (a - head).sqrMagnitude.CompareTo((b - head).sqrMagnitude));

            m_Bar.Punch(BoosterType.Clear);
            sound.Play(sound.boosterThrow);
            Haptics.Light();
            ClearHint();
            m_BoosterBusy = true;
            for (int i = 0; i < tiles.Count; i++)
            {
                float height = m_Cells.ContainsKey(tiles[i]) ? m_Blob.height + 0.02f : 0.1f;
                StartCoroutine(Deliver(BoosterType.Clear, tiles[i], height, cam, i * 0.11f, i));
            }
        }

        void Deny(BoosterType type, string title, string text)
        {
            m_Bar.Deny(type);
            m_Bar.Toast(type, title, text);
            sound.Play(sound.boosterDeny);
            Haptics.Light();
        }

        // ---------------------------------------------------------------- aiming

        /// <summary>
        /// Blocks the bomb may hit: any of them, as long as one block is left afterwards. Bombing the
        /// head moves the face to a neighbour; bombing the middle leaves two pieces that still move as one.
        /// </summary>
        int PopTargets(List<Vector2Int> into)
        {
            if (m_Cells.Count < 2) return 0;
            if (into != null) into.AddRange(m_Cells.Keys);
            return m_Cells.Count;
        }

        void BeginAim(BoosterType type)
        {
            m_AimCells.Clear();
            m_AimHeights.Clear();
            PopTargets(m_AimCells);

            for (int i = 0; i < m_AimCells.Count; i++)
                m_AimHeights.Add(m_Cells.ContainsKey(m_AimCells[i]) ? m_Blob.height + 0.02f : 0.1f);

            m_Aiming = true;
            m_Aim = type;
            m_Board.ShowMarkers(m_AimCells, m_AimHeights);
            m_Bar.BeginTargeting(type, "Tap any block of the blob to destroy it.");
            sound.Play(sound.boosterSelect);
            Haptics.Light();
        }

        void CancelAim()
        {
            m_Aiming = false;
            if (m_Board != null) m_Board.HideMarkers();
            if (m_Bar != null) m_Bar.EndTargeting();
        }

        void OnBoardTapped(Vector2 screen)
        {
            if (!m_Aiming || m_BoosterBusy || m_Solved) return;
            Camera cam = cameraFramer != null && cameraFramer.cam != null ? cameraFramer.cam : Camera.main;
            if (cam == null) return;

            // The marker nearest to the finger wins, as long as the tap is about on its cell.
            Vector2 cell = CellPixels();
            float reach = Mathf.Max(24f, Mathf.Max(cell.x, cell.y) * 0.62f);
            int best = -1;
            float bestDist = reach;
            for (int i = 0; i < m_AimCells.Count; i++)
            {
                Vector3 p = cam.WorldToScreenPoint(m_Board.CellToWorld(m_AimCells[i], m_AimHeights[i]));
                if (p.z <= 0f) continue;
                float d = ((Vector2)p - screen).magnitude;
                if (d < bestDist) { bestDist = d; best = i; }
            }

            if (best < 0)
            {
                m_Bar.DenyTap();
                sound.Play(sound.boosterDeny);
                return;
            }

            BoosterType type = m_Aim;
            Vector2Int target = m_AimCells[best];
            float height = m_AimHeights[best];
            CancelAim();
            if (!Boosters.Spend(type)) return;
            m_Bar.Punch(type);
            sound.Play(sound.boosterThrow);
            Haptics.Light();
            ClearHint();
            m_BoosterBusy = true;
            StartCoroutine(Deliver(type, target, height, cam, 0f, 0));
        }

        // ---------------------------------------------------------------- delivery

        /// <param name="delay">Seconds to wait before leaving the button (a shower launches one after another).</param>
        /// <param name="order">0, 1, 2... within one shower: each landing sounds a little higher.</param>
        IEnumerator Deliver(BoosterType type, Vector2Int cell, float height, Camera cam, float delay, int order)
        {
            m_InFlight++;
            if (delay > 0f) yield return new WaitForSeconds(delay);

            // From the button (a point under it, a little closer to the camera than the board)...
            Vector3 target = m_Board.CellToWorld(cell, height);
            Vector2 from = m_Bar.ScreenPosition(type);
            Ray ray = cam.ScreenPointToRay(from);
            float depth = Vector3.Dot(target - cam.transform.position, cam.transform.forward);
            float along = depth / Mathf.Max(0.01f, Vector3.Dot(ray.direction, cam.transform.forward));
            Vector3 start = ray.origin + ray.direction * (along * 0.8f);

            // ...in a high curve that swings out to the side, onto the target.
            float span = (target - start).magnitude;
            float side = (cam.WorldToScreenPoint(target).x >= from.x ? -1f : 1f) * (order % 2 == 0 ? 1f : -0.6f);
            Vector3 control = Vector3.Lerp(start, target, 0.45f) + cam.transform.up * (span * 0.42f) + cam.transform.right * (side * span * 0.3f);

            Transform shot = BuildProjectile(type);
            Material trail = type == BoosterType.Pop ? theme.MarkMaterial(3) : theme.MarkMaterial(LevelData.AnyColor);
            float size = Mathf.Clamp(span * 0.085f, 0.4f, 0.9f);

            float t = 0f, nextSpark = 0f;
            Vector3 last = start;
            while (t < 1f)
            {
                t += Time.deltaTime / 0.6f;
                float k = Mathf.Clamp01(t);
                float e = k * k * (0.4f + 0.6f * k);          // leaves slowly, arrives fast
                Vector3 a = Vector3.Lerp(start, control, e), b = Vector3.Lerp(control, target, e);
                Vector3 pos = Vector3.Lerp(a, b, e);
                shot.position = pos;
                shot.localScale = Vector3.one * size * Mathf.Lerp(0.25f, 1f, Mathf.Clamp01(k * 5f));
                if (type == BoosterType.Pop)
                {
                    Vector3 heading = pos - last;
                    if (heading.sqrMagnitude > 0.000001f) shot.rotation = Quaternion.LookRotation(heading, cam.transform.up) * Quaternion.Euler(k * 500f, 0f, 0f);
                }
                else shot.Rotate(new Vector3(260f, 620f, 340f) * Time.deltaTime, Space.Self);
                last = pos;

                if (t >= nextSpark)
                {
                    nextSpark = t + 0.035f;
                    SpawnSpark(pos + Random.insideUnitSphere * 0.06f, Random.insideUnitSphere * 0.5f, 0f,
                        Random.Range(0.1f, 0.17f) * size / 0.6f, 0.32f, trail, PrimitiveType.Sphere);
                }
                yield return null;
            }
            Destroy(shot.gameObject);

            if (type == BoosterType.Pop) ApplyPop(cell, target);
            else ApplyClear(cell, target, order);

            // The board is judged once everything in the air has landed.
            m_InFlight--;
            if (m_InFlight <= 0)
            {
                m_InFlight = 0;
                m_BoosterBusy = false;
                AfterBoosterChange();
            }
        }

        void ApplyPop(Vector2Int cell, Vector3 at)
        {
            int color;
            if (!m_Cells.TryGetValue(cell, out color)) return;

            PushUndo();
            m_Cells.Remove(cell);
            if (cell == m_Head)
            {
                // The head is gone: the face moves to the nearest block that is left.
                float nearest = float.MaxValue;
                foreach (var c in m_Cells.Keys)
                {
                    float d = (c - cell).sqrMagnitude;
                    if (d < nearest) { nearest = d; m_Head = c; }
                }
            }
            m_Blob.Snap(m_Cells, m_Head);
            m_Blob.Startle();

            // Pieces of the block fly apart.
            Material[] blobMaterials = theme.BlobMaterials;
            Material piece = blobMaterials[Mathf.Clamp(color, 0, blobMaterials.Length - 1)];
            for (int i = 0; i < 12; i++)
            {
                Vector2 ring = Random.insideUnitCircle.normalized * Random.Range(1.4f, 3.4f);
                SpawnSpark(at + new Vector3(ring.x, 0f, ring.y) * 0.08f, new Vector3(ring.x, Random.Range(2.6f, 5.2f), ring.y), 13f,
                    Random.Range(0.12f, 0.24f), Random.Range(0.55f, 0.85f), piece, PrimitiveType.Cube);
            }
            if (fx != null)
            {
                fx.Play(fx.winBurstNoStars, at + Vector3.up * 0.1f);
                fx.Play(fx.growPop, at);
            }
            sound.Play(sound.boosterPop);
            Haptics.Heavy();
            if (cameraFramer != null) cameraFramer.Punch(0.9f);
            m_Board.Ripple(cell, 0.15f, 10f);
        }

        void ApplyClear(Vector2Int cell, Vector3 at, int order)
        {
            if (!m_Level.forbidden.Remove(cell)) return;
            m_Cleared.Add(cell);
            m_Board.ClearForbidden(cell);

            Material gold = theme.MarkMaterial(LevelData.AnyColor);
            for (int i = 0; i < 10; i++)
            {
                Vector2 ring = Random.insideUnitCircle.normalized * Random.Range(1.2f, 2.8f);
                SpawnSpark(at, new Vector3(ring.x, Random.Range(2.2f, 4.4f), ring.y), 11f,
                    Random.Range(0.08f, 0.16f), Random.Range(0.45f, 0.7f), gold, PrimitiveType.Sphere);
            }
            if (fx != null)
            {
                fx.Play(fx.winSparkleNoStars, at + Vector3.up * 0.1f);
                fx.Play(fx.targetHit, at);
            }
            sound.Play(sound.boosterClear, 1f + 0.07f * Mathf.Min(order, 8));
            Haptics.Medium();
            if (cameraFramer != null) cameraFramer.Punch(0.45f);
            if (order == 0) m_Board.Ripple(cell, 0.13f, 10f);
        }

        /// <summary>Like the end of a move, but without counting one.</summary>
        void AfterBoosterChange()
        {
            UpdateMovesUi();
            RefreshFeedback(true);
            UpdateBoosterAvailability();

            if (!m_Solved && Rules.IsSolved(m_Level, m_Cells))
            {
                m_Solved = true;
                if (!Levels.IsTemporary(m_LevelIndex)) SaveBestStars(m_LevelIndex, Rules.StarsFor(m_Level, m_Moves));
                BoostersOnSolved();
                StartCoroutine(WinRoutine());
            }
        }

        Transform BuildProjectile(BoosterType type)
        {
            var root = new GameObject(type == BoosterType.Pop ? "Bomb" : "Magic Star").transform;
            root.SetParent(m_Board.transform, true);   // goes away with the board on a restart

            if (type == BoosterType.Pop)
            {
                Piece(PrimitiveType.Sphere, root, theme.eye, Vector3.zero, Vector3.one * 0.72f, Quaternion.identity);
                Piece(PrimitiveType.Cylinder, root, theme.pumpNozzle, new Vector3(0f, 0.36f, 0f), new Vector3(0.26f, 0.07f, 0.26f), Quaternion.identity);
                Piece(PrimitiveType.Cylinder, root, theme.pumpKnob, new Vector3(0.04f, 0.5f, 0f), new Vector3(0.06f, 0.1f, 0.06f), Quaternion.Euler(0f, 0f, -20f));
                Piece(PrimitiveType.Sphere, root, theme.MarkMaterial(3), new Vector3(0.09f, 0.62f, 0f), Vector3.one * 0.2f, Quaternion.identity);
            }
            else
            {
                // Three crossed bars: from any side it reads as a twinkling star.
                Material gold = theme.MarkMaterial(LevelData.AnyColor);
                Quaternion tilt = Quaternion.Euler(45f, 45f, 0f);
                Piece(PrimitiveType.Cube, root, gold, Vector3.zero, new Vector3(1f, 0.2f, 0.2f), tilt);
                Piece(PrimitiveType.Cube, root, gold, Vector3.zero, new Vector3(0.2f, 1f, 0.2f), tilt);
                Piece(PrimitiveType.Cube, root, gold, Vector3.zero, new Vector3(0.2f, 0.2f, 1f), tilt);
                Piece(PrimitiveType.Sphere, root, theme.eyeShine, Vector3.zero, Vector3.one * 0.36f, Quaternion.identity);
            }
            return root;
        }

        static Transform Piece(PrimitiveType shape, Transform parent, Material material, Vector3 position, Vector3 scale, Quaternion rotation)
        {
            var go = GameObject.CreatePrimitive(shape);
            BoardView.StripCollider(go);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation;
            go.transform.localScale = scale;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go.transform;
        }

        void SpawnSpark(Vector3 position, Vector3 velocity, float gravity, float size, float life, Material material, PrimitiveType shape)
        {
            Transform t = Piece(shape, m_Board.transform, material, Vector3.zero, Vector3.one * size, Random.rotation);
            t.position = position;
            var spark = t.gameObject.AddComponent<BoosterSpark>();
            spark.velocity = velocity;
            spark.gravity = gravity;
            spark.life = life;
            spark.spin = Random.insideUnitSphere * 540f;
        }

        // ---------------------------------------------------------------- hint

        void UseHint()
        {
            if (m_Hint.Count > 0)
            {
                // Already showing one: point at it again instead of spending another.
                m_Bar.Punch(BoosterType.Hint);
                m_Bar.Toast(BoosterType.Hint, "Follow the arrows", "Your hint is still on the board.", 1.8f);
                return;
            }

            if (m_BlockHint)
            {
                m_Bar.Punch(BoosterType.Hint);
                m_Bar.Toast(BoosterType.Hint, "Drag the wall", "Your hint is still on the board.", 1.8f);
                return;
            }

            SolveResult result = Solver.Solve(m_Level, m_State, 60000);
            if (!result.solvable)
            {
                if (result.complete)
                    Deny(BoosterType.Hint, "No way from here", "This position can't be finished any more. Step back or restart.");
                else
                    Deny(BoosterType.Hint, "Too open to work out", "Try again a few moves further on.");
                return;
            }
            if (result.moves.Count == 0 || !Boosters.Spend(BoosterType.Hint)) return;

            if (result.steps[0].block >= 0)
            {
                // The next move is dragging a movable wall: show which one, and where it goes.
                ShowBlockHint(result.steps[0]);
                m_Bar.Punch(BoosterType.Hint);
                sound.Play(sound.boosterHint);
                Haptics.Light();
                return;
            }
            for (int i = 0; i < result.steps.Count && i < hintMoves && result.steps[i].block < 0; i++) m_Hint.Add(result.steps[i].dir);
            ShowHintArrow(true);
            m_Bar.Punch(BoosterType.Hint);
            sound.Play(sound.boosterHint);
            Haptics.Light();
        }

        /// <summary>The player moved: a step along the hint brings up the next arrow, any other step ends it.</summary>
        void HintAfterStep(Vector2Int dir)
        {
            UpdateBoosterAvailability();
            if (m_Hint.Count == 0) return;
            if (!m_Solved && dir == m_Hint[0])
            {
                m_Hint.RemoveAt(0);
                if (m_Hint.Count > 0) { ShowHintArrow(false); return; }
            }
            ClearHint();
        }

        void ClearHint()
        {
            m_Hint.Clear();
            if (m_HintArrow != null) m_HintArrow.gameObject.SetActive(false);
        }

        void ShowHintArrow(bool withSparkle)
        {
            if (m_Hint.Count == 0 || m_Board == null || m_Blob == null) return;

            if (m_HintArrow == null)
            {
                if (m_ChevronMesh == null) m_ChevronMesh = BuildChevron();
                m_HintArrow = new GameObject("Hint Arrow").transform;
                m_HintArrow.SetParent(m_Board.transform, false);
                m_Chevrons = new Transform[3];
                Material gold = theme.MarkMaterial(LevelData.AnyColor);
                for (int i = 0; i < m_Chevrons.Length; i++)
                {
                    var go = new GameObject("Chevron " + i);
                    go.transform.SetParent(m_HintArrow, false);
                    go.AddComponent<MeshFilter>().sharedMesh = m_ChevronMesh;
                    var r = go.AddComponent<MeshRenderer>();
                    r.sharedMaterial = gold;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    r.receiveShadows = false;
                    m_Chevrons[i] = go.transform;
                }
            }

            // The arrows sit at the side of the blob that leads the way.
            Vector2Int dir = m_Hint[0];
            int far = int.MinValue;
            foreach (var c in m_Cells.Keys) far = Mathf.Max(far, c.x * dir.x + c.y * dir.y);
            Vector2 edge = Vector2.zero;
            int count = 0;
            foreach (var c in m_Cells.Keys)
                if (c.x * dir.x + c.y * dir.y == far) { edge += c; count++; }
            edge /= Mathf.Max(1, count);

            // A walking step is shown on the floor ahead; a pump press on the blob itself, pointing at the pump.
            m_HintOnBlob = !Rules.CanMove(m_Level, m_Cells, dir);
            Vector2 at = edge + (Vector2)dir * 0.5f;
            float y = (m_HintOnBlob ? m_Blob.height : Mathf.Max(m_Blob.height, BoardView.WallHeight)) + 0.14f;
            m_HintArrow.localPosition = new Vector3(at.x, y, at.y);
            m_HintArrow.localRotation = Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.y), Vector3.up);
            m_HintArrow.gameObject.SetActive(true);

            if (withSparkle && fx != null) fx.Play(fx.targetHit, m_Board.transform.TransformPoint(m_HintArrow.localPosition));
        }

        void LateUpdate()
        {
            if (m_HintArrow == null || !m_HintArrow.gameObject.activeSelf || m_Chevrons == null) return;
            // A wave runs through the three chevrons in the direction to go.
            float time = Time.unscaledTime * 7f;
            for (int i = 0; i < m_Chevrons.Length; i++)
            {
                float wave = Mathf.Max(0f, Mathf.Sin(time - i * 1.2f));
                float size = (m_HintOnBlob ? 0.7f : 1f) * (0.78f + 0.4f * wave);
                float z = m_HintOnBlob ? -0.78f + i * 0.27f : 0.22f + i * 0.3f;
                m_Chevrons[i].localPosition = new Vector3(0f, 0.05f * wave, z + 0.05f * wave);
                m_Chevrons[i].localScale = new Vector3(size, 1f, size);
            }
        }

        /// <summary>A flat "V" pointing forward (+Z), visible from both sides.</summary>
        static Mesh BuildChevron()
        {
            var mesh = new Mesh { name = "Hint Chevron" };
            var v = new[]
            {
                new Vector3(0f, 0f, 0.17f), new Vector3(0.3f, 0f, -0.1f), new Vector3(0.3f, 0f, -0.27f),
                new Vector3(0f, 0f, 0f), new Vector3(-0.3f, 0f, -0.27f), new Vector3(-0.3f, 0f, -0.1f)
            };
            mesh.vertices = v;
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.triangles = new[]
            {
                0, 1, 2, 0, 2, 3, 0, 3, 4, 0, 4, 5,     // one side
                2, 1, 0, 3, 2, 0, 4, 3, 0, 5, 4, 0      // and the other
            };
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    /// <summary>A little piece that flies, spins, shrinks and removes itself: bomb trail, debris, star dust.</summary>
    class BoosterSpark : MonoBehaviour
    {
        public Vector3 velocity;
        public Vector3 spin;
        public float gravity;
        public float life = 0.3f;

        float m_Age;
        Vector3 m_Scale;

        void Start()
        {
            m_Scale = transform.localScale;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            m_Age += dt;
            float k = m_Age / life;
            if (k >= 1f)
            {
                Destroy(gameObject);
                return;
            }
            velocity.y -= gravity * dt;
            transform.position += velocity * dt;
            transform.Rotate(spin * dt, Space.Self);
            transform.localScale = m_Scale * (1f - k * k);
        }
    }
}
