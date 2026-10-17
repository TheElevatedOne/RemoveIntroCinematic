using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using RoR2;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.SceneManagement;

namespace RemoveIntroCinematic
{
    [BepInDependency(RiskOfOptionsCompat.PluginGUID, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public class RemoveIntroCinematicPlugin : BaseUnityPlugin
    {
        public const string PluginGUID = "com.elevated.RemoveIntroCinematic";
        public const string PluginName = "RemoveIntroCinematic";
        public const string PluginVersion = "1.1.0";

        // Addressable keys the game hardcodes in RoR2Application / SplashScreenController.
        private const string SplashSceneGuid = "fe13e00695117284fb0d1b25422a0529";
        private const string IntroSceneGuid = "330d792ae1727574e969e68ce8e966d2";

        // Scene names used as a fallback in case a game update changes the GUIDs above.
        private const string SplashSceneName = "splash";
        private const string IntroSceneName = "intro";
        private const string LoadingSceneName = "loadingbasic";

        internal static ManualLogSource Log;

        private static ConfigEntry<bool> skipSplash;
        private static ConfigEntry<bool> skipIntro;
        private static ConfigEntry<bool> skipIntroMusic;

        private static readonly Dictionary<string, string> sceneNameByKey = new Dictionary<string, string>();
        private static FieldInfo haveLoadedSplashField;

        private static bool titleRequested;
        private static bool introPreloadStarted;
        private static bool musicStartPending;

        private static bool ShouldSkipIntro => skipIntro.Value || IntroCutsceneController.shouldSkip;

        public void Awake()
        {
            Log = Logger;

            skipSplash = Config.Bind("General", "SkipSplashScreen", true,
                "Skip the developer logo splash screens shown at startup.");
            skipIntro = Config.Bind("General", "SkipIntroCinematic", true,
                "Skip the intro cutscene and go straight to the main menu once loading is done.");
            skipIntroMusic = Config.Bind("General", "SkipIntroMusic", true,
                "Don't play the intro music during startup. Music starts normally once the main menu is reached.");

            if (BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(RiskOfOptionsCompat.PluginGUID))
            {
                RiskOfOptionsCompat.Init(System.IO.Path.GetDirectoryName(Info.Location), skipSplash, skipIntro, skipIntroMusic);
            }

            haveLoadedSplashField = typeof(FadeToBlackManager).GetField("haveLoadedSplash", BindingFlags.Static | BindingFlags.NonPublic);
            if (haveLoadedSplashField == null)
            {
                Log.LogWarning("FadeToBlackManager.haveLoadedSplash not found; screen fades may not work if the splash is skipped.");
            }

            musicStartPending = skipIntroMusic.Value;

            On.LoadHelper.LoadSceneAdditivelyAsync += LoadHelper_LoadSceneAdditivelyAsync;
            On.LoadHelper.StartLoadSceneAdditiveAsync += LoadHelper_StartLoadSceneAdditiveAsync;
            On.RoR2.SplashScreenController.Finish += SplashScreenController_Finish;
            On.RoR2.MusicController.StartIntroMusic += MusicController_StartIntroMusic;
            On.RoR2.MusicController.SceneManager_OnLoadNextScene += MusicController_SceneManager_OnLoadNextScene;
            On.RoR2.MusicController.SceneCatalog_OnSceneDefChanged += MusicController_SceneCatalog_OnSceneDefChanged;
        }

        public void OnDestroy()
        {
            On.LoadHelper.LoadSceneAdditivelyAsync -= LoadHelper_LoadSceneAdditivelyAsync;
            On.LoadHelper.StartLoadSceneAdditiveAsync -= LoadHelper_StartLoadSceneAdditiveAsync;
            On.RoR2.SplashScreenController.Finish -= SplashScreenController_Finish;
            On.RoR2.MusicController.StartIntroMusic -= MusicController_StartIntroMusic;
            On.RoR2.MusicController.SceneManager_OnLoadNextScene -= MusicController_SceneManager_OnLoadNextScene;
            On.RoR2.MusicController.SceneCatalog_OnSceneDefChanged -= MusicController_SceneCatalog_OnSceneDefChanged;
        }

        #region Splash / intro

        // RoR2Application loads the splash scene through this coroutine and waits for it to activate.
        private static IEnumerator LoadHelper_LoadSceneAdditivelyAsync(On.LoadHelper.orig_LoadSceneAdditivelyAsync orig, string sceneKey)
        {
            if (skipSplash.Value && RoR2Application.isLoading && IsScene(sceneKey, SplashSceneGuid, SplashSceneName))
            {
                return SkipSplash();
            }
            return orig(sceneKey);
        }

        // RoR2Application preloads the intro scene with activateOnLoad = false. If the intro is skipped
        // that load must never start: a pending scene activation would block every later scene load.
        private static AsyncOperationHandle LoadHelper_StartLoadSceneAdditiveAsync(On.LoadHelper.orig_StartLoadSceneAdditiveAsync orig, string sceneKey)
        {
            if (IsScene(sceneKey, IntroSceneGuid, IntroSceneName))
            {
                if (ShouldSkipIntro && RoR2Application.isLoading)
                {
                    Log.LogInfo("Skipping intro cinematic preload.");
                    return default;
                }
                introPreloadStarted = true;
            }
            return orig(sceneKey);
        }

        // Called when the splash finishes (or immediately with splash_skip). Vanilla changes scene right
        // away when intro_skip is set, even while the game is still loading, which breaks the title screen.
        private static void SplashScreenController_Finish(On.RoR2.SplashScreenController.orig_Finish orig, SplashScreenController self)
        {
            if (ShouldSkipIntro)
            {
                RequestTitle();
                return;
            }
            orig(self);
        }

        private static IEnumerator SkipSplash()
        {
            Log.LogInfo("Skipping splash screen.");

            // The splash scene normally hosts TriggerIntroMusicStart.
            if (!musicStartPending)
            {
                StartIntroMusicNow();
            }

            if (ShouldSkipIntro)
            {
                RequestTitle();
            }
            else
            {
                RoR2Application.instance.StartCoroutine(PlayIntroAfterPreload());
            }
            yield break;
        }

        private static IEnumerator PlayIntroAfterPreload()
        {
            // Reuse the game's own preload so the intro scene isn't loaded twice.
            while (!introPreloadStarted && !RoR2Application.loadFinished)
            {
                yield return null;
            }
            MarkSplashSeen();
            yield return LoadHelper.LoadSceneAdditivelyAsync(IntroSceneGuid);
        }

        private static void RequestTitle()
        {
            if (titleRequested)
            {
                return;
            }
            titleRequested = true;

            if (RoR2Application.loadFinished)
            {
                GoToTitle();
            }
            else
            {
                Log.LogInfo("Intro skipped; going to the main menu when loading finishes.");
                RoR2Application.onLoad += GoToTitle;
            }
        }

        // Runs from RoR2Application.onLoad: an exception here would abort the loading coroutine and
        // leave the game stuck at 99%, so never let one escape.
        private static void GoToTitle()
        {
            try
            {
                MarkSplashSeen();
                RoR2.Console.instance.SubmitCmd(null, "set_scene title");
            }
            catch (Exception e)
            {
                Log.LogError($"Failed to open the main menu: {e}");
            }
        }

        // FadeToBlackManager only enables screen fades after it has seen the splash scene unload.
        private static void MarkSplashSeen()
        {
            haveLoadedSplashField?.SetValue(null, true);
        }

        private static bool IsScene(string key, string guid, string sceneName)
        {
            if (key == guid)
            {
                return true;
            }
            return string.Equals(ResolveSceneName(key), sceneName, StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveSceneName(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }
            if (sceneNameByKey.TryGetValue(key, out var cached))
            {
                return cached;
            }

            string name = null;
            try
            {
                foreach (var locator in Addressables.ResourceLocators)
                {
                    if (locator.Locate(key, null, out IList<IResourceLocation> locations) && locations.Count > 0)
                    {
                        name = System.IO.Path.GetFileNameWithoutExtension(locations[0].InternalId);
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                Log.LogDebug($"Could not resolve addressable key {key}: {e.Message}");
            }

            sceneNameByKey[key] = name;
            return name;
        }

        #endregion

        #region Music

        private static void StartIntroMusicNow()
        {
            var musicController = MusicController.Instance;
            if (musicController)
            {
                musicController.StartIntroMusic();
            }
            else
            {
                // Make sure the music system still starts once a real scene loads.
                musicStartPending = true;
            }
        }

        private static void MusicController_StartIntroMusic(On.RoR2.MusicController.orig_StartIntroMusic orig, MusicController self)
        {
            if (musicStartPending)
            {
                Log.LogInfo("Skipping intro music.");
                return;
            }
            orig(self);
        }

        private static void MusicController_SceneManager_OnLoadNextScene(On.RoR2.MusicController.orig_SceneManager_OnLoadNextScene orig, MusicController self, Scene scene, LoadSceneMode mode)
        {
            // Vanilla switches to the intro track when the intro scene loads.
            if (musicStartPending && scene.name == IntroSceneName)
            {
                return;
            }
            orig(self, scene, mode);
        }

        // Vanilla starts the Wwise music system in StartIntroMusic. Since that was skipped, run it here
        // on the first real scene (normally the main menu) so the scene's own track gets picked right after.
        // Going through StartIntroMusic (instead of posting the event directly) keeps other mods' hooks on it
        // working, e.g. R2API.Sound starts custom music systems there.
        private static void MusicController_SceneCatalog_OnSceneDefChanged(On.RoR2.MusicController.orig_SceneCatalog_OnSceneDefChanged orig, MusicController self, SceneDef sceneDef)
        {
            if (musicStartPending && sceneDef && !IsBootScene(sceneDef.baseSceneName))
            {
                musicStartPending = false;
                Log.LogInfo($"Starting music system in scene '{sceneDef.baseSceneName}'.");
                try
                {
                    self.StartIntroMusic();
                }
                catch (Exception e)
                {
                    Log.LogError($"Failed to start the music system: {e}");
                }
            }
            orig(self, sceneDef);
        }

        private static bool IsBootScene(string sceneName)
        {
            return sceneName == SplashSceneName || sceneName == IntroSceneName || sceneName == LoadingSceneName;
        }

        #endregion
    }
}
