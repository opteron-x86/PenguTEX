# CLI

Build with .NET 10: `dotnet build src/PenguTEX.Cli -c Release`.
Run `dotnet src/PenguTEX.Cli/bin/Release/net10.0/PenguTEX.dll --help`, or use
the `PenguTEX` executable in a self-contained package.

```sh
PenguTEX profiles --instance /games/modlist
PenguTEX analyze --instance /games/modlist --profile Example --report analysis.json
PenguTEX analyze --folder /mods/TexturePack --preset balanced --report analysis.json
PenguTEX preset --preset balanced > policy.json
PenguTEX build --folder /mods/TexturePack --policy policy.json --output /generated/PenguTEX --cache /cache/PenguTEX --workers 2
PenguTEX verify /generated/PenguTEX
PenguTEX inspect /mods/TexturePack/textures/armor/example.dds
```

The folder source accepts a mod/Data root containing `textures`, or a texture folder.
It includes loose DDS files only; use a profile for archive activation and conflicts.
`--max` changes all category caps but does not disable protection rules. An explicit
policy rule such as `{"Pattern":"textures/armor/*","Maximum":1024}` overrides the
category cap and protection for matching files. Later rules take precedence.

`--game`, `--mods`, `--profiles`, `--overwrite`, `--wine-prefix`, `--data` and `--ini`
override profile locations. `--exclude-mods 'Mod A|Mod B'` excludes exact mod names.
Previous optimizer outputs are excluded by recognizable markers, not generic names.

Fluorine's mounted Data directory is a merged view. For a fresh baseline, stop its
virtual filesystem first, or supply an unmounted original Data directory using
`--data`. PenguTEX rejects the merged mount because it cannot reconstruct original
files hidden by existing optimizer outputs. It does not unmount anything itself.

Analyze is read-only. Build creates an output mod and reusable cache in separate
directories outside the inputs. An existing empty output folder is accepted for a first build. A nonempty output
folder must be owned by PenguTEX; extra untracked files prevent replacement. Build
checks the output location before scanning sources and again before publication. Keep the manifest with the output. Cancel with
Ctrl+C and rerun the same command to reuse validated cached textures. Install the
output as a high-priority mod; disable the old optimizer output to compare results.

JSON goes to stdout and progress/errors to stderr. Exit codes: 0 success, 1 failure,
2 incomplete source analysis, 130 cancellation. Unsupported and invalid textures
remain unchanged in their original mods and are counted explicitly. Missing or
ambiguous source layers prevent building. Payload estimates are not measured VRAM.
