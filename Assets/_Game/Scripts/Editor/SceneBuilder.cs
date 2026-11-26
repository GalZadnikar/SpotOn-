using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace ExpandPuzzle.EditorTools
{
    /// <summary>
    /// Tools > Expand Puzzle > Build Scene
    /// Sets up the open scene for the game: materials, camera rig, lighting, game objects and HUD.
    /// Safe to run again - it replaces what it created before and keeps existing material assets.
    /// </summary>
    public static class SceneBuilder
    {
        const string MaterialDir = "Assets/_Game/Materials";
        const string SettingsDir = "Assets/_Game/Settings";
        const string JoystickPrefab = "Assets/Joystick Pack/Prefabs/Floating Joystick.prefab";

        const string GameRootName = "ExpandPuzzle";
        const string UiRootName = "Game UI";
        const string CmName = "CM Camera";
        const string WinCanvasName = "Win Canvas";
        const string WinCameraName = "Win Card Camera";
        const string DefaultStarEffect = "Assets/EpicVictoryEffects/Prefabs/Star_01/Star_01_Anim_01.prefab";
        const int UiLayer = 5;

        [MenuItem("Tools/Expand Puzzle/Build Scene")]
        public static void Build()
        {
            DestroyRoot(GameRootName);
            DestroyRoot(UiRootName);
            DestroyRoot(CmName);
            DestroyRoot(WinCanvasName);
            DestroyRoot(WinCameraName);

            GameTheme theme = BuildTheme();

            // ---- camera
            Camera cam = Camera.main;
            if (cam == null) cam = Object.FindFirstObjectByType<Camera>();
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera");
                camGo.tag = "MainCamera";
                cam = camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
            }
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = theme.SurfaceColor(0);
            cam.fieldOfView = 30f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 200f;
            if (cam.GetComponent<CinemachineBrain>() == null) cam.gameObject.AddComponent<CinemachineBrain>();

            var vcamGo = new GameObject(CmName);
            var vcam = vcamGo.AddComponent<CinemachineCamera>();
            vcam.Lens.FieldOfView = 30f;
            vcam.Lens.NearClipPlane = 0.3f;
            vcam.Lens.FarClipPlane = 200f;
            vcamGo.transform.SetPositionAndRotation(new Vector3(4f, 20f, -8f), Quaternion.Euler(68f, 0f, 0f));

            // ---- light
            Light sun = null;
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) { sun = l; break; }
            if (sun == null)
            {
                var lightGo = new GameObject("Directional Light");
                sun = lightGo.AddComponent<Light>();
                sun.type = LightType.Directional;
            }
            // Light comes from the back-left so highlights sit on the far edges and shadows fall towards the player.
            sun.transform.rotation = Quaternion.Euler(52f, 150f, 0f);
            sun.useColorTemperature = false;
            sun.color = Color.white;
            sun.intensity = 0.9f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.85f;

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.31f, 0.33f, 0.36f);
            RenderSettings.fog = false;

            BuildVolume();

            // ---- game
            var root = new GameObject(GameRootName);
            var manager = root.AddComponent<GameManager>();
            var input = root.AddComponent<GameInput>();
            var framer = root.AddComponent<CameraFramer>();
            framer.rig = vcamGo.transform;
            framer.cam = cam;
            framer.fieldOfView = 30f;
            framer.pitch = 68f;

            var levelRoot = new GameObject("Level");
            levelRoot.transform.SetParent(root.transform, false);

            var fx = root.AddComponent<GameFx>();
            AssignEffects(fx);
            AssignSounds(root.AddComponent<GameAudio>());

            manager.input = input;
            manager.fx = fx;
            manager.sound = root.GetComponent<GameAudio>();
            manager.cameraFramer = framer;
            manager.levelRoot = levelRoot.transform;
            manager.theme = theme;

            // ---- HUD
            GameUI ui = BuildUI(input);
            manager.ui = ui;
            AddStarEffects();

            EnsureEventSystem();

            EditorUtility.SetDirty(manager);
            EditorSceneManager.MarkSceneDirty(root.scene);
            Selection.activeGameObject = root;
            Debug.Log("[ExpandPuzzle] Scene built. Press Play.");
        }

        /// <summary>Clean post-processing: no tonemapping or vignette, just a touch of contrast.</summary>
        static void BuildVolume()
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Game")) AssetDatabase.CreateFolder("Assets", "_Game");
            if (!AssetDatabase.IsValidFolder(SettingsDir)) AssetDatabase.CreateFolder("Assets/_Game", "Settings");

            string path = SettingsDir + "/GameVolume.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);

                var tone = AddOverride<Tonemapping>(profile);
                tone.mode.Override(TonemappingMode.None);

                var color = AddOverride<ColorAdjustments>(profile);
                color.contrast.Override(6f);
                color.saturation.Override(8f);

                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
            }

            Volume volume = null;
            foreach (var v in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None))
                if (v.isGlobal) { volume = v; break; }
            if (volume == null)
            {
                var go = new GameObject("Global Volume");
                volume = go.AddComponent<Volume>();
                volume.isGlobal = true;
            }
            volume.sharedProfile = profile;
            EditorUtility.SetDirty(volume);
        }

        static T AddOverride<T>(VolumeProfile profile) where T : VolumeComponent
        {
            T c = profile.Add<T>(true);
            c.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(c, profile);
            return c;
        }

        // ------------------------------------------------------------ effects

        const string FxRoot = "Assets/Epic Toon FX/Prefabs/";

        /// <summary>Fills the effect slots from Epic Toon FX. Missing prefabs just leave a slot empty.</summary>
        static void AssignEffects(GameFx fx)
        {
            fx.pumpPuff.prefab = Fx("Environment/Smoke/White/SmokeBurstWhiteSoft");
            fx.growPop.prefab = Fx("Combat/Brawling/Soft/Radial/SoftRadialPunchLight");
            fx.bump.prefab = Fx("Environment/Dust/DustDirtyPoofSoft");
            fx.targetHit.prefab = Fx("Interactive/Sparkle/SparkleSolo/SparkleSoloYellow");
            fx.forbiddenHit.prefab = Fx("Environment/Smoke/Dark/SmokeBurstDarkSoft");
            fx.winSparkle.prefab = Fx("Interactive/Stars/StarPoof");
            fx.winBurst.prefab = Fx("Interactive/Stars/StarExplosion");
            fx.winConfetti.prefab = Fx("Environment/Confetti/Blast/ConfettiBlastRainbow");
            fx.winSparkleNoStars.prefab = Fx("Interactive/Sparkle/SparkleSolo/SparkleSoloWhite");
            fx.winBurstNoStars.prefab = Fx("Interactive/Level Up/Nova/LevelupNovaYellow");
        }

        const string SoundRoot = "Assets/Casual Game Sounds U6/CasualGameSounds/DM-CGS-";

        /// <summary>
        /// Fills the empty sound slots from the "Casual Game Sounds U6" pack. The clips were picked by
        /// their length and pitch shape, not by ear - swap any of them on the Game Audio component.
        /// </summary>
        static void AssignSounds(GameAudio a)
        {
            if (a == null) return;
            Clip(a.move, 3);            // short soft single note
            Clip(a.bump, 22);           // low, dull
            Clip(a.pump, 7);            // quick upward slide
            Clip(a.grow, 21);           // tiny rising blip
            Clip(a.targetCovered, 34);  // one bright ding
            Clip(a.forbidden, 47);      // short falling phrase
            Clip(a.wrongColor, 16);     // very short falling phrase
            Clip(a.starLost, 30);       // falling, half a second
            Clip(a.winSettle, 27);      // small chime
            Clip(a.winJingle, 45);      // two seconds, climbing
            Clip(a.winStar, 37);        // short bright note
            Clip(a.levelStart, 14);     // quick low pop
            Clip(a.undo, 31);
            Clip(a.restart, 46);        // downward slide
            Clip(a.click, 40);          // tiny tick
            Clip(a.boosterSelect, 44);  // short bright blip
            Clip(a.boosterThrow, 19);   // airy, rising: a whoosh
            Clip(a.boosterPop, 39);     // noisy burst that falls away
            Clip(a.boosterClear, 28);   // rising, with some air in it
            Clip(a.boosterHint, 26);    // quick climbing notes
            Clip(a.boosterDeny, 41);    // short flat phrase
            Clip(a.boosterRefill, 15);  // quick sparkle
            AssignMusic(a);
            EditorUtility.SetDirty(a);
        }

        const string MusicRoot = "Assets/Free Ambient Instrumental Music/";
        static readonly string[] MusicTracks =
        {
            "3Piano/Piano", "3Piano/Piano1", "3Piano/Piano2",
            "Harp/Harp", "Harp/Harp1", "Harp/Harp2",
        };

        /// <summary>Fills an empty music list with calm piano and harp tracks, set to stream so they stay light.</summary>
        static void AssignMusic(GameAudio a)
        {
            if (a.music != null && a.music.Length > 0) return;   // keep what was chosen by hand
            var list = new System.Collections.Generic.List<AudioClip>();
            foreach (string t in MusicTracks)
            {
                string path = MusicRoot + t + ".wav";
                MakeStreaming(path);
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip != null) list.Add(clip);
                else Debug.LogWarning("[ExpandPuzzle] Music not found: " + path);
            }
            a.music = list.ToArray();
        }

        static void MakeStreaming(string path)
        {
            var imp = AssetImporter.GetAtPath(path) as AudioImporter;
            if (imp == null) return;
            var set = imp.defaultSampleSettings;
            if (set.loadType == AudioClipLoadType.Streaming && set.compressionFormat == AudioCompressionFormat.Vorbis) return;
            set.loadType = AudioClipLoadType.Streaming;
            set.compressionFormat = AudioCompressionFormat.Vorbis;
            set.quality = 0.5f;
            imp.defaultSampleSettings = set;
            imp.loadInBackground = true;
            imp.SaveAndReimport();
        }

        static void Clip(GameAudio.Sound slot, int number)
        {
            if (slot == null || slot.clip != null) return;   // keep what was chosen by hand
            string path = SoundRoot + number.ToString("00") + ".wav";
            slot.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (slot.clip == null) Debug.LogWarning("[ExpandPuzzle] Sound not found: " + path);
        }

        /// <summary>Adds the Game Audio component to the game in the open scene and fills its empty slots.</summary>
        [MenuItem("Tools/Expand Puzzle/Add Sounds")]
        public static void AddSounds()
        {
            var manager = Object.FindFirstObjectByType<GameManager>();
            if (manager == null)
            {
                Debug.LogWarning("[ExpandPuzzle] Sounds: no game in this scene.");
                return;
            }
            GameAudio a = manager.GetComponent<GameAudio>();
            if (a == null) a = manager.gameObject.AddComponent<GameAudio>();
            AssignSounds(a);
            manager.sound = a;
            EditorUtility.SetDirty(manager);
            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
        }

        static GameObject Fx(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FxRoot + path + ".prefab");
            if (prefab == null) Debug.LogWarning("[ExpandPuzzle] Effect not found: " + FxRoot + path);
            return prefab;
        }

        static void DestroyRoot(string name)
        {
            var scene = EditorSceneManager.GetActiveScene();
            foreach (var go in scene.GetRootGameObjects())
                if (go.name == name) Object.DestroyImmediate(go);
        }

        // ------------------------------------------------------------ materials

        static GameTheme BuildTheme()
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Game")) AssetDatabase.CreateFolder("Assets", "_Game");
            if (!AssetDatabase.IsValidFolder(MaterialDir)) AssetDatabase.CreateFolder("Assets/_Game", "Materials");

            var t = new GameTheme();
            t.boardSurface = Mat(MaterialFactory.Slot.BoardSurface);
            t.boardPit = Mat(MaterialFactory.Slot.BoardPit);
            t.floorPad = Mat(MaterialFactory.Slot.FloorPad);
            t.target = Mat(MaterialFactory.Slot.Target);
            t.targetMark = Mat(MaterialFactory.Slot.TargetMark);
            t.forbidden = Mat(MaterialFactory.Slot.Forbidden);
            t.forbiddenMark = Mat(MaterialFactory.Slot.ForbiddenMark);
            t.blob = Mat(MaterialFactory.Slot.Blob);
            t.eye = Mat(MaterialFactory.Slot.Eye);
            t.eyeShine = Mat(MaterialFactory.Slot.EyeShine);
            t.pumpBody = Mat(MaterialFactory.Slot.PumpBody);
            t.pumpNozzle = Mat(MaterialFactory.Slot.PumpNozzle);
            t.pumpKnob = Mat(MaterialFactory.Slot.PumpKnob);
            t.badgeLight = Mat(MaterialFactory.Slot.BadgeLight);
            t.badgeDark = Mat(MaterialFactory.Slot.BadgeDark);
            t.glyphGood = Mat(MaterialFactory.Slot.GlyphGood);
            t.glyphBad = Mat(MaterialFactory.Slot.GlyphBad);
            AssetDatabase.SaveAssets();
            return t;
        }

        /// <summary>Puts every game material back to its default look (overwrites inspector tweaks).</summary>
        [MenuItem("Tools/Expand Puzzle/Reset Materials To Defaults")]
        public static void ResetMaterials()
        {
            foreach (MaterialFactory.Slot slot in System.Enum.GetValues(typeof(MaterialFactory.Slot)))
            {
                string path = MaterialDir + "/" + MaterialFactory.AssetName(slot) + ".mat";
                var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (existing == null) continue;
                Material fresh = MaterialFactory.Create(slot);
                existing.shader = fresh.shader;
                existing.CopyPropertiesFromMaterial(fresh);
                existing.shaderKeywords = fresh.shaderKeywords;
                EditorUtility.SetDirty(existing);
                Object.DestroyImmediate(fresh);
            }
            AssetDatabase.SaveAssets();
        }

        static Material Mat(MaterialFactory.Slot slot)
        {
            string path = MaterialDir + "/" + MaterialFactory.AssetName(slot) + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            Material m = MaterialFactory.Create(slot);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        // ------------------------------------------------------------ HUD

        static readonly Color Ink = new Color(0.22f, 0.36f, 0.28f, 1f);
        static Font s_Font;

        static GameUI BuildUI(GameInput input)
        {
            s_Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Sprite circle = UiSpriteGen.Circle();
            Sprite rounded = UiSpriteGen.Rounded();
            Sprite star = UiSpriteGen.Star();

            var canvasGo = new GameObject(UiRootName, typeof(RectTransform));
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            var ui = canvasGo.AddComponent<GameUI>();
            ui.font = s_Font;
            ui.pickerButtonSprite = circle;
            Transform root = canvasGo.transform;

            // Joystick first, so everything drawn after it stays clickable.
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(JoystickPrefab);
            if (prefab != null)
            {
                var joyGo = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
                var rt = joyGo.GetComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = new Vector2(0f, -230f);
                input.joystick = joyGo.GetComponent<Joystick>();
                foreach (var img in joyGo.GetComponentsInChildren<Image>(true))
                {
                    if (img.gameObject == joyGo) continue;
                    img.color = img.name == "Handle" ? new Color(1f, 1f, 1f, 0.95f) : new Color(1f, 1f, 1f, 0.5f);
                    img.raycastTarget = false;
                }
            }
            else
            {
                Debug.LogWarning("[ExpandPuzzle] Joystick prefab not found at " + JoystickPrefab + " - keyboard only.");
            }

            // ---- HUD lives inside the safe area (clear of notches and camera cut-outs)
            var safeGo = new GameObject("Safe Area", typeof(RectTransform));
            safeGo.transform.SetParent(root, false);
            Stretch(safeGo.GetComponent<RectTransform>());
            safeGo.AddComponent<SafeAreaFitter>();
            Transform hud = safeGo.transform;

            // ---- top bar
            ui.levelLabel = Label(hud, "Level Label", "LEVEL 1", 54, FontStyle.Bold, new Vector2(0.5f, 1f), new Vector2(0f, -44f), new Vector2(420f, 72f));
            ui.levelLabel.raycastTarget = true;   // tap to open the debug level picker
            ui.levelButton = ui.levelLabel.gameObject.AddComponent<Button>();
            ui.levelButton.targetGraphic = ui.levelLabel;
            ui.levelButton.transition = Selectable.Transition.None;

            ui.hudStars = new Image[3];
            for (int i = 0; i < 3; i++)
                ui.hudStars[i] = Icon(hud, "Star " + (i + 1), star, new Vector2(0.5f, 1f), new Vector2((i - 1) * 58f, -146f), 50f, new Color(1f, 0.78f, 0.18f, 1f));

            ui.movesLabel = Label(hud, "Moves Label", "0 / 7", 30, FontStyle.Bold, new Vector2(0.5f, 1f), new Vector2(0f, -176f), new Vector2(420f, 40f));
            ui.movesLabel.color = new Color(Ink.r, Ink.g, Ink.b, 0.65f);

            // Undo / restart sit a little below the top edge so a thumb reaches them.
            ui.undoButton = RoundButton(hud, "Undo Button", circle, UiSpriteGen.IconUndo(), new Vector2(0f, 1f), new Vector2(48f, -190f));
            ui.restartButton = RoundButton(hud, "Restart Button", circle, UiSpriteGen.IconRestart(), new Vector2(1f, 1f), new Vector2(-48f, -190f));

            // ---- win card
            var card = Panel(hud, "Win Card", rounded, new Vector2(0.5f, 0.5f), new Vector2(640f, 340f), new Color(1f, 1f, 1f, 0.97f));
            card.GetComponent<Image>().raycastTarget = false;
            ui.winBanner = card;
            ui.winLabel = Label(card, "Title", "Nice!", 62, FontStyle.Bold, new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(600f, 80f));
            ui.winLabel.color = new Color(1f, 0.38f, 0.33f, 1f);
            ui.winStars = new Image[3];
            for (int i = 0; i < 3; i++)
                ui.winStars[i] = Icon(card, "Star " + (i + 1), star, new Vector2(0.5f, 0.5f), new Vector2((i - 1) * 150f, i == 1 ? -6f : -24f), i == 1 ? 150f : 124f, new Color(1f, 0.78f, 0.18f, 1f));
            ui.winMovesLabel = Label(card, "Moves", "7 moves", 32, FontStyle.Bold, new Vector2(0.5f, 0f), new Vector2(0f, 34f), new Vector2(600f, 44f));
            ui.winMovesLabel.rectTransform.pivot = new Vector2(0.5f, 0f);
            ui.winMovesLabel.color = new Color(0.35f, 0.4f, 0.45f, 0.8f);
            card.gameObject.SetActive(false);

            // ---- level overview (buttons, stars and page tabs are created at runtime by GameUI.BuildPicker)
            var overlay = new GameObject("Level Picker", typeof(RectTransform));
            overlay.transform.SetParent(root, false);
            Stretch(overlay.GetComponent<RectTransform>());
            var dim = overlay.AddComponent<Image>();
            dim.color = new Color(0.08f, 0.12f, 0.1f, 0.5f);
            ui.pickerCloseButton = overlay.AddComponent<Button>();
            ui.pickerCloseButton.targetGraphic = dim;
            ui.pickerCloseButton.transition = Selectable.Transition.None;
            ui.pickerRoot = overlay;

            var pickerPanel = Panel(overlay.transform, "Panel", rounded, new Vector2(0.5f, 0.5f), new Vector2(900f, 400f), Color.white);
            var vlg = pickerPanel.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(50, 50, 40, 50);
            vlg.spacing = 30f;
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            var fitter = pickerPanel.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            Text pickerTitle = Label(pickerPanel, "Title", "SELECT LEVEL", 34, FontStyle.Bold, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(800f, 50f));
            pickerTitle.color = new Color(0.3f, 0.36f, 0.4f, 1f);
            pickerTitle.gameObject.AddComponent<LayoutElement>().preferredHeight = 50f;

            var gridGo = new GameObject("Grid", typeof(RectTransform));
            gridGo.transform.SetParent(pickerPanel, false);
            var grid = gridGo.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(140f, 140f);
            grid.spacing = new Vector2(24f, 24f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 5;
            grid.childAlignment = TextAnchor.UpperCenter;
            ui.pickerGrid = gridGo.GetComponent<RectTransform>();
            overlay.SetActive(false);

            // ---- full-screen wipe between levels (drawn last, never blocks input)
            var fade = new GameObject("Fader", typeof(RectTransform));
            fade.transform.SetParent(root, false);
            Stretch(fade.GetComponent<RectTransform>());
            var fimg = fade.AddComponent<Image>();
            fimg.color = new Color(1f, 1f, 1f, 0f);
            fimg.raycastTarget = false;
            fimg.enabled = false;
            ui.fader = fimg;

            return ui;
        }

        /// <summary>
        /// Particles cannot be drawn on top of an overlay canvas, so the win card moves to its own canvas
        /// that is drawn by a small extra camera (stacked on the main one, no post-processing). The
        /// three-star effect from Epic Victory Effects is then created on the card. Safe to run again.
        /// </summary>
        [MenuItem("Tools/Expand Puzzle/Add Star Effects To Win Card")]
        public static void AddStarEffects()
        {
            var ui = Object.FindFirstObjectByType<GameUI>();
            Camera main = Camera.main;
            if (ui == null || ui.winBanner == null || main == null)
            {
                Debug.LogWarning("[ExpandPuzzle] Star effects: no game UI / win card / main camera in this scene.");
                return;
            }

            // ---- camera that only draws the win card and its particles
            GameObject camGo = GameObject.Find(WinCameraName);
            if (camGo == null) camGo = new GameObject(WinCameraName);
            camGo.transform.SetPositionAndRotation(new Vector3(0f, -500f, 0f), Quaternion.identity);
            Camera cam = camGo.GetComponent<Camera>();
            if (cam == null) cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 5f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 50f;
            cam.cullingMask = 1 << UiLayer;
            cam.clearFlags = CameraClearFlags.Depth;
            var camData = cam.GetUniversalAdditionalCameraData();
            camData.renderType = CameraRenderType.Overlay;
            camData.renderPostProcessing = false;

            var mainData = main.GetUniversalAdditionalCameraData();
            if (!mainData.cameraStack.Contains(cam)) mainData.cameraStack.Add(cam);
            main.cullingMask &= ~(1 << UiLayer);

            // ---- canvas for the card
            GameObject canvasGo = GameObject.Find(WinCanvasName);
            if (canvasGo == null) canvasGo = new GameObject(WinCanvasName, typeof(RectTransform));
            Canvas canvas = canvasGo.GetComponent<Canvas>();
            if (canvas == null) canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 10f;
            canvas.sortingOrder = 0;
            CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
            if (scaler == null) scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            RectTransform card = ui.winBanner;
            bool wasActive = card.gameObject.activeSelf;
            card.SetParent(canvasGo.transform, false);
            card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
            card.anchoredPosition = Vector2.zero;
            card.localScale = Vector3.one;
            card.gameObject.SetActive(wasActive);

            // ---- where the effect goes
            Transform anchor = card.Find("Star Effects");
            if (anchor == null)
            {
                var anchorGo = new GameObject("Star Effects", typeof(RectTransform));
                anchorGo.transform.SetParent(card, false);
                anchor = anchorGo.transform;
            }
            var anchorRt = (RectTransform)anchor;
            anchorRt.anchorMin = anchorRt.anchorMax = new Vector2(0.5f, 0.5f);
            anchorRt.anchoredPosition = new Vector2(0f, 12f);
            anchorRt.sizeDelta = Vector2.zero;
            ui.starEffectAnchor = anchorRt;

            if (ui.starEffectPrefab == null)
            {
                ui.starEffectPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DefaultStarEffect);
                if (ui.starEffectPrefab == null)
                    Debug.LogWarning("[ExpandPuzzle] Star effect not found: " + DefaultStarEffect + " - the win card keeps its plain stars.");
            }

            foreach (var t in canvasGo.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = UiLayer;

            EditorUtility.SetDirty(ui);
            EditorUtility.SetDirty(main);
            EditorSceneManager.MarkSceneDirty(canvasGo.scene);
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        static RectTransform Panel(Transform parent, string name, Sprite sprite, Vector2 anchor, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.type = Image.Type.Sliced;
            img.color = color;
            var shadow = go.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.16f);
            shadow.effectDistance = new Vector2(0f, -8f);
            return rt;
        }

        static Image Icon(Transform parent, string name, Sprite sprite, Vector2 anchor, Vector2 pos, float size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(size, size);
            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        static Text Label(Transform parent, string name, string text, int size, FontStyle style,
            Vector2 anchor, Vector2 pos, Vector2 sizeDelta)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, anchor.y > 0.9f ? 1f : 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = sizeDelta;
            var t = go.AddComponent<Text>();
            t.font = s_Font;
            t.text = text;
            t.fontSize = size;
            t.fontStyle = style;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = Ink;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        static Button RoundButton(Transform parent, string name, Sprite circle, Sprite icon, Vector2 anchor, Vector2 pos)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(124f, 124f);
            var img = go.AddComponent<Image>();
            img.sprite = circle;
            img.color = new Color(1f, 1f, 1f, 0.96f);
            var shadow = go.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.16f);
            shadow.effectDistance = new Vector2(0f, -7f);
            var button = go.AddComponent<Button>();
            button.targetGraphic = img;
            Icon(go.transform, "Icon", icon, new Vector2(0.5f, 0.5f), Vector2.zero, 72f, Ink);
            return button;
        }

        static void EnsureEventSystem()
        {
            var es = Object.FindFirstObjectByType<EventSystem>();
            if (es == null)
            {
                var go = new GameObject("EventSystem");
                es = go.AddComponent<EventSystem>();
            }
            var legacy = es.GetComponent<StandaloneInputModule>();
            if (legacy != null) Object.DestroyImmediate(legacy);
            if (es.GetComponent<InputSystemUIInputModule>() == null) es.gameObject.AddComponent<InputSystemUIInputModule>();
        }
    }
}
