# Desktop workflow

Start `PenguTEX.Desktop` in a portable distribution, or run
`dotnet run --project src/PenguTEX.Desktop -c Release` from source.

1. Select a texture folder, or an MO2/Fluorine instance and profile. Load profile
   names after choosing the instance. Folder sources contain loose textures only.
2. Choose Quality, Balanced or Compact and review the maximum dimensions and
   protection switches. Import/save JSON policies; advanced path rules use the same
   schema as the CLI. Later matching rules win.
3. Choose separate output/cache folders. Output is a removable mod; cache is reusable
   work. They must be outside all source folders and separate from each other.
4. Analyze. Review changed, protected, unsupported and invalid textures; search by
   path, source mod or decision reason. Source or policy edits invalidate review.
5. Inspect a texture to compare existing source/proposed mips. The channel selector
   shows RGB, individual RGB channels or alpha/data. RGB is displayed without alpha
   masking. Signed BC4/BC5 preview is unavailable; mip copying still preserves them.
   Preview decoding is capped at 16 million pixels per image.
6. Build the output mod. Source winners/headers are checked again before work; full
   source hashes are checked during processing. Cancellation preserves previously
   published output and verified cache. Re-running reuses verified files.
7. Install/enable the output as a higher-priority mod through your mod manager.
   Disable/remove it to revert. PenguTEX does not edit profiles or launch the game.

Preferences use `$XDG_CONFIG_HOME/PenguTEX/settings.json`, or
`~/.config/PenguTEX/settings.json`. Cache defaults to `$XDG_CACHE_HOME/PenguTEX`, or
`~/.cache/PenguTEX`. `--settings FILE` selects an isolated preferences file.
`--smoke` opens the real native window and exits after two seconds for startup tests.

If Fluorine has mounted a merged Data directory, stop that VFS session before a
profile scan, or supply an unmounted original Data directory under Locations.
PenguTEX refuses to use merged Data as an original baseline because that can feed
existing optimized output back into the next run. It never unmounts it automatically.

Export analysis saves detailed local JSON, including physical source paths. Estimates
describe texture payload, not measured resident VRAM or FPS. Unsupported and invalid
textures remain in their original mods; source-resolution errors block publication.
