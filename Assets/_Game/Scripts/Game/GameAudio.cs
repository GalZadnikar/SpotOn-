using UnityEngine;

namespace ExpandPuzzle
{
    /// <summary>
    /// All the game's sounds in one place. Each slot takes any clip; empty slots are simply silent.
    /// The scene builder fills them from the "Casual Game Sounds U6" pack.
    /// </summary>
    public class GameAudio : MonoBehaviour
    {
        [System.Serializable]
        public class Sound
        {
            public AudioClip clip;
            [Range(0f, 1f)] public float volume = 1f;
            [Range(0.3f, 3f)] public float pitch = 1f;
            [Tooltip("Random pitch change each time it plays, so repeats do not sound mechanical.")]
            [Range(0f, 0.3f)] public float pitchJitter = 0f;
        }

        [Range(0f, 1f)] public float masterVolume = 0.8f;

        [Header("Background music")]
        [Tooltip("Tracks played one after another in random order. Leave empty for no music.")]
        public AudioClip[] music;
        [Tooltip("Kept low so it sits behind the game sounds.")]
        [Range(0f, 1f)] public float musicVolume = 0.25f;
        [Tooltip("Seconds to fade in at the start of each track and out at its end.")]
        [Range(0f, 10f)] public float musicFade = 3f;

        [Header("Moving")]
        [Tooltip("Every step of the blob. Keep it quiet: it plays all the time.")]
        public Sound move = new Sound { volume = 0.3f, pitchJitter = 0.08f };
        [Tooltip("Walking into a wall, or pumping when nothing can grow.")]
        public Sound bump = new Sound { volume = 0.45f, pitchJitter = 0.05f };

        [Header("Pumping")]
        public Sound pump = new Sound { volume = 0.7f };
        [Tooltip("The new cells popping out.")]
        public Sound grow = new Sound { volume = 0.55f };
        [Tooltip("Each pump press in a row sounds this much higher.")]
        [Range(0f, 0.2f)] public float pumpPitchStep = 0.06f;

        [Header("Tiles")]
        [Tooltip("A target gets covered with the right colour. Higher with every target that is already covered.")]
        public Sound targetCovered = new Sound { volume = 0.6f };
        [Range(0f, 0.2f)] public float targetPitchStep = 0.05f;
        public Sound forbidden = new Sound { volume = 0.6f };
        [Tooltip("A target covered by the wrong colour.")]
        public Sound wrongColor = new Sound { volume = 0.5f };

        [Header("Stars and winning")]
        [Tooltip("A move just cost a star.")]
        public Sound starLost = new Sound { volume = 0.6f };
        [Tooltip("The click when the last target is covered.")]
        public Sound winSettle = new Sound { volume = 0.7f };
        public Sound winJingle = new Sound { volume = 0.8f };
        [Tooltip("Each star landing on the win card (higher for every next star).")]
        public Sound winStar = new Sound { volume = 0.7f };

        [Header("Level and buttons")]
        public Sound levelStart = new Sound { volume = 0.5f };
        public Sound undo = new Sound { volume = 0.5f };
        public Sound restart = new Sound { volume = 0.5f };
        public Sound click = new Sound { volume = 0.5f };

        [Header("Boosters")]
        [Tooltip("A booster that needs a target was picked up.")]
        public Sound boosterSelect = new Sound { volume = 0.55f };
        [Tooltip("The bomb or the star leaves its button.")]
        public Sound boosterThrow = new Sound { volume = 0.6f };
        [Tooltip("The bomb goes off and a block is gone.")]
        public Sound boosterPop = new Sound { volume = 0.85f };
        [Tooltip("The star lands and a forbidden tile is gone.")]
        public Sound boosterClear = new Sound { volume = 0.75f };
        public Sound boosterHint = new Sound { volume = 0.65f };
        [Tooltip("Not possible right now, or a tap that was no valid target.")]
        public Sound boosterDeny = new Sound { volume = 0.5f };
        [Tooltip("An empty booster was topped up.")]
        public Sound boosterRefill = new Sound { volume = 0.6f };

        const string Key = "ExpandPuzzle.Sound";
        const int Voices = 6;

        /// <summary>The player's sound switch (remembered between sessions).</summary>
        public static bool Enabled
        {
            get { return PlayerPrefs.GetInt(Key, 1) == 1; }
            set { PlayerPrefs.SetInt(Key, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        AudioSource[] m_Sources;
        AudioSource m_Long;     // jingles get their own voice so quick sounds never cut them off
        int m_Next;
        AudioSource m_Music;
        int m_Track = -1;
        float m_MusicLevel;     // 0..1 fade for the sound switch

        void Awake()
        {
            m_Sources = new AudioSource[Voices];
            for (int i = 0; i < Voices; i++) m_Sources[i] = NewSource();
            m_Long = NewSource();
            m_Music = NewSource();
            m_Music.priority = 0;
            m_Music.ignoreListenerPause = true;

            // Something has to listen.
            if (FindFirstObjectByType<AudioListener>() == null)
            {
                Camera cam = Camera.main;
                (cam != null ? cam.gameObject : gameObject).AddComponent<AudioListener>();
            }
        }

        void Start()
        {
            m_MusicLevel = Enabled ? 1f : 0f;
            NextTrack();
        }

        void Update()
        {
            if (m_Music == null || music == null || music.Length == 0) return;

            // Follow the sound switch with a short fade instead of a hard cut.
            m_MusicLevel = Mathf.MoveTowards(m_MusicLevel, Enabled ? 1f : 0f, Time.unscaledDeltaTime * 2f);

            if (m_Music.clip != null && !m_Music.isPlaying && m_Music.time == 0f) NextTrack();
            if (m_Music.clip == null) return;

            // Fade in at the start and out at the end of each track.
            float t = m_Music.time, len = m_Music.clip.length, edge = 1f;
            if (musicFade > 0f) edge = Mathf.Clamp01(Mathf.Min(t, len - t) / musicFade);
            m_Music.volume = musicVolume * masterVolume * m_MusicLevel * edge;
        }

        void NextTrack()
        {
            if (m_Music == null || music == null || music.Length == 0) return;
            int pick = Random.Range(0, music.Length);
            if (music.Length > 1 && pick == m_Track) pick = (pick + 1) % music.Length;   // no repeats back to back
            m_Track = pick;
            if (music[pick] == null) return;
            m_Music.clip = music[pick];
            m_Music.volume = 0f;
            m_Music.Play();
        }

        AudioSource NewSource()
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = 0f;
            s.loop = false;
            return s;
        }

        public void Play(Sound sound, float pitchScale = 1f, float volumeScale = 1f)
        {
            if (sound == null || sound.clip == null || !Enabled || m_Sources == null) return;

            AudioSource source;
            if (sound.clip.length > 1f) source = m_Long;
            else
            {
                source = m_Sources[m_Next];
                m_Next = (m_Next + 1) % m_Sources.Length;
            }

            float jitter = sound.pitchJitter > 0f ? Random.Range(-sound.pitchJitter, sound.pitchJitter) : 0f;
            source.pitch = Mathf.Clamp(sound.pitch * pitchScale * (1f + jitter), 0.3f, 3f);
            source.volume = Mathf.Clamp01(sound.volume * volumeScale * masterVolume);
            source.clip = sound.clip;
            source.Play();
        }
    }
}
