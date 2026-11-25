using UnityEngine;

namespace ExpandPuzzle
{
    /// <summary>A little air pump that sits on the wall and points at the floor cell in front of it.</summary>
    public class PumpView : MonoBehaviour
    {
        Transform m_Body;
        Transform m_Plunger;
        Transform m_Tip;
        Mesh m_BodyMesh;

        // The little queue of spare blocks waiting behind the pump.
        const float SlotStart = 0.68f;
        const float SlotStep = 0.56f;
        Transform[] m_Supply;
        Transform m_Entering;
        Mesh m_SupplyMesh;
        float m_Feed;         // 1 right after a block was used, eases to 0

        bool m_Armed;
        float m_Press;        // 1 right after firing, eases back to 0
        float m_ArmedBlend;   // 0..1
        float m_Phase;

        public PumpData Data { get; private set; }

        /// <summary>World position of the nozzle opening, for effects.</summary>
        public Vector3 NozzlePosition { get { return transform.TransformPoint(new Vector3(0f, 0.14f, 0.55f)); } }

        /// <summary>World direction the pump blows in.</summary>
        public Vector3 BlowDirection { get { return transform.forward; } }

        /// <summary>How far behind the pump its supply queue reaches (for camera framing).</summary>
        public static float SupplyReach(int count)
        {
            return count <= 0 ? 0.4f : SlotStart + SlotStep * (count - 1) + 0.3f;
        }

        /// <param name="plainColor">Colour a plain pump wears: the blob's own colour.</param>
        /// <param name="supplyCount">Spare blocks lined up behind the pump (0 = none).</param>
        public void Build(PumpData data, GameTheme theme, int plainColor, int supplyCount)
        {
            Data = data;
            transform.localPosition = BoardView.CellToLocal(data.cell) + Vector3.up * BoardView.WallHeight;
            transform.localRotation = Quaternion.LookRotation(new Vector3(data.dir.x, 0f, data.dir.y), Vector3.up);
            m_Phase = (data.cell.x * 0.37f + data.cell.y * 0.61f) % 1f * 6.28f;

            // Body
            var body = new GameObject("Body");
            m_Body = body.transform;
            m_Body.SetParent(transform, false);
            m_BodyMesh = CellMesh.Build(new[] { Vector2Int.zero }, CellMesh.Settings.Create(0.17f, 0.2f, 0f, 0.3f, 0.09f, false));
            m_BodyMesh.name = "PumpBody";
            body.AddComponent<MeshFilter>().sharedMesh = m_BodyMesh;
            Material bodyMaterial = theme.PumpMaterial(data.color >= 0 ? data.color : plainColor);
            body.AddComponent<MeshRenderer>().sharedMaterial = bodyMaterial;

            // Supply queue: same-sized blocks in the pump's colour. Each pump stroke takes the front one.
            if (supplyCount > 0)
            {
                m_SupplyMesh = CellMesh.Build(new[] { Vector2Int.zero }, CellMesh.Settings.Create(0.27f, 0.13f, 0f, 0.25f, 0.07f, false));
                m_SupplyMesh.name = "PumpSupply";
                m_Supply = new Transform[supplyCount];
                for (int i = 0; i < supplyCount; i++) m_Supply[i] = SupplyBlock("Supply " + i, bodyMaterial);
                m_Entering = SupplyBlock("Supply (entering)", bodyMaterial);
                m_Entering.gameObject.SetActive(false);
            }

            // Nozzle, pointing forward (+Z)
            var nozzle = Part(PrimitiveType.Cylinder, "Nozzle", transform, theme.pumpNozzle);
            nozzle.localPosition = new Vector3(0f, 0.14f, 0.38f);
            nozzle.localRotation = Quaternion.Euler(90f, 0f, 0f);
            nozzle.localScale = new Vector3(0.2f, 0.13f, 0.2f);

            m_Tip = Part(PrimitiveType.Cylinder, "Tip", transform, theme.pumpKnob);
            m_Tip.localPosition = new Vector3(0f, 0.14f, 0.5f);
            m_Tip.localRotation = Quaternion.Euler(90f, 0f, 0f);
            m_Tip.localScale = new Vector3(0.27f, 0.035f, 0.27f);

            // Plunger: rod + handle
            var plunger = new GameObject("Plunger");
            m_Plunger = plunger.transform;
            m_Plunger.SetParent(transform, false);

            var rod = Part(PrimitiveType.Cylinder, "Rod", m_Plunger, theme.pumpNozzle);
            rod.localPosition = new Vector3(0f, 0.38f, -0.03f);
            rod.localScale = new Vector3(0.07f, 0.13f, 0.07f);

            var handle = Part(PrimitiveType.Cylinder, "Handle", m_Plunger, theme.pumpKnob);
            handle.localPosition = new Vector3(0f, 0.52f, -0.03f);
            handle.localRotation = Quaternion.Euler(0f, 0f, 90f);
            handle.localScale = new Vector3(0.1f, 0.19f, 0.1f);
        }

        Transform SupplyBlock(string name, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = m_SupplyMesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            return go.transform;
        }

        static Vector3 Slot(float index)
        {
            return new Vector3(0f, 0f, -(SlotStart + SlotStep * index));
        }

        static Transform Part(PrimitiveType type, string name, Transform parent, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            BoardView.StripCollider(go);
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go.transform;
        }

        /// <summary>Armed = the blob is touching the nozzle, one more push will inflate it.</summary>
        public void SetArmed(bool armed)
        {
            m_Armed = armed;
        }

        /// <param name="feed">True when the stroke really inflated the blob: one spare block goes in.</param>
        public void Fire(bool feed)
        {
            m_Press = 1f;
            if (feed && m_Supply != null) m_Feed = 1f;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            m_Press = Mathf.MoveTowards(m_Press, 0f, dt * 3.2f);
            m_ArmedBlend = Mathf.MoveTowards(m_ArmedBlend, m_Armed ? 1f : 0f, dt * 6f);

            // Quick push down, springy return.
            float p = m_Press;
            float push = p > 0.75f ? (1f - p) / 0.25f : Mathf.Sin(p / 0.75f * Mathf.PI * 0.5f);
            float wobble = Mathf.Sin((1f - p) * 22f) * p * 0.25f;

            float bob = Mathf.Sin(Time.time * 7f + m_Phase) * 0.035f * m_ArmedBlend;
            float idle = Mathf.Sin(Time.time * 1.6f + m_Phase) * 0.008f;

            if (m_Plunger != null)
                m_Plunger.localPosition = new Vector3(0f, -0.15f * push + bob + idle + 0.03f * m_ArmedBlend, 0f);

            if (m_Body != null)
            {
                float squash = push * 0.14f + wobble * 0.08f;
                m_Body.localScale = new Vector3(1f + squash * 0.6f, 1f - squash, 1f + squash * 0.6f);
            }

            UpdateSupply(dt);

            if (m_Tip != null)
            {
                float pulse = 1f + 0.12f * m_ArmedBlend * (0.5f + 0.5f * Mathf.Sin(Time.time * 7f + m_Phase)) + push * 0.35f;
                m_Tip.localScale = new Vector3(0.27f * pulse, 0.035f, 0.27f * pulse);
            }
        }

        void UpdateSupply(float dt)
        {
            if (m_Supply == null) return;

            m_Feed = Mathf.MoveTowards(m_Feed, 0f, dt * 4.2f);
            float t = 1f - m_Feed;
            t = t * t * (3f - 2f * t);                 // 0 -> 1 while the queue steps forward
            bool feeding = m_Feed > 0f;

            for (int i = 0; i < m_Supply.Length; i++)
            {
                // Everyone shuffles one place forward; a fresh block pops in at the back.
                Vector3 p = Slot(i + (1f - t));
                float wave = Mathf.Sin(Time.time * (3f + 5f * m_ArmedBlend) + m_Phase - i * 0.9f);
                p.y = Mathf.Max(0f, wave) * (0.02f + 0.05f * m_ArmedBlend) + Mathf.Sin(t * Mathf.PI) * 0.06f;
                m_Supply[i].localPosition = p;
                float s = feeding && i == m_Supply.Length - 1 ? t : 1f;
                m_Supply[i].localScale = new Vector3(s, s, s);
            }

            // The block that was at the front slides into the pump and is gone.
            m_Entering.gameObject.SetActive(feeding);
            if (feeding)
            {
                m_Entering.localPosition = Vector3.Lerp(Slot(0f), new Vector3(0f, 0.02f, -0.22f), t);
                float s = Mathf.Lerp(1f, 0.25f, t);
                m_Entering.localScale = new Vector3(s, s, s);
            }
        }

        void OnDestroy()
        {
            if (m_SupplyMesh != null) Destroy(m_SupplyMesh);
            if (m_BodyMesh != null) Destroy(m_BodyMesh);
        }
    }
}
