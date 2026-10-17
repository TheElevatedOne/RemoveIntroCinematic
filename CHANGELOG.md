# Changelog

## 1.1.0

- Rewritten without Harmony. Fixes loading getting stuck at 99% in large modpacks (the old version crashed while checking music tracks).
- Music startup now goes through `MusicController.StartIntroMusic`, so mods that hook it (such as R2API.Sound custom music) keep working.
- Errors while opening the main menu can no longer stall loading.

## 1.0.0

- Initial release: skips the splash screens, the intro cinematic and the intro music.
- Main menu music starts normally after skipping.
- Optional Risk Of Options support for changing the settings in-game.
