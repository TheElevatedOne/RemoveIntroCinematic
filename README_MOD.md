# Remove Intro Cinematic

![Static Badge](https://img.shields.io/badge/Vibecoded-Grok-black?style=for-the-badge&labelColor=%236F0E82)

Gets you to the main menu faster. The mod skips:

- **The developer logo splash screens**
- **The intro cinematic**
- **The intro music**

The main menu music still starts normally once you reach the title screen, the same way it does in the unmodded game.
Everything after that (stage music, boss music, fades between scenes) works as usual.

## How it works

- The splash scene is never loaded, so the logos don't show at all and you stay on the loading screen until the game is ready.
- The intro scene is never preloaded. The game goes straight to the main menu as soon as loading finishes.
  This also fixes the vanilla `intro_skip` option, which can switch scenes while the game is still loading.
- The intro track is not played. The game's music system, which normally starts with the intro music,
  is started when the first real scene (the main menu) loads. That scene's own track plays from there.
- The game only enables screen fades after it has seen the splash scene. The mod keeps that working when the splash is skipped.

If a game update changes the addressable keys of the splash or intro scenes, the mod falls back to matching them by scene name.

## Configuration

`BepInEx/config/com.elevated.RemoveIntroCinematic.cfg`

| Setting | Default | Description |
| --- | --- | --- |
| `SkipSplashScreen` | `true` | Skip the developer logo splash screens. |
| `SkipIntroCinematic` | `true` | Skip the intro cutscene and go to the main menu once loading is done. |
| `SkipIntroMusic` | `true` | Don't play the intro music. Music starts with the main menu. |

You can use any combination. For example, keep the logos but skip the cinematic, or skip the logos but keep the cinematic and its music.

### In-game settings

If [Risk Of Options](https://thunderstore.io/package/Rune580/Risk_Of_Options/) is installed, the same settings appear under
**Settings → Mod Options → Remove Intro Cinematic**. They only affect startup, so changes apply the next time you launch the game.
Risk Of Options is optional; without it, edit the config file instead.

## Compatibility

- Works with the base game and all DLCs. Nothing here depends on expansion content.
- Client-side only, so it doesn't affect multiplayer.
- Don't use it together with other intro-skip mods such as *FixIntroSkip* or *Skip Intro Deluxe*; this mod already covers what they do.
