# Remove Intro Cinematic

Risk of Rain 2 plays a studio splash and an intro cinematic while the game loads, then waits for you to skip it. Hiding the video leaves the audio playing.

This mod does not play that cinematic. Startup shows the loading bar, then the main menu. There is no intro video, no intro audio, and no skip prompt.

The normal main-menu theme still starts when the menu appears. The game boots all later music from that call, so stage music keeps working. The logbook and the ending cinematic are not changed.

## Install

Install with a Thunderstore mod manager, or copy `RemoveIntroCinematic.dll` into `BepInEx/plugins`.

You need the BepInEx 5 pack you already use for Risk of Rain 2. This package does not depend on a specific BepInExPack version, so installing it will not downgrade that pack.

Do not also run FixIntroSkip, Skip Intro Deluxe, or AutoSkipIntroCutscene. Those mods start the intro music, and two mods both changing the startup scene can fight.

## Config

`BepInEx/config/com.removeintrocinematic.cfg`

| Key | Default | Effect |
| --- | --- | --- |
| `Enabled` | `true` | Remove the intro cinematic, video and audio. |
| `SkipSplashScreen` | `true` | Also skip the Hopoo splash. Set this to `false` to keep the splash. |

`BepInEx/LogOutput.log` should contain `Main menu music started.` from **Remove Intro Cinematic**.
