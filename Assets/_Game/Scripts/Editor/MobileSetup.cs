using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace ExpandPuzzle.EditorTools
{
    /// <summary>
    /// Tools > Expand Puzzle > Apply Mobile Quality
    /// Tunes the quality level Android/iOS use for a small, crisp puzzle scene and makes the effects mobile safe.
    /// </summary>
    public static class MobileSetup
    {
        [MenuItem("Tools/Expand Puzzle/Apply Mobile Quality")]
        public static string Apply()
        {
            var log = new System.Text.StringBuilder();

            // The quality level phones start on.
            int level = DefaultLevel("Android");
            var asset = QualitySettings.GetRenderPipelineAssetAt(level) as UniversalRenderPipelineAsset;
            if (asset == null)
            {
                log.Append("No URP asset on quality level " + level + ". ");
                return log.ToString();
            }

            var so = new SerializedObject(asset);
            Set(so, "m_RenderScale", 1f, log);                 // was 0.8 = blurry on phones
            Set(so, "m_MSAA", 4, log);                         // smooth edges on the rounded shapes
            Set(so, "m_SupportsHDR", false, log);              // no tonemapping in use, saves bandwidth
            Set(so, "m_MainLightShadowsSupported", true, log);
            Set(so, "m_MainLightShadowmapResolution", 2048, log);
            Set(so, "m_ShadowCascadeCount", 1, log);           // whole board is close to the camera
            Set(so, "m_ShadowDistance", 40f, log);             // refined per level by CameraFramer
            Set(so, "m_SoftShadowsSupported", true, log);
            Set(so, "m_SoftShadowQuality", 2, log);            // medium
            Set(so, "m_ShadowDepthBias", 1f, log);
            Set(so, "m_ShadowNormalBias", 0.6f, log);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);

            QualitySettings.softParticles = false;

            // Portrait only: the HUD, the joystick and the board framing are built for it.
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;

            // Preview in the editor with what the phone will use.
            QualitySettings.SetQualityLevel(level, true);

            int fixedMaterials = FixEffects();
            AssetDatabase.SaveAssets();

            log.Append("Quality '" + QualitySettings.names[level] + "' (" + asset.name + ") tuned; ");
            log.Append(fixedMaterials + " effect materials had soft particles turned off.");
            Debug.Log("[ExpandPuzzle] " + log);
            return log.ToString();
        }

        /// <summary>Turns soft particles off on every material used by the game's effects.</summary>
        public static int FixEffects()
        {
            var fx = Object.FindFirstObjectByType<GameFx>();
            if (fx == null) return 0;
            int count = 0;
            foreach (var prefab in fx.AllPrefabs())
                count += GameFx.MakeMobileSafe(prefab, true);
            return count;
        }

        static int DefaultLevel(string platform)
        {
            var settings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0];
            var so = new SerializedObject(settings);
            var list = so.FindProperty("m_PerPlatformDefaultQuality");
            for (int i = 0; i < list.arraySize; i++)
            {
                var e = list.GetArrayElementAtIndex(i);
                if (e.FindPropertyRelative("first").stringValue == platform)
                    return e.FindPropertyRelative("second").intValue;
            }
            return 0;
        }

        static void Set(SerializedObject so, string name, float value, System.Text.StringBuilder log)
        {
            var p = so.FindProperty(name);
            if (p == null) { log.Append("(no " + name + ") "); return; }
            if (p.propertyType == SerializedPropertyType.Float) p.floatValue = value;
            else p.intValue = Mathf.RoundToInt(value);
        }

        static void Set(SerializedObject so, string name, int value, System.Text.StringBuilder log)
        {
            var p = so.FindProperty(name);
            if (p == null) { log.Append("(no " + name + ") "); return; }
            if (p.propertyType == SerializedPropertyType.Float) p.floatValue = value;
            else p.intValue = value;   // also right for enums, which are stored by value
        }

        static void Set(SerializedObject so, string name, bool value, System.Text.StringBuilder log)
        {
            var p = so.FindProperty(name);
            if (p == null) { log.Append("(no " + name + ") "); return; }
            p.boolValue = value;
        }
    }
}
