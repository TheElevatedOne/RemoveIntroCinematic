using System.IO;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using RiskOfOptions;
using RiskOfOptions.OptionConfigs;
using RiskOfOptions.Options;
using UnityEngine;

namespace RemoveIntroCinematic
{
    // Only touched when RiskOfOptions is installed, so its types are never loaded otherwise.
    internal static class RiskOfOptionsCompat
    {
        internal const string PluginGUID = "com.rune580.riskofoptions";

        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        internal static void Init(string pluginDirectory, params ConfigEntry<bool>[] entries)
        {
            const string guid = RemoveIntroCinematicPlugin.PluginGUID;
            const string name = "Remove Intro Cinematic";

            ModSettingsManager.SetModDescription(
                "Skips the developer logos, the intro cinematic and the intro music on startup. "
                + "Changes take effect the next time the game starts.",
                guid, name);

            var icon = LoadIcon(Path.Combine(pluginDirectory, "icon.png"));
            if (icon)
            {
                ModSettingsManager.SetModIcon(icon, guid, name);
            }

            foreach (var entry in entries)
            {
                ModSettingsManager.AddOption(new CheckBoxOption(entry, new CheckBoxConfig { restartRequired = true }), guid, name);
            }
        }

        private static Sprite LoadIcon(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var texture = new Texture2D(2, 2);
            if (!texture.LoadImage(File.ReadAllBytes(path)))
            {
                return null;
            }
            return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
        }
    }
}
