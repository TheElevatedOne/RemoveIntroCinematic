# Remove Intro Cinematic

![Static Badge](https://img.shields.io/badge/Vibecoded-Grok-black?style=for-the-badge&labelColor=%236F0E82)

Risk of Rain 2 plays a studio splash and an intro cinematic while the game loads, then waits for you to skip the cinematic. Hiding the video is not enough: the clip and the intro cue still play audio.

This plugin does not play that cinematic at all. Startup shows the loading bar, then goes to the main menu. The cinematic video does not play, and neither does its audio. The normal main-menu theme starts once the menu is actually up. Stage music, the logbook, and the ending cinematic are left alone.

The menu theme has to start. The game boots its whole music system from that call. Skipping it leaves every later stage silent.

## Layout

| Path | What it is |
| --- | --- |
| `src/RemoveIntroCinematic/` | Plugin source. |
| `src/UnityEngineStub/` | Compile-time stand-in for `UnityEngine.dll`. Not shipped. |
| `third_party/bepinex/` | BepInEx 5 and HarmonyX, used only to compile. |
| `build/` | Intermediate output. Deleted by `make clean`. |
| `dist/` | `RemoveIntroCinematic.dll` and the Thunderstore zip. |
| `thunderstore/` | `manifest.json`, package `README.md`, `CHANGELOG.md`, and your `icon.png`. |

## Build on Linux

Requires `make`, `python3`, and the .NET SDK (`dotnet`).

```sh
make

```

That writes `dist/RemoveIntroCinematic.dll`. It is a managed .NET assembly (a Windows PE DLL). BepInEx loads that file on Windows and under Proton. It is not a native `.so`.

Copy **only that DLL** into `BepInEx/plugins`. Do not copy `third_party/` or `build/`.

The plugin loads only when the process is named `Risk of Rain 2.exe`. That is the normal Steam and r2modman name, including under Proton.

```
make package
```

Build a Thunderstore Uploadable zip file.

## Config

`BepInEx/config/com.removeintrocinematic.cfg`

| Key | Default | Effect |
| --- | --- | --- |
| `Enabled` | `true` | Remove the intro cinematic, video and audio. |
| `SkipSplashScreen` | `true` | Also skip the Hopoo splash, so only the loading bar shows. Set this to `false` to keep the splash. |

Do not also run FixIntroSkip, Skip Intro Deluxe, or AutoSkipIntroCutscene. Those mods still start the intro music. This plugin blocks that music if they are present, but two mods both changing the startup scene can fight.

## What you should see

The loading bar, then the main menu. No intro video, no intro score, and no manual skip. The menu theme starts with the menu.

`BepInEx/LogOutput.log` should contain lines from **Remove Intro Cinematic**, including `Main menu music started.`

## Limit

This was not run inside Risk of Rain 2 here. The game is not installed in this environment. Confirm the log line above on your machine.
