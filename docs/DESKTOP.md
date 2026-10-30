# Desktop workflow

Start `PenguTEX.Desktop` in a portable distribution, or run
`dotnet run --project src/PenguTEX.Desktop -c Release` from source.

1. Select a texture folder, or an MO2/Fluorine instance and profile. Load profile
   names after choosing the instance. Folder sources contain loose textures only.
2. Choose Quality, Balanced or Compact and review the maximum dimensions and
   protection switches. Import/save JSON policies; advanced path rules use the same
   schema as the CLI. Later matching rules win. Previously saved numeric limits
   remain unchanged and appear as Custom / saved limits when they differ from a preset.
3. Choose separate output/cache folders. Output is a removable mod; cache is reusable
   work. They must be outside all source folders and separate from each other.
   Creating an empty output folder in the file picker is supported. Nonempty folders
   must be previous PenguTEX output; personal files prevent replacement.
4. Analyze. Review changed, protected, unsupported and invalid textures; search by
   path, source mod or decision reason. Source or policy edits invalidate review.
   When replacing an optimizer, select its Reference output folder first. Review
   the Replacement check tab for larger, missing and unassessed textures. Optional
   fitting preserves formats; a separate switch permits reference-covered landscape
   and character reductions. See [replacement checks](REPLACEMENT.md).
5. Inspect a texture to compare existing source/proposed mips. The channel selector
   shows RGB, individual RGB channels or alpha/data. RGB is displayed without alpha
   masking. Signed BC4/BC5 preview is unavailable; mip copying still preserves them.
   Preview decoding is capped at 16 million pixels per image.
6. Build the candidate mod. Source winners/headers and reference metadata are checked again before work; full
   source hashes are checked during processing. Cancellation preserves previously
   published output and verified cache. Re-running reuses verified files.
   After Build reaches its texture count, Verify reads every staged texture and
   checks its hash. Publish then moves the verified output into place; Cleanup
   removes the previous generated output when replacing it. Each phase has its
   own progress count. The temporary `.pengutex-stage-*` folder is working data;
   wait for Complete and use the chosen output folder for installation.
7. A completed build is a candidate, not an in-game performance result. When using
   a reference comparison, install at the compared mod priority and preserve the
   previous output for rollback. For a new standalone optimization, install/enable
   the output as a higher-priority mod through your mod manager.
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
