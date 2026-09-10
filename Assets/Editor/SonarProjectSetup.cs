#if UNITY_EDITOR
using System.IO;
using SonarTask.Bootstrap;
using SonarTask.UI;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SonarTask.EditorTools
{
    [InitializeOnLoad]
    public static class SonarProjectSetup
    {
        static readonly string[] Scenes =
        {
            "Boot", "LoginWeb", "Startup", "ExperimentSelection",
            "Instructions", "SettingsWeb", "SonarTask"
        };

        static SonarProjectSetup() => EditorApplication.delayCall += Ensure;

        [MenuItem("SONAR/Initialize Project")]
        public static void Ensure() => EnsureInternal(false);

        [MenuItem("SONAR/Rebuild Editable Scene Layouts")]
        public static void RebuildLayouts()
        {
            if (!EditorUtility.DisplayDialog("Rebuild SONAR scene layouts",
                    "This replaces the generated SceneUIRoot hierarchy in each SONAR scene. Any GUI edits made inside those generated roots will be lost.",
                    "Rebuild", "Cancel")) return;
            EnsureInternal(true);
        }

        [MenuItem("SONAR/Apply Branding")]
        public static void ApplyBranding()
        {
            ConfigureBranding();
            AssetDatabase.SaveAssets();
            Debug.Log("SONAR branding applied to splash screen and player icons.");
        }

        static void EnsureInternal(bool rebuildLayouts)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!CanTemporarilyReplaceOpenScenes())
            {
                Debug.LogWarning("SONAR project initialization was skipped because an open scene has unsaved changes. Save the scene, then choose SONAR > Initialize Project.");
                return;
            }

            Directory.CreateDirectory("Assets/Scenes");
            var originalSetup = EditorSceneManager.GetSceneManagerSetup();
            bool canRestore = originalSetup.Length > 0;
            foreach (var x in originalSetup) if (string.IsNullOrEmpty(x.path)) { canRestore = false; break; }

            foreach (var n in Scenes)
            {
                string path = $"Assets/Scenes/{n}.unity";
                Scene scene;
                if (File.Exists(path)) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                else scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

                bool changed = false;
                if (n == "Boot")
                {
                    if (!FindInScene<SonarBootstrap>(scene))
                    {
                        new GameObject("SonarBootstrap").AddComponent<SonarBootstrap>();
                        changed = true;
                    }
                }
                else
                {
                    var canvas = FindInScene<Canvas>(scene);
                    if (!canvas)
                    {
                        canvas = UIFactory.Canvas();
                        canvas.gameObject.name = "Canvas";
                        changed = true;
                    }

                    var scaler = canvas.GetComponent<CanvasScaler>();
                    if (scaler)
                    {
                        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                        scaler.referenceResolution = new Vector2(1920, 1080);
                        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                        scaler.matchWidthOrHeight = .5f;
                    }

                    var root = canvas.transform.Find(SceneLayoutBuilder.RootName);
                    if (rebuildLayouts || !root)
                    {
                        SceneLayoutBuilder.Rebuild(n, canvas.transform);
                        changed = true;
                    }
                }

                if (!File.Exists(path) || changed || scene.isDirty)
                    EditorSceneManager.SaveScene(scene, path);
            }

            if (canRestore) EditorSceneManager.RestoreSceneManagerSetup(originalSetup);
            else EditorSceneManager.OpenScene("Assets/Scenes/Boot.unity", OpenSceneMode.Single);

            var build = new EditorBuildSettingsScene[Scenes.Length];
            for (int i = 0; i < Scenes.Length; i++) build[i] = new EditorBuildSettingsScene($"Assets/Scenes/{Scenes[i]}.unity", true);
            EditorBuildSettings.scenes = build;

            PlayerSettings.productName = "SONAR Simulator Task";
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.companyName = "SONAR Research";
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = false;
            PlayerSettings.WebGL.template = "PROJECT:SONARResponsive";
            ConfigureBranding();
            EnsureInputSystemOnly();
            AssetDatabase.SaveAssets();
            Debug.Log("SONAR project scenes, editable GUI shells, and build settings are ready.");
        }

        static T FindInScene<T>(Scene scene) where T : Component
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var found = root.GetComponentInChildren<T>(true);
                if (found) return found;
            }
            return null;
        }

        static bool CanTemporarilyReplaceOpenScenes()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++) if (SceneManager.GetSceneAt(i).isDirty) return false;
            return true;
        }

        static void ConfigureBranding()
        {
            const string iconPath = "Assets/Branding/SonarIcon.png";
            const string splashPath = "Assets/Branding/SonarSplash.png";

            ConfigureTextureImporter(iconPath, false);
            ConfigureTextureImporter(splashPath, true);

            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(iconPath);
            var splash = AssetDatabase.LoadAssetAtPath<Sprite>(splashPath);

            if (icon)
            {
                SetPlatformIcons(NamedBuildTarget.Standalone, icon);
                SetPlatformIcons(NamedBuildTarget.WebGL, icon);
            }
            else Debug.LogWarning("SONAR branding icon could not be loaded: " + iconPath);

            if (splash)
            {
                PlayerSettings.SplashScreen.show = true;
                PlayerSettings.SplashScreen.showUnityLogo = false;
                PlayerSettings.SplashScreen.background = splash;
                PlayerSettings.SplashScreen.backgroundPortrait = splash;
                PlayerSettings.SplashScreen.blurBackgroundImage = false;
                PlayerSettings.SplashScreen.overlayOpacity = 0f;
                PlayerSettings.SplashScreen.backgroundColor = Color.black;
            }
            else Debug.LogWarning("SONAR splash image could not be loaded: " + splashPath);
        }

        static void ConfigureTextureImporter(string assetPath, bool asSprite)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (!importer) return;

            bool changed = false;
            var desiredType = asSprite ? TextureImporterType.Sprite : TextureImporterType.Default;
            if (importer.textureType != desiredType) { importer.textureType = desiredType; changed = true; }
            if (asSprite && importer.spriteImportMode != SpriteImportMode.Single) { importer.spriteImportMode = SpriteImportMode.Single; changed = true; }
            if (!importer.alphaIsTransparency) { importer.alphaIsTransparency = true; changed = true; }
            if (importer.mipmapEnabled) { importer.mipmapEnabled = false; changed = true; }
            if (importer.wrapMode != TextureWrapMode.Clamp) { importer.wrapMode = TextureWrapMode.Clamp; changed = true; }
            if (importer.maxTextureSize < 2048) { importer.maxTextureSize = 2048; changed = true; }
            if (changed) importer.SaveAndReimport();
        }

        static void SetPlatformIcons(NamedBuildTarget target, Texture2D icon)
        {
            try
            {
                var sizes = PlayerSettings.GetIconSizes(target, IconKind.Application);
                if (sizes == null || sizes.Length == 0) return;
                var icons = new Texture2D[sizes.Length];
                for (int i = 0; i < icons.Length; i++) icons[i] = icon;
                PlayerSettings.SetIcons(target, icons, IconKind.Application);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"SONAR could not assign icons for {target.TargetName}: {ex.Message}");
            }
        }

        static void EnsureInputSystemOnly()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (assets == null || assets.Length == 0) return;
            var serialized = new SerializedObject(assets[0]);
            var activeInputHandler = serialized.FindProperty("activeInputHandler");
            if (activeInputHandler == null || activeInputHandler.intValue == 1) return;
            activeInputHandler.intValue = 1;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log("SONAR: Active Input Handling set to Input System Package (New). Unity may require one editor restart.");
        }
    }
}
#endif
