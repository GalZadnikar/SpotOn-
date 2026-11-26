using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace ExpandPuzzle
{
    /// <summary>
    /// The part of the game that deals with the loose pieces on a board:
    ///  - Movable walls: put a finger on one and drag it to another cell. Dropping it somewhere new is one move.
    ///  - Loose blob body: joins the blob when the blob pushes into it.
    ///  - Traps: destroy the blob cell that lands on them, then they are gone.
    /// The rules themselves live in <see cref="Play"/>; this is input, undo and effects.
    /// </summary>
    public partial class GameManager
    {
        BoardState m_State;
        PiecesView m_Pieces;

        int m_DragBlock = -1;
        Vector2Int m_DragCell;
        float m_DragCooldown;
        bool m_BlockHint;
        readonly List<Vector2Int> m_DragReach = new List<Vector2Int>();
        readonly List<float> m_DragHeights = new List<float>();
        static readonly List<RaycastResult> s_Hits = new List<RaycastResult>();

        /// <summary>The whole board as it is right now (blob, walls, loose body, traps).</summary>
        public BoardState State { get { return m_State; } }

        // ---------------------------------------------------------------- hooks called by the main part

        /// <summary>The board exists and the blob cells are set: place the pieces.</summary>
        void PiecesAfterBoardBuilt()
        {
            if (m_State == null) m_State = new BoardState(m_Cells);   // shares the blob with the main part
            m_State.ResetPieces(m_Level);
            m_DragBlock = -1;
            m_BlockHint = false;

            var go = new GameObject("Pieces");
            go.transform.SetParent(m_Board.transform, false);
            m_Pieces = go.AddComponent<PiecesView>();
            m_Pieces.Build(m_State, theme, m_LevelIndex);
        }

        void PiecesAfterUndo(BoardState pieces)
        {
            CancelDrag();
            ClearBlockHint();
            if (pieces != null) m_State.CopyPiecesFrom(pieces);
            if (m_Pieces != null)
            {
                m_Pieces.Sync(m_State);
                m_Pieces.SetNearby(m_Cells.Keys);
            }
        }

        /// <summary>The face needs a cell: when its own was destroyed it moves to the nearest one that is left.</summary>
        void FixHead()
        {
            if (m_Cells.ContainsKey(m_Head)) return;
            Vector2Int old = m_Head;
            float nearest = float.MaxValue;
            foreach (var c in m_Cells.Keys)
            {
                float d = (c - old).sqrMagnitude;
                if (d < nearest) { nearest = d; m_Head = c; }
            }
        }

        // ---------------------------------------------------------------- effects

        /// <summary>A trap snapped shut on a blob cell of this colour.</summary>
        void TrapSnap(Vector2Int cell, int color)
        {
            if (m_Pieces != null) m_Pieces.RemoveTrap(cell);
            Vector3 at = m_Board.CellToWorld(cell, m_Blob.height * 0.5f);

            Material[] blobMaterials = theme.BlobMaterials;
            Material piece = blobMaterials[Mathf.Clamp(color, 0, blobMaterials.Length - 1)];
            for (int i = 0; i < 10; i++)
            {
                Vector2 ring = Random.insideUnitCircle.normalized * Random.Range(1.2f, 3f);
                SpawnSpark(at + new Vector3(ring.x, 0f, ring.y) * 0.08f, new Vector3(ring.x, Random.Range(2.4f, 4.8f), ring.y), 13f,
                    Random.Range(0.11f, 0.22f), Random.Range(0.5f, 0.8f), piece, PrimitiveType.Cube);
            }
            if (fx != null)
            {
                fx.Play(fx.forbiddenHit, at + Vector3.up * 0.1f);
                fx.Play(fx.growPop, at);
            }
            sound.Play(sound.boosterPop, 1.15f, 0.8f);
            Haptics.Heavy();
            if (cameraFramer != null) cameraFramer.Punch(0.7f);
            m_Board.Ripple(cell, 0.13f, 10f);
        }

        void GrowPops(List<KeyValuePair<Vector2Int, int>> cells)
        {
            if (fx == null) return;
            int shown = Mathf.Min(cells.Count, 8);
            for (int i = 0; i < shown; i++)
                fx.Play(fx.growPop, m_Board.CellToWorld(cells[i].Key, m_Blob.height));
        }

        // ---------------------------------------------------------------- dragging a wall

        /// <summary>
        /// Call once per frame, before reading steps. True while a finger holds a movable wall:
        /// the blob takes no steps then.
        /// </summary>
        bool UpdateBlockDrag()
        {
            if (m_State == null || m_Board == null || m_Pieces == null || m_State.blocks.Count == 0) return false;
            Pointer pointer = Pointer.current;
            Camera cam = cameraFramer != null && cameraFramer.cam != null ? cameraFramer.cam : Camera.main;
            if (pointer == null || cam == null)
            {
                CancelDrag();
                return false;
            }
            Vector2 screen = pointer.position.ReadValue();

            if (m_DragBlock < 0)
            {
                if (!pointer.press.wasPressedThisFrame) return false;
                if (m_Solved || m_Aiming || m_BoosterBusy) return false;
                if (ui != null && ui.PickerOpen) return false;
                if (OverHud(screen)) return false;

                // The wall nearest to the finger, as long as the finger is about on it.
                Vector2 cell = CellPixels();
                float reach = Mathf.Max(28f, Mathf.Max(cell.x, cell.y) * 0.62f);
                int best = -1;
                for (int i = 0; i < m_State.blocks.Count; i++)
                {
                    Vector3 local = m_Pieces.BlockPosition(i) + Vector3.up * (PiecesView.BlockHeight * 0.6f);
                    Vector3 p = cam.WorldToScreenPoint(m_Board.transform.TransformPoint(local));
                    if (p.z <= 0f) continue;
                    float d = ((Vector2)p - screen).magnitude;
                    if (d < reach) { reach = d; best = i; }
                }
                if (best < 0) return false;
                BeginDrag(best);
                input.CancelSwipe();
                return true;
            }

            input.CancelSwipe();   // this finger moves the wall, not the blob
            if (pointer.press.isPressed)
            {
                Vector2Int over = NearestDragCell(cam, screen);
                if (over != m_DragCell)
                {
                    m_DragCell = over;
                    m_Pieces.SetBlockCell(m_DragBlock, over);
                    sound.Play(sound.move);
                    Haptics.Tick();
                }
            }
            else EndDrag();
            return true;
        }

        /// <summary>True when the finger is on a button or a menu rather than on the play area.</summary>
        static bool OverHud(Vector2 screen)
        {
            EventSystem es = EventSystem.current;
            if (es == null) return false;
            var data = new PointerEventData(es) { position = screen };
            s_Hits.Clear();
            es.RaycastAll(data, s_Hits);
            if (s_Hits.Count == 0) return false;
            GameObject top = s_Hits[0].gameObject;
            return top.GetComponentInParent<SwipePad>() == null && top.GetComponentInParent<Joystick>() == null;
        }

        /// <summary>The cell the dragged wall should show on: of all cells it may go to, the one nearest the finger.</summary>
        Vector2Int NearestDragCell(Camera cam, Vector2 screen)
        {
            Ray ray = cam.ScreenPointToRay(screen);
            var plane = new Plane(m_Board.transform.up, m_Board.CellToWorld(Vector2Int.zero, PiecesView.BlockHeight * 0.5f));
            float t;
            if (!plane.Raycast(ray, out t)) return m_DragCell;
            Vector3 local = m_Board.transform.InverseTransformPoint(ray.GetPoint(t));
            Vector2 finger = new Vector2(local.x, local.z);

            Vector2Int best = m_State.blocks[m_DragBlock];
            float nearest = ((Vector2)best - finger).sqrMagnitude;
            for (int i = 0; i < m_DragReach.Count; i++)
            {
                float d = ((Vector2)m_DragReach[i] - finger).sqrMagnitude;
                if (d < nearest) { nearest = d; best = m_DragReach[i]; }
            }
            return best;
        }

        void BeginDrag(int block)
        {
            ClearBlockHint();
            m_DragBlock = block;
            m_DragCell = m_State.blocks[block];
            Play.Reachable(m_Level, m_State, block, m_DragReach);
            m_DragHeights.Clear();
            for (int i = 0; i < m_DragReach.Count; i++) m_DragHeights.Add(0.1f);
            m_Board.ShowMarkers(m_DragReach, m_DragHeights);   // dots: everywhere this wall may go
            m_Pieces.SetBlockLifted(block, true);
            sound.Play(sound.boosterSelect);
            Haptics.Light();
        }

        void EndDrag()
        {
            int block = m_DragBlock;
            m_DragBlock = -1;
            m_DragCooldown = Time.unscaledTime + 0.12f;
            m_Board.HideMarkers();
            m_Pieces.SetBlockLifted(block, false);
            if (m_Solved || m_DragCell == m_State.blocks[block]) return;   // put back where it was: not a move

            PushUndo();
            m_State.blocks[block] = m_DragCell;
            m_PumpStreak = 0;
            sound.Play(sound.bump);
            Haptics.Medium();
            m_Board.Ripple(m_DragCell, 0.08f, 12f);
            AfterChange();
            ClearHint();
            UpdateBoosterAvailability();
        }

        /// <summary>Lets go of a wall without moving it (undo, restart).</summary>
        void CancelDrag()
        {
            if (m_DragBlock < 0) return;
            int block = m_DragBlock;
            m_DragBlock = -1;
            if (m_Board != null) m_Board.HideMarkers();
            if (m_Pieces != null && m_State != null && block < m_State.blocks.Count)
            {
                m_Pieces.SetBlockLifted(block, false);
                m_Pieces.SetBlockCell(block, m_State.blocks[block]);
            }
        }

        // ---------------------------------------------------------------- hint for a wall

        /// <summary>The next move of the solution is a wall drag: the wall hops and a dot shows where it goes.</summary>
        void ShowBlockHint(SolveMove step)
        {
            m_BlockHint = true;
            m_Board.ShowMarkers(new List<Vector2Int> { step.to }, new List<float> { 0.1f });
            if (m_Pieces != null) m_Pieces.HopBlock(step.block);
            if (m_Bar != null) m_Bar.Toast(BoosterType.Hint, "Drag the wall", "Put the wall with the yellow studs on the white dot.", 3f);
            HandDrag(step);
        }

        /// <summary>The hand presses on the blob and swipes one cell the way to go (also when that is a pump press).</summary>
        bool HandSwipe(Vector2Int dir)
        {
            Camera cam = cameraFramer != null && cameraFramer.cam != null ? cameraFramer.cam : Camera.main;
            if (m_HintHand == null || cam == null || m_Blob == null || m_Cells.Count == 0) return false;

            Vector2 middle = Vector2.zero;
            foreach (var c in m_Cells.Keys) middle += c;
            middle /= m_Cells.Count;
            Vector3 a = m_Board.transform.TransformPoint(new Vector3(middle.x, m_Blob.height, middle.y));
            Vector3 b = m_Board.transform.TransformPoint(new Vector3(middle.x + dir.x, m_Blob.height, middle.y + dir.y));
            m_HintHand.ShowSwipe(cam, a, b, m_Board.transform.TransformVector(Vector3.right));
            return true;
        }

        /// <summary>The hand presses on the movable wall and drags it onto its spot.</summary>
        void HandDrag(SolveMove step)
        {
            Camera cam = cameraFramer != null && cameraFramer.cam != null ? cameraFramer.cam : Camera.main;
            if (m_HintHand == null || cam == null || m_Pieces == null) return;
            Vector3 a = m_Board.transform.TransformPoint(m_Pieces.BlockPosition(step.block) + Vector3.up * PiecesView.BlockHeight);
            Vector3 b = m_Board.CellToWorld(step.to, PiecesView.BlockHeight);
            m_HintHand.ShowDrag(cam, a, b, m_Board.transform.TransformVector(Vector3.right));
        }

        void ClearBlockHint()
        {
            if (!m_BlockHint) return;
            m_BlockHint = false;
            if (m_Board != null && m_DragBlock < 0) m_Board.HideMarkers();
            if (m_HintHand != null) m_HintHand.Hide();
        }
    }
}
