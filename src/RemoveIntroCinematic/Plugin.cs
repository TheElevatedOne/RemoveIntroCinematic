using BepInEx;

namespace RemoveIntroCinematic
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInProcess("Risk of Rain 2.exe")]
    public sealed class RemoveIntroCinematicPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.removeintrocinematic";
        public const string PluginName = "Remove Intro Cinematic";
        public const string PluginVersion = "1.0.0";

        private bool _enabled;

        private void Awake()
        {
            Boot.Log = Logger;
            Boot.Host = this;
            _enabled = Config.Bind(
                "General",
                "Enabled",
                true,
                "Remove the intro cinematic entirely, including its video and audio.").Value;
            Boot.SkipSplash = Config.Bind(
                "General",
                "SkipSplashScreen",
                true,
                "Also skip the studio splash, so startup shows the loading bar and then the main menu.").Value;

            if (!_enabled)
            {
                Logger.LogInfo("Disabled in config. The intro cinematic will play normally.");
                return;
            }

            Logger.LogInfo("Removing the intro cinematic and its audio.");
            Boot.Install();
        }

        private void Update()
        {
            if (_enabled)
                Boot.Tick();
        }
    }
}
