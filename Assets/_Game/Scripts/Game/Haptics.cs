using UnityEngine;

namespace ExpandPuzzle
{
    /// <summary>
    /// Short vibrations for touch feedback. Android uses the system vibrator with a length and a
    /// strength per tap. iOS has no such control without a native plug-in, so only the strong taps
    /// do something there. Nothing happens in the editor or on devices without a vibrator.
    /// </summary>
    public static class Haptics
    {
        const string Key = "ExpandPuzzle.Haptics";

        /// <summary>The player's vibration switch (remembered between sessions).</summary>
        public static bool Enabled
        {
            get { return PlayerPrefs.GetInt(Key, 1) == 1; }
            set { PlayerPrefs.SetInt(Key, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>The faintest tick: one per cell the blob moves.</summary>
        public static void Tick() { Tap(8, 45); }

        /// <summary>Tiny tick: buttons, bumping a wall, a target clicking in.</summary>
        public static void Light() { Tap(12, 70); }

        /// <summary>A pump stroke, a forbidden tile.</summary>
        public static void Medium() { Tap(22, 140); }

        /// <summary>The last target clicking in.</summary>
        public static void Heavy() { Tap(38, 230, true); }

        /// <summary>Level solved: two quick taps and a long one.</summary>
        public static void Success()
        {
            Pattern(new long[] { 0, 28, 70, 28, 70, 60 }, new[] { 0, 150, 0, 190, 0, 255 }, true);
        }

        /// <summary>Lost a star: one soft, low buzz.</summary>
        public static void Fail() { Tap(60, 90); }

#if UNITY_ANDROID && !UNITY_EDITOR
        static AndroidJavaObject s_Vibrator;
        static AndroidJavaClass s_Effect;
        static int s_Sdk;
        static bool s_Ready, s_Broken, s_Amplitude;

        static bool Ready()
        {
            if (s_Ready) return true;
            if (s_Broken) return false;
            try
            {
                using (var version = new AndroidJavaClass("android.os.Build$VERSION")) s_Sdk = version.GetStatic<int>("SDK_INT");
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                    s_Vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
                if (s_Vibrator == null || !s_Vibrator.Call<bool>("hasVibrator")) { s_Broken = true; return false; }
                if (s_Sdk >= 26)
                {
                    s_Effect = new AndroidJavaClass("android.os.VibrationEffect");
                    s_Amplitude = s_Vibrator.Call<bool>("hasAmplitudeControl");
                }
                s_Ready = true;
            }
            catch (System.Exception)
            {
                s_Broken = true;
            }
            return s_Ready;
        }

        static void Tap(int milliseconds, int strength, bool strong = false)
        {
            if (!Enabled || !Ready()) return;
            try
            {
                if (s_Sdk >= 26)
                {
                    // Without amplitude control every tap is full strength, so keep the weak ones shorter.
                    int amplitude = s_Amplitude ? Mathf.Clamp(strength, 1, 255) : -1;
                    long length = s_Amplitude ? milliseconds : Mathf.Max(8, milliseconds * strength / 255);
                    using (var effect = s_Effect.CallStatic<AndroidJavaObject>("createOneShot", length, amplitude))
                        s_Vibrator.Call("vibrate", effect);
                }
                else
                {
                    s_Vibrator.Call("vibrate", (long)Mathf.Max(8, milliseconds * strength / 255));
                }
            }
            catch (System.Exception) { s_Broken = true; s_Ready = false; }
        }

        static void Pattern(long[] timings, int[] strengths, bool strong)
        {
            if (!Enabled || !Ready()) return;
            try
            {
                if (s_Sdk >= 26 && s_Amplitude)
                {
                    using (var effect = s_Effect.CallStatic<AndroidJavaObject>("createWaveform", timings, strengths, -1))
                        s_Vibrator.Call("vibrate", effect);
                }
                else
                {
                    s_Vibrator.Call("vibrate", timings, -1);
                }
            }
            catch (System.Exception) { s_Broken = true; s_Ready = false; }
        }
#else
        static void Tap(int milliseconds, int strength, bool strong = false)
        {
            if (!Enabled || !strong) return;
            Buzz();
        }

        static void Pattern(long[] timings, int[] strengths, bool strong)
        {
            if (!Enabled || !strong) return;
            Buzz();
        }
#endif

        /// <summary>
        /// The one vibration Unity offers everywhere. Also: because this call exists in the project,
        /// Unity adds the VIBRATE permission to the Android build, which the code above needs.
        /// </summary>
        static void Buzz()
        {
#if (UNITY_IOS || UNITY_ANDROID) && !UNITY_EDITOR
            Handheld.Vibrate();
#endif
        }
    }
}
