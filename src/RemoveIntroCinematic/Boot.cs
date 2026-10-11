using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;

namespace RemoveIntroCinematic
{
    /// <summary>
    /// Skips the splash and intro scenes. The game's music system is not blocked:
    /// stopping it, or swallowing Play_Music_System, silences every later stage.
    /// </summary>
    public static class Boot
    {
        // Addressable scene guids used since Seekers of the Storm. Scene names are
        // matched as well, so a guid change still silences a scene that actually loads.
        private const string IntroGuid = "330d792ae1727574e969e68ce8e966d2";
        private const string SplashGuid = "fe13e00695117284fb0d1b25422a0529";

        internal static ManualLogSource Log;
        internal static bool SkipSplash = true;
        internal static bool BootComplete;

        private static Harmony _harmony;
        private static bool _coreHooks;
        private static bool _videoHooks;
        private static bool _audioHooks;
        private static bool _musicHooks;
        private static bool _onLoadHooked;
        private static bool _flagsForced;
        private static bool _splashStandInDone;
        private static bool _splashFinished;
        private static bool _loadFinished;
        private static bool _sawLoading;
        private static bool _musicStarted;
        private static bool _applyingMenuTrack;
        private static int _musicAttempts;
        private static int _installAttempts;
        private static object _musicObject;
        private static int _titleAttempts;
        private static int _framesSinceTitleAttempt;
        private static int _titleFrames;

        private static readonly HashSet<string> _once = new HashSet<string>();
        private static readonly HashSet<string> _unloadAttempted = new HashSet<string>();
        private static int _tick;
        private static readonly Dictionary<string, Type> _typeCache = new Dictionary<string, Type>();
        private static readonly Dictionary<string, int> _typeMiss = new Dictionary<string, int>();

        internal static void Install()
        {
            _harmony ??= new Harmony(RemoveIntroCinematicPlugin.PluginGuid);
            if (!GameTypesReady())
                return;

            if (!_coreHooks)
                _coreHooks = InstallCoreHooks();
            if (!_videoHooks)
                _videoHooks = InstallVideoHooks();
            if (!_audioHooks)
                _audioHooks = InstallAudioHooks();
            if (!_musicHooks)
                _musicHooks = InstallMusicHooks();
            if (!_onLoadHooked)
                _onLoadHooked = SubscribeOnLoad();
            _flagsForced = ForceSkipFlags();
        }

        internal static void Tick()
        {
            if (BootComplete)
                return;

            _tick++;
            try
            {
                // A failed hook install scans every assembly. Do that on a gap, not
                // every frame, or the loading bar sits at a few frames per second.
                if (!_coreHooks || !_videoHooks || !_audioHooks || !_musicHooks || !_onLoadHooked || !_flagsForced)
                {
                    if (_installAttempts % 30 == 0)
                        Install();
                    _installAttempts++;
                }

                var loading = ReadIsLoading();
                if (loading == true)
                    _sawLoading = true;
                else if (loading == false && _sawLoading)
                    _loadFinished = true;

                // shouldSkip is a plain flag and the static constructor can reset it.
                if (!_flagsForced)
                    _flagsForced = ForceSkipFlags();
                else
                    SetStaticBool("IntroCutsceneController", "shouldSkip", true);

                if (_loadFinished && (SkipSplash || _splashFinished || !IsSceneLoaded("splash")))
                    RequestTitle();

                if (!IsTitleScene())
                    return;

                if (!_musicStarted)
                    BeginMenuMusic();
                if (!_musicStarted)
                    return;

                // Keep replacing a late intro cue until boot ends. Blocking turns off
                // at BootComplete, which is after the menu theme has had time to stick.
                CorrectIntroTrackLeak();
                _titleFrames++;
                if (_titleFrames < 45)
                    return;

                if (!MusicLivesIn("intro"))
                    TryUnload("intro");
                if (SkipSplash && !MusicLivesIn("splash"))
                    TryUnload("splash");
                BootComplete = true;
                LogInfo("Main menu is up. Intro cinematic will not play again this session.");
            }
            catch (Exception ex)
            {
                WarnOnce("Boot tick failed", ex);
            }
        }

        private static bool GameTypesReady()
        {
            return AccessTools.TypeByName("RoR2.RoR2Application") != null;
        }

        private static bool InstallCoreHooks()
        {
            var patched = 0;
            foreach (var type in FindTypesNamed("LoadHelper"))
            {
                foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (!ShouldPatchLoadMethod(method))
                        continue;
                    try
                    {
                        MethodInfo prefix;
                        if (method.ReturnType == typeof(void))
                        {
                            prefix = typeof(Boot).GetMethod(nameof(BlockLoadVoid), BindingFlags.Public | BindingFlags.Static);
                        }
                        else
                        {
                            var openLoad = typeof(Boot).GetMethod(nameof(BlockLoad), BindingFlags.Public | BindingFlags.Static);
                            if (openLoad == null)
                                continue;
                            prefix = openLoad.MakeGenericMethod(method.ReturnType);
                        }

                        _harmony.Patch(method, new HarmonyMethod(prefix) { priority = Priority.First });
                        patched++;
                        LogInfo("Blocking cinematic loads through " + type.FullName + "." + method.Name);
                    }
                    catch (Exception ex)
                    {
                        WarnOnce("Could not patch " + method.Name, ex);
                    }
                }
            }

            foreach (var type in FindTypesNamed("SplashScreenController"))
            {
                var finish = type.GetMethod("Finish", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                if (finish == null)
                    continue;
                try
                {
                    _harmony.Patch(finish, new HarmonyMethod(typeof(Boot), nameof(BlockSplashFinish)) { priority = Priority.First });
                    patched++;
                    LogInfo("Intercepting " + type.FullName + ".Finish");
                }
                catch (Exception ex)
                {
                    WarnOnce("Could not patch SplashScreenController.Finish", ex);
                }
            }

            PatchSceneLoaded();
            return patched > 0 || FindTypesNamed("RoR2Application").Count > 0;
        }

        private static void PatchSceneLoaded()
        {
            var sceneManager = AccessTools.TypeByName("UnityEngine.SceneManagement.SceneManager");
            var method = sceneManager?.GetMethod("Internal_SceneLoaded", BindingFlags.NonPublic | BindingFlags.Static);
            if (method == null)
                return;
            try
            {
                _harmony.Patch(method, postfix: new HarmonyMethod(typeof(Boot), nameof(AfterSceneLoaded)));
            }
            catch (Exception ex)
            {
                WarnOnce("Could not watch scene loads", ex);
            }
        }

        private static bool InstallVideoHooks()
        {
            var videoPlayer = AccessTools.TypeByName("UnityEngine.Video.VideoPlayer");
            if (videoPlayer == null)
                return false;

            var count = 0;
            foreach (var method in videoPlayer.GetMethods(BindingFlags.Instance | BindingFlags.Public))
            {
                if (method.IsSpecialName || method.DeclaringType == typeof(object))
                    continue;
                if (method.Name != "Play" && method.Name != "Prepare")
                    continue;
                try
                {
                    _harmony.Patch(method, new HarmonyMethod(typeof(Boot), nameof(BlockVideoPlay)) { priority = Priority.First });
                    count++;
                }
                catch (Exception ex)
                {
                    WarnOnce("Could not patch VideoPlayer." + method.Name, ex);
                }
            }

            LogInfo(count > 0
                ? "Intro video playback is blocked."
                : "VideoPlayer.Play was not found; an intro video will be stopped if its scene loads.");
            return true;
        }

        private static bool InstallAudioHooks()
        {
            var audio = AccessTools.TypeByName("UnityEngine.AudioSource");
            if (audio == null)
                return false;

            foreach (var name in new[] { "Play", "PlayDelayed", "PlayOneShot" })
            {
                foreach (var method in audio.GetMethods(BindingFlags.Instance | BindingFlags.Public))
                {
                    if (method.Name != name)
                        continue;
                    try
                    {
                        _harmony.Patch(method, new HarmonyMethod(typeof(Boot), nameof(BlockIntroAudio)) { priority = Priority.First });
                    }
                    catch (Exception ex)
                    {
                        WarnOnce("Could not patch AudioSource." + name, ex);
                    }
                }
            }

            return true;
        }

        private static bool InstallMusicHooks()
        {
            // Do not patch StartIntroMusic or AkSoundEngine.PostEvent. Those calls boot
            // the whole Wwise music system. Blocking them leaves every stage silent.
            var track = AccessTools.TypeByName("RoR2.MusicTrackDef");
            if (track == null)
                return false;

            var play = track.GetMethod("Play", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            if (play == null)
                return true;

            try
            {
                _harmony.Patch(play, new HarmonyMethod(typeof(Boot), nameof(BlockIntroTrack)) { priority = Priority.First });
                LogInfo("Intro music tracks are skipped. The music system still starts.");
            }
            catch (Exception ex)
            {
                WarnOnce("Could not patch MusicTrackDef.Play", ex);
            }

            return true;
        }

        private static bool SubscribeOnLoad()
        {
            var app = AccessTools.TypeByName("RoR2.RoR2Application");
            if (app == null)
                return false;

            try
            {
                var field = app.GetField("onLoad", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (field != null && typeof(Delegate).IsAssignableFrom(field.FieldType))
                {
                    var handler = Delegate.CreateDelegate(field.FieldType, typeof(Boot).GetMethod(nameof(OnGameLoaded), BindingFlags.NonPublic | BindingFlags.Static));
                    field.SetValue(null, Delegate.Combine((Delegate)field.GetValue(null), handler));
                    LogInfo("Will open the main menu when loading finishes.");
                    return true;
                }

                var evt = app.GetEvent("onLoad", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (evt != null)
                {
                    var handler = Delegate.CreateDelegate(evt.EventHandlerType, typeof(Boot).GetMethod(nameof(OnGameLoaded), BindingFlags.NonPublic | BindingFlags.Static));
                    evt.GetAddMethod(true).Invoke(null, new object[] { handler });
                    LogInfo("Will open the main menu when loading finishes.");
                    return true;
                }
            }
            catch (Exception ex)
            {
                WarnOnce("Could not subscribe to RoR2Application.onLoad. The menu transition uses the loading flag instead", ex);
                return true;
            }

            Log?.LogWarning("RoR2Application.onLoad was not found. The menu transition uses the loading flag instead.");
            return true;
        }

        private static void OnGameLoaded()
        {
            _loadFinished = true;
            LogInfo("Game load finished.");
            if (SkipSplash || _splashFinished || !IsSceneLoaded("splash"))
                RequestTitle();
        }

        public static bool BlockLoadVoid(object[] __args)
        {
            var key = CinematicKey(__args);
            if (key == null)
                return true;
            OnBlocked(key);
            return false;
        }

        public static bool BlockLoad<T>(object[] __args, ref T __result)
        {
            var key = CinematicKey(__args);
            if (key == null)
                return true;
            OnBlocked(key);
            // Only the IEnumerator interface gets an empty iterator. A concrete
            // iterator type would not accept that cast, and default is null for classes.
            if (typeof(T) == typeof(IEnumerator))
                __result = (T)(object)Empty();
            else
                __result = default;
            return false;
        }

        public static bool BlockSplashFinish()
        {
            if (BootComplete)
                return true;
            var loading = ReadIsLoading();
            if (loading == false && !_sawLoading)
                return true;
            _splashFinished = true;
            LogOnce("Splash will not continue into the intro cinematic.");
            return false;
        }

        public static void AfterSceneLoaded(object[] __args)
        {
            if (BootComplete || __args == null || __args.Length == 0)
                return;
            var name = SceneObjectName(__args[0]);
            if (IsRemovedScene(name))
                ForEachRoot(name, SilenceObjectTree);
            else if (string.Equals(name, "loadingbasic", StringComparison.OrdinalIgnoreCase))
                ForEachRoot(name, SilenceVideosInTree);
        }

        public static bool BlockVideoPlay(object __instance)
        {
            // Only the cinematic. A loading-bar or gameplay video is left alone.
            if (IsRemovedScene(SceneNameOf(__instance)) || IsCinematicObject(__instance, false))
            {
                SilenceVideo(__instance);
                return false;
            }
            return true;
        }

        public static bool BlockIntroAudio(object __instance)
        {
            if (!IsRemovedScene(SceneNameOf(__instance)))
                return true;
            SilenceAudio(__instance);
            return false;
        }

        public static bool BlockIntroTrack(object __instance)
        {
            if (BootComplete || _applyingMenuTrack || __instance == null)
                return true;
            var name = ReadMember(__instance, "name") as string ?? string.Empty;
            if (!IsIntroTrackName(name))
                return true;
            LogOnce("Skipped intro music track '" + name + "'.");
            return false;
        }

        private static void OnBlocked(string key)
        {
            LogOnce("Removed cinematic scene load (" + key + ").");
            if (IsSplashKey(key) && SkipSplash)
                EnsureSplashStandIn();
        }

        private static void EnsureSplashStandIn()
        {
            if (_splashStandInDone)
                return;
            _splashStandInDone = true;
            try
            {
                var goType = AccessTools.TypeByName("UnityEngine.GameObject");
                var splashType = FirstTypeNamed("SplashScreenController");
                if (goType == null || splashType == null)
                    return;

                // Stay inactive. Activating SplashScreenController runs its enable
                // path, and that path is what other skip mods use to start the music.
                var go = Activator.CreateInstance(goType, "RemoveIntroCinematicSplash");
                var setActive = goType.GetMethod("SetActive", new[] { typeof(bool) });
                setActive?.Invoke(go, new object[] { false });
                goType.GetMethod("AddComponent", new[] { typeof(Type) })?.Invoke(go, new object[] { splashType });

                var component = goType.GetMethod("GetComponent", new[] { typeof(Type) })?.Invoke(go, new object[] { splashType });
                var finish = splashType.GetMethod("Finish", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                finish?.Invoke(component, null);
                SilenceObjectTree(go);
                setActive?.Invoke(go, new object[] { false });
                _splashFinished = true;
            }
            catch (Exception ex)
            {
                WarnOnce("Could not stand in for the splash screen", ex);
            }
        }

        private static void SilenceVideosInTree(object gameObject)
        {
            SilenceComponents(gameObject, "UnityEngine.Video.VideoPlayer", SilenceVideo);
        }

        private static bool IsIntroTrackName(string name)
        {
            var text = (name ?? string.Empty).ToLowerInvariant();
            if (text.Contains("menu") || text.Contains("title") || text.Contains("gameplay") || text.Contains("boss"))
                return false;
            return text.Contains("cinematic") || text.Contains("cutscene") || text.Contains("splash")
                || text.Contains("intro");
        }

        private static bool IsRemovedScene(string scene)
        {
            if (scene == "intro")
                return true;
            return scene == "splash" && SkipSplash;
        }

        private static bool IsCinematicObject(object component, bool includeVideoWord)
        {
            var gameObject = GameObjectOf(component);
            var name = (gameObject == null ? null : ReadMember(gameObject, "name") as string) ?? string.Empty;
            name = name.ToLowerInvariant();
            if (name.Contains("intro") || name.Contains("cinematic") || name.Contains("cutscene"))
                return true;
            if (includeVideoWord && name.Contains("video"))
                return true;
            return SkipSplash && (name.Contains("splash") || name.Contains("hopoo"));
        }

        private static bool MusicLivesIn(string sceneName)
        {
            var music = MusicInstance();
            if (music == null)
                return true;
            return string.Equals(SceneNameOf(music), sceneName, StringComparison.OrdinalIgnoreCase);
        }

        private static void SilenceObjectTree(object gameObject)
        {
            if (gameObject == null)
                return;

            SilenceComponents(gameObject, "UnityEngine.Video.VideoPlayer", SilenceVideo);
            SilenceComponents(gameObject, "UnityEngine.AudioSource", SilenceAudio);
            SilenceComponents(gameObject, "UnityEngine.Renderer", behaviour => WriteEnabled(behaviour, false));
            SilenceComponents(gameObject, "UnityEngine.Canvas", behaviour => WriteEnabled(behaviour, false));
            SilenceComponents(gameObject, "UnityEngine.Camera", behaviour => WriteEnabled(behaviour, false));
            SilenceComponents(gameObject, "UnityEngine.AudioListener", behaviour => WriteEnabled(behaviour, false));
        }

        private static void SilenceComponents(object gameObject, string typeName, Action<object> silence)
        {
            var type = AccessTools.TypeByName(typeName);
            if (type == null)
                return;
            var method = gameObject.GetType().GetMethod("GetComponentsInChildren", new[] { typeof(Type), typeof(bool) });
            var found = method?.Invoke(gameObject, new object[] { type, true }) as Array;
            if (found == null)
                return;
            foreach (var component in found)
            {
                if (component != null)
                    silence(component);
            }
        }

        private static void SilenceVideo(object video)
        {
            try
            {
                video.GetType().GetMethod("Stop", Type.EmptyTypes)?.Invoke(video, null);
                WriteEnabled(video, false);
                WriteMember(video, "playOnAwake", false);

                var output = video.GetType().GetProperty("audioOutputMode");
                if (output != null && output.CanWrite && output.PropertyType.IsEnum)
                    output.SetValue(video, Enum.ToObject(output.PropertyType, 0));

                var mute = video.GetType().GetMethod("SetDirectAudioMute", new[] { typeof(ushort), typeof(bool) });
                var volume = video.GetType().GetMethod("SetDirectAudioVolume", new[] { typeof(ushort), typeof(float) });
                for (ushort track = 0; track < 8; track++)
                {
                    mute?.Invoke(video, new object[] { track, true });
                    volume?.Invoke(video, new object[] { track, 0f });
                }

                var gameObject = GameObjectOf(video);
                if (gameObject != null)
                {
                    SilenceComponents(gameObject, "UnityEngine.AudioSource", SilenceAudio);
                    SilenceComponents(gameObject, "UnityEngine.Renderer", behaviour => WriteEnabled(behaviour, false));
                }
            }
            catch (Exception ex)
            {
                WarnOnce("Could not silence a video player", ex);
            }
        }

        private static void SilenceAudio(object source)
        {
            try
            {
                source.GetType().GetMethod("Stop", Type.EmptyTypes)?.Invoke(source, null);
                WriteMember(source, "mute", true);
                WriteMember(source, "volume", 0f);
                WriteEnabled(source, false);
            }
            catch (Exception ex)
            {
                WarnOnce("Could not silence an audio source", ex);
            }
        }

        private static void BeginMenuMusic()
        {
            if (_musicStarted)
                return;

            _musicAttempts++;
            if (MusicInstance() == null && _musicAttempts < 600)
                return;

            // Menu state first. StartIntroMusic posts Play_Music_System, which boots
            // every later track. Stopping that event, or never posting it, silences
            // the whole game. Do not mark music started until the post actually runs.
            try
            {
                ApplyTitleTrack();
                if (!InvokeStartIntroMusic())
                {
                    if (_musicAttempts < 600)
                        return;
                    _musicStarted = true;
                    Log?.LogWarning("Main menu music was not started. The music system was left running so the game can still start it.");
                    return;
                }

                ApplyTitleTrack();
                _musicStarted = true;
                LogInfo("Main menu music started.");
            }
            catch (Exception ex)
            {
                WarnOnce("Could not start main menu music", ex);
                if (_musicAttempts >= 600)
                    _musicStarted = true;
            }
        }

        private static void CorrectIntroTrackLeak()
        {
            var music = MusicInstance();
            var track = music == null ? null : ReadMember(music, "currentTrack");
            var name = (track == null ? null : ReadMember(track, "name") as string) ?? string.Empty;
            if (!IsIntroTrackName(name))
            {
                if (name.Length > 0)
                    LogOnce("Menu music track is '" + name + "'.");
                return;
            }

            LogOnce("Swapping leftover intro track '" + name + "' for the main menu theme.");
            ApplyTitleTrack();
        }

        private static void ApplyTitleTrack()
        {
            var sceneDef = FindSceneDef("title");
            var track = sceneDef == null ? null : ReadMember(sceneDef, "mainTrack");
            if (track == null)
                return;

            // Play is patched. This flag lets the menu theme through while an intro
            // track is still blocked. SetState only; do not stop the music system.
            _applyingMenuTrack = true;
            try
            {
                track.GetType().GetMethod("Play", Type.EmptyTypes)?.Invoke(track, null);
            }
            finally
            {
                _applyingMenuTrack = false;
            }
        }

        private static bool InvokeStartIntroMusic()
        {
            var instance = MusicInstance();
            var method = instance?.GetType().GetMethod("StartIntroMusic", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            if (instance == null || method == null)
                return false;
            method.Invoke(instance, null);
            return true;
        }

        private static object MusicInstance()
        {
            if (_musicObject != null)
                return _musicObject;

            var type = AccessTools.TypeByName("RoR2.MusicController");
            if (type == null)
                return null;

            var instance = ReadStatic(type, "instance") ?? ReadStatic(type, "Instance");
            // FindObjectOfType walks the loaded scene. Only fall back to it, and not
            // every frame, or the menu wait recreates the loading stutter.
            if (instance == null && _musicAttempts % 15 == 1)
                instance = FindObject(type);
            if (instance != null)
                _musicObject = instance;
            return instance;
        }

        private static void RequestTitle()
        {
            if (IsTitleScene())
                return;
            if (_titleAttempts >= 4)
                return;
            if (_titleAttempts > 0 && _framesSinceTitleAttempt < 90)
            {
                _framesSinceTitleAttempt++;
                return;
            }

            try
            {
                if (!TryChangeScene("title"))
                    return;
                _titleAttempts++;
                _framesSinceTitleAttempt = 0;
                LogInfo("Loading finished. Opening the main menu.");
            }
            catch (Exception ex)
            {
                WarnOnce("Could not open the main menu", ex);
            }
        }

        private static bool TryChangeScene(string sceneName)
        {
            var platform = AccessTools.TypeByName("RoR2.PlatformSystems");
            var manager = platform == null ? null : ReadStatic(platform, "networkManager");
            var method = manager?.GetType().GetMethod("ServerChangeScene", new[] { typeof(string) });
            if (method == null)
                return false;
            method.Invoke(manager, new object[] { sceneName });
            return true;
        }

        private static bool ForceSkipFlags()
        {
            var intro = SetConVar("IntroCutsceneController", "cvIntroSkip", true);
            var splash = !SkipSplash || SetConVar("SplashScreenController", "cvSplashSkip", true);
            var skip = SetStaticBool("IntroCutsceneController", "shouldSkip", true);
            return intro && splash && skip;
        }

        private static bool SetConVar(string typeName, string fieldName, bool value)
        {
            var type = FirstTypeNamed(typeName);
            var field = type?.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            var convar = field?.GetValue(null);
            if (convar == null)
                return false;

            var setBool = convar.GetType().GetMethod("SetBool", new[] { typeof(bool) });
            if (setBool != null)
            {
                setBool.Invoke(convar, new object[] { value });
                return true;
            }

            var setString = convar.GetType().GetMethod("SetString", new[] { typeof(string) });
            if (setString != null)
            {
                setString.Invoke(convar, new object[] { value ? "1" : "0" });
                return true;
            }

            return WriteMember(convar, "value", value);
        }

        private static bool SetStaticBool(string typeName, string fieldName, bool value)
        {
            var field = FirstTypeNamed(typeName)?.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (field == null || field.FieldType != typeof(bool))
                return false;
            field.SetValue(null, value);
            return true;
        }

        private static object FindSceneDef(string sceneName)
        {
            var catalog = AccessTools.TypeByName("RoR2.SceneCatalog");
            if (catalog == null)
                return null;

            foreach (var name in new[] { "GetSceneDefFromSceneName", "FindSceneDef", "GetSceneDef" })
            {
                var method = catalog.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (method == null || method.GetParameters().Length != 1 || method.GetParameters()[0].ParameterType != typeof(string))
                    continue;
                var found = method.Invoke(null, new object[] { sceneName });
                if (found != null)
                    return found;
            }

            foreach (var field in catalog.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            {
                var value = field.GetValue(null) as IEnumerable;
                if (value == null || value is string)
                    continue;
                foreach (var entry in value)
                {
                    var baseName = entry == null ? null : ReadMember(entry, "baseSceneName") as string;
                    if (string.Equals(baseName, sceneName, StringComparison.OrdinalIgnoreCase))
                        return entry;
                }
            }

            return null;
        }

        private static bool ShouldPatchLoadMethod(MethodInfo method)
        {
            // Only the two loads FixIntroSkip intercepts. Returning a default handle from
            // every other scene load leaves the loading bar waiting at 99%.
            if (method == null || method.IsAbstract || method.ContainsGenericParameters || method.IsSpecialName)
                return false;
            if (method.Name != "LoadSceneAdditivelyAsync" && method.Name != "StartLoadSceneAdditiveAsync")
                return false;
            if (!HasStringParameter(method))
                return false;

            var ret = method.ReturnType;
            return ret == typeof(void)
                || typeof(IEnumerator).IsAssignableFrom(ret)
                || ret.IsValueType;
        }

        private static bool HasStringParameter(MethodInfo method)
        {
            foreach (var parameter in method.GetParameters())
            {
                if (parameter.ParameterType == typeof(string))
                    return true;
            }
            return false;
        }

        private static string CinematicKey(object[] args)
        {
            if (args == null)
                return null;
            foreach (var arg in args)
            {
                if (arg is string value && IsCinematicKey(value))
                    return value;
            }
            return null;
        }

        private static bool IsCinematicKey(string value)
        {
            var key = value.Replace('\\', '/').Trim().ToLowerInvariant();
            if (key == IntroGuid)
                return true;
            if (key == SplashGuid)
                return SkipSplash;

            var name = key;
            var slash = name.LastIndexOf('/');
            if (slash >= 0)
                name = name.Substring(slash + 1);
            if (name.EndsWith(".unity"))
                name = name.Substring(0, name.Length - ".unity".Length);
            if (name == "intro")
                return true;
            return name == "splash" && SkipSplash;
        }

        private static bool IsSplashKey(string value)
        {
            var key = value.ToLowerInvariant();
            return key.Contains(SplashGuid) || key.EndsWith("splash") || key.Contains("/splash");
        }

        private static IEnumerator Empty()
        {
            yield break;
        }

        private static void TryUnload(string sceneName)
        {
            if (!_unloadAttempted.Add(sceneName))
                return;
            try
            {
                var sceneManager = AccessTools.TypeByName("UnityEngine.SceneManagement.SceneManager");
                var count = SceneCount(sceneManager);
                if (sceneManager == null || count <= 1)
                {
                    _unloadAttempted.Remove(sceneName);
                    return;
                }

                var getSceneAt = sceneManager.GetMethod("GetSceneAt", new[] { typeof(int) });
                if (getSceneAt == null)
                {
                    _unloadAttempted.Remove(sceneName);
                    return;
                }

                for (var i = 0; i < count; i++)
                {
                    var scene = getSceneAt.Invoke(null, new object[] { i });
                    if (!string.Equals(SceneObjectName(scene), sceneName, StringComparison.OrdinalIgnoreCase))
                        continue;
                    var loaded = scene.GetType().GetProperty("isLoaded")?.GetValue(scene);
                    if (loaded is bool ready && !ready)
                    {
                        _unloadAttempted.Remove(sceneName);
                        return;
                    }

                    foreach (var method in sceneManager.GetMethods(BindingFlags.Public | BindingFlags.Static))
                    {
                        var parameters = method.GetParameters();
                        if (method.Name == "UnloadSceneAsync" && parameters.Length == 1 && parameters[0].ParameterType == scene.GetType())
                        {
                            method.Invoke(null, new object[] { scene });
                            LogInfo("Unloaded the '" + sceneName + "' scene.");
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                WarnOnce("Could not unload the " + sceneName + " scene", ex);
            }
        }

        private static void ForEachRoot(string sceneName, Action<object> visit)
        {
            var sceneManager = AccessTools.TypeByName("UnityEngine.SceneManagement.SceneManager");
            var count = SceneCount(sceneManager);
            var getSceneAt = sceneManager?.GetMethod("GetSceneAt", new[] { typeof(int) });
            if (getSceneAt == null)
                return;
            for (var i = 0; i < count; i++)
            {
                var scene = getSceneAt.Invoke(null, new object[] { i });
                if (!string.Equals(SceneObjectName(scene), sceneName, StringComparison.OrdinalIgnoreCase))
                    continue;
                var roots = scene.GetType().GetMethod("GetRootGameObjects", Type.EmptyTypes)?.Invoke(scene, null) as Array;
                if (roots == null)
                    continue;
                foreach (var root in roots)
                    visit(root);
            }
        }

        private static bool IsSceneLoaded(string sceneName)
        {
            var sceneManager = AccessTools.TypeByName("UnityEngine.SceneManagement.SceneManager");
            var count = SceneCount(sceneManager);
            var getSceneAt = sceneManager?.GetMethod("GetSceneAt", new[] { typeof(int) });
            if (getSceneAt == null)
                return false;
            for (var i = 0; i < count; i++)
            {
                var scene = getSceneAt.Invoke(null, new object[] { i });
                if (string.Equals(SceneObjectName(scene), sceneName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static int SceneCount(Type sceneManager)
        {
            var count = sceneManager?.GetProperty("sceneCount")?.GetValue(null);
            return count is int value ? value : 0;
        }

        private static bool IsTitleScene()
        {
            return string.Equals(ActiveSceneName(), "title", StringComparison.OrdinalIgnoreCase);
        }

        private static string ActiveSceneName()
        {
            var sceneManager = AccessTools.TypeByName("UnityEngine.SceneManagement.SceneManager");
            var scene = sceneManager?.GetMethod("GetActiveScene", Type.EmptyTypes)?.Invoke(null, null);
            return SceneObjectName(scene);
        }

        private static string SceneObjectName(object scene)
        {
            return scene?.GetType().GetProperty("name")?.GetValue(scene) as string;
        }

        private static string SceneNameOf(object component)
        {
            var gameObject = GameObjectOf(component);
            if (gameObject == null)
                return null;
            return SceneObjectName(gameObject.GetType().GetProperty("scene")?.GetValue(gameObject));
        }

        private static object GameObjectOf(object component)
        {
            return component?.GetType().GetProperty("gameObject")?.GetValue(component);
        }

        private static bool? ReadIsLoading()
        {
            var app = AccessTools.TypeByName("RoR2.RoR2Application");
            if (app == null)
                return null;
            foreach (var name in new[] { "isLoading", "IsLoading" })
            {
                var value = ReadStatic(app, name);
                if (value is bool loading)
                    return loading;
            }
            return null;
        }

        private static object FindObject(Type type)
        {
            var unityObject = AccessTools.TypeByName("UnityEngine.Object");
            foreach (var method in unityObject.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                var parameters = method.GetParameters();
                if (method.Name == "FindObjectOfType" && parameters.Length == 1 && parameters[0].ParameterType == typeof(Type))
                    return method.Invoke(null, new object[] { type });
            }
            return null;
        }

        private static List<Type> FindTypesNamed(string shortName)
        {
            var found = new List<Type>();
            var direct = AccessTools.TypeByName("RoR2." + shortName) ?? AccessTools.TypeByName(shortName);
            if (direct != null)
                found.Add(direct);

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types;
                }

                if (types == null)
                    continue;
                foreach (var type in types)
                {
                    if (type != null && type.Name == shortName && !found.Contains(type))
                        found.Add(type);
                }
            }

            return found;
        }

        private static Type FirstTypeNamed(string shortName)
        {
            if (_typeCache.TryGetValue(shortName, out var cached))
                return cached;
            if (_typeMiss.TryGetValue(shortName, out var until) && _tick < until)
                return null;

            var found = FindTypesNamed(shortName);
            if (found.Count == 0)
            {
                _typeMiss[shortName] = _tick + 30;
                return null;
            }

            _typeCache[shortName] = found[0];
            return found[0];
        }

        private static object ReadStatic(Type type, string name)
        {
            var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (property != null)
                return property.GetValue(null);
            var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            return field?.GetValue(null);
        }

        private static object ReadMember(object instance, string name)
        {
            if (instance == null)
                return null;
            var type = instance.GetType();
            var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (property != null)
                return property.GetValue(instance);
            var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return field?.GetValue(instance);
        }

        private static bool WriteMember(object instance, string name, object value)
        {
            if (instance == null)
                return false;
            var type = instance.GetType();
            var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (property != null && property.CanWrite && property.PropertyType.IsInstanceOfType(value))
            {
                property.SetValue(instance, value);
                return true;
            }

            var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null)
                return false;
            field.SetValue(instance, value);
            return true;
        }

        private static void WriteEnabled(object behaviour, bool enabled)
        {
            var property = behaviour?.GetType().GetProperty("enabled", BindingFlags.Public | BindingFlags.Instance);
            if (property != null && property.CanWrite && property.PropertyType == typeof(bool))
                property.SetValue(behaviour, enabled);
        }

        private static void LogInfo(string message)
        {
            Log?.LogInfo(message);
        }

        private static void LogOnce(string message)
        {
            if (_once.Add(message))
                LogInfo(message);
        }

        private static void WarnOnce(string message, Exception ex)
        {
            var root = ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
            if (_once.Add(message + "|" + root.GetType().Name + "|" + root.Message))
                Log?.LogWarning(message + ": " + root.Message);
        }
    }
}
