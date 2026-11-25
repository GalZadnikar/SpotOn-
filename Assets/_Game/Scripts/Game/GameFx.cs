using UnityEngine;

namespace ExpandPuzzle
{
    /// <summary>
    /// Particle effects for the juicy moments. Each slot takes any particle prefab
    /// (the scene builder fills them from Epic Toon FX); empty slots are simply skipped.
    /// </summary>
    public class GameFx : MonoBehaviour
    {
        [System.Serializable]
        public class Effect
        {
            public GameObject prefab;
            public float scale = 1f;
            public float lifetime = 3f;
        }

        [Tooltip("Air burst at the nozzle when a pump fires.")]
        public Effect pumpPuff = new Effect { scale = 0.45f, lifetime = 2f };
        [Tooltip("On every new cell when the blob inflates.")]
        public Effect growPop = new Effect { scale = 0.4f, lifetime = 2f };
        [Tooltip("Where the blob hits a wall.")]
        public Effect bump = new Effect { scale = 0.3f, lifetime = 1.5f };
        [Tooltip("When a cell lands on a target with the right colour.")]
        public Effect targetHit = new Effect { scale = 0.5f, lifetime = 1.5f };
        [Tooltip("When a cell lands on a forbidden tile.")]
        public Effect forbiddenHit = new Effect { scale = 0.35f, lifetime = 1.5f };
        [Header("Win celebration")]
        [Tooltip("On: star-shaped particles pop on the tiles when you win. Off: sparkles and a ring instead, so the only stars are the rating.")]
        public bool starParticlesOnWin = false;
        [Tooltip("One per target during the win sequence.")]
        public Effect winSparkle = new Effect { scale = 0.38f, lifetime = 3.5f };
        [Tooltip("The big finish.")]
        public Effect winConfetti = new Effect { scale = 0.9f, lifetime = 5f };
        public Effect winBurst = new Effect { scale = 0.9f, lifetime = 3.5f };
        [Tooltip("Used instead of the two above when Star Particles On Win is off.")]
        public Effect winSparkleNoStars = new Effect { scale = 0.7f, lifetime = 2f };
        public Effect winBurstNoStars = new Effect { scale = 0.8f, lifetime = 3f };

        public Effect WinSparkle { get { return starParticlesOnWin ? winSparkle : winSparkleNoStars; } }
        public Effect WinBurst { get { return starParticlesOnWin ? winBurst : winBurstNoStars; } }

        void Awake()
        {
            // Soft particles need the camera depth texture, which the mobile renderer does not draw:
            // on many phones (Pixels among them) such particles turn invisible. Switch them off.
            foreach (var prefab in AllPrefabs()) MakeMobileSafe(prefab, false);
        }

        public System.Collections.Generic.IEnumerable<GameObject> AllPrefabs()
        {
            foreach (var f in GetType().GetFields())
            {
                var e = f.GetValue(this) as Effect;
                if (e != null && e.prefab != null) yield return e.prefab;
            }
        }

        /// <summary>Turns off soft particles and camera fading on a prefab's particle materials.</summary>
        /// <returns>How many materials were changed.</returns>
        public static int MakeMobileSafe(GameObject prefab, bool markDirty)
        {
            int changed = 0;
            if (prefab == null) return 0;
            foreach (var r in prefab.GetComponentsInChildren<ParticleSystemRenderer>(true))
            {
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    bool soft = (m.HasProperty("_SoftParticlesEnabled") && m.GetFloat("_SoftParticlesEnabled") > 0.5f)
                                || m.IsKeywordEnabled("_FADING_ON") || m.IsKeywordEnabled("_SOFTPARTICLES_ON");
                    bool camFade = m.HasProperty("_CameraFadingEnabled") && m.GetFloat("_CameraFadingEnabled") > 0.5f;
                    if (!soft && !camFade) continue;

                    if (m.HasProperty("_SoftParticlesEnabled")) m.SetFloat("_SoftParticlesEnabled", 0f);
                    if (m.HasProperty("_CameraFadingEnabled")) m.SetFloat("_CameraFadingEnabled", 0f);
                    m.DisableKeyword("_FADING_ON");
                    m.DisableKeyword("_SOFTPARTICLES_ON");
#if UNITY_EDITOR
                    if (markDirty) UnityEditor.EditorUtility.SetDirty(m);
#endif
                    changed++;
                }
            }
            return changed;
        }

        /// <summary>Plays an effect at a world position, with its "up" pointing along <paramref name="direction"/>.</summary>
        public void Play(Effect fx, Vector3 position, Vector3 direction)
        {
            if (fx == null || fx.prefab == null) return;

            // Prefabs carry their own base rotation (most are authored to spray upwards).
            Quaternion rot = Quaternion.FromToRotation(Vector3.up, direction.sqrMagnitude > 0.001f ? direction.normalized : Vector3.up)
                             * fx.prefab.transform.rotation;
            GameObject go = Instantiate(fx.prefab, position, rot, transform);
            go.transform.localScale = fx.prefab.transform.localScale * fx.scale;

            var systems = go.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < systems.Length; i++)
            {
                var main = systems[i].main;
                main.loop = false;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            }

            // Effects from packs sometimes bring their own sounds or lights - keep it to particles.
            foreach (var audio in go.GetComponentsInChildren<AudioSource>(true)) audio.enabled = false;
            foreach (var l in go.GetComponentsInChildren<Light>(true)) l.enabled = false;

            Destroy(go, fx.lifetime);
        }

        public void Play(Effect fx, Vector3 position)
        {
            Play(fx, position, Vector3.up);
        }
    }
}
