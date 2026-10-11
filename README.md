# Remove Intro Cinematic

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
| `thunderstore.toml` | Source for the package website, version, dependencies, and team name. |
| `thunderstore/` | Generated `manifest.json`, package `README.md`, `CHANGELOG.md`, and `icon.png`. |
| `tools/make_thunderstore.py` | Reads `thunderstore.toml` and writes the manifest and zip. |

## Build on Linux

Requires `make`, `python3`, and the .NET SDK (`dotnet`).

```sh
make
```

That writes `dist/RemoveIntroCinematic.dll` and the Thunderstore zip. The DLL is a managed .NET assembly (a Windows PE DLL). BepInEx loads that file on Windows and under Proton. It is not a native `.so`. `make package` rebuilds the zip from `thunderstore.toml` without a code change.

Copy **only that DLL** into `BepInEx/plugins`. Do not copy `third_party/` or `build/`.

The plugin loads only when the process is named `Risk of Rain 2.exe`. That is the normal Steam and r2modman name, including under Proton.

## Thunderstore

`thunderstore.toml` is the package source. `make package` reads it, rewrites `thunderstore/manifest.json`, and builds the zip. Changing the website, version, description, dependencies, icon path, or team name in that file changes the package. Categories stay in the toml for `tcli publish`; they are not manifest fields.

```sh
make package
```

The upload zip is `dist/ELEVATED-RemoveIntroCinematic-1.0.1.zip`. The team name is the zip name, not a field inside `manifest.json`. Thunderstore assigns the team from the account that uploads. The zip root contains `manifest.json`, `README.md`, `CHANGELOG.md`, `icon.png`, and `RemoveIntroCinematic.dll`.

`manifest.json` has `name`, `version_number`, `website_url`, `description`, and `dependencies`. The dependency is `bbepis-BepInExPack-5.4.2122`. That pack also depends on `RiskofThunder-RoR2BepInExPack` 1.9.0. On a profile where nothing else asks for a newer RoR2 pack, the mod manager can install 1.9.0 instead of the current 1.45.0. Say so if that pin should be added too.

Keep `versionNumber` in step with `PluginVersion` in `Plugin.cs` and `<Version>` in the csproj. `make package` warns if they differ. The icon has to be a 256×256 PNG at the path in `thunderstore.toml`, or the package step stops.

## Config

`BepInEx/config/com.removeintrocinematic.cfg`

| Key | Default | Effect |
| --- | --- | --- |
| `Enabled` | `true` | Remove the intro cinematic, video and audio. |
| `SkipSplashScreen` | `true` | Also skip the Hopoo splash, so only the loading bar shows. Set this to `false` to keep the splash. |

Do not also run FixIntroSkip, Skip Intro Deluxe, or AutoSkipIntroCutscene. Those mods change the same startup scenes and can fight this one. This plugin does not shut off the music system.

## What you should see

The loading bar, then the main menu. No intro video, no intro score, and no manual skip. The menu theme starts with the menu.

`BepInEx/LogOutput.log` should contain lines from **Remove Intro Cinematic**, including `Main menu music started.`

## Limit

This was not run inside Risk of Rain 2 here. The game is not installed in this environment. Confirm the log line above on your machine.
