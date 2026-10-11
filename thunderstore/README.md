# Remove Intro Cinematic

![Static Badge](https://img.shields.io/badge/Vibecoded-Grok-black?style=for-the-badge&labelColor=%236F0E82)

![ICON](https://raw.githubusercontent.com/TheElevatedOne/RemoveIntroCinematic/refs/heads/main/thunderstore/icon.png)

Risk of Rain 2 plays a studio splash and an intro cinematic while the game loads, then waits for you to skip it. Hiding the video leaves the audio playing.

This mod does not play that cinematic. Startup shows the loading bar, then the main menu. There is no intro video, no intro audio, and no skip prompt.

The normal main-menu theme still starts when the menu appears. The game boots all later music from that call, so stage music keeps working. The logbook and the ending cinematic are not changed.

The plugin code was written with assistance from Grok.

## Install

Install with a Thunderstore mod manager, or copy `RemoveIntroCinematic.dll` into `BepInEx/plugins`.

You need BepInEx 5. The package depends on `bbepis-BepInExPack` 5.4.2122, so a mod manager installs that pack when you do not already have one.

Do not also run FixIntroSkip, Skip Intro Deluxe, or AutoSkipIntroCutscene. Those mods change the same startup scenes and can fight this one.

## Config

`BepInEx/config/com.removeintrocinematic.cfg`

| Key | Default | Effect |
| --- | --- | --- |
| `Enabled` | `true` | Remove the intro cinematic, video and audio. |
| `SkipSplashScreen` | `true` | Also skip the Hopoo splash. Set this to `false` to keep the splash. |

`BepInEx/LogOutput.log` should contain `Main menu music started.` from **Remove Intro Cinematic**.
