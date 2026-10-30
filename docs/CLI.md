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

Build reports generation, per-texture verification, publication and previous-output
cleanup separately. Each phase has its own count; generation reaching its total
does not mean publication is finished. Standalone `verify` also reports progress.

For an optimizer replacement, use `--reference /mods/PreviousOutput` and, when the
folder is outside the instance, `--replace-mod 'Previous Output'`. The comparison
uses that mod's profile priority, even if disabled. `--fit-reference true` selects
existing mips that fit the reference dimensions and payload while preserving format.
`--fit-protected true` also permits fitting reference-covered landscape/character
textures. Optional `--carry-reference true` retains valid reference files unchanged
for unresolved paths and reports them separately. `--require-parity true` refuses publication if larger or unassessed paths
remain. See [replacement checks](REPLACEMENT.md) for assumptions and acceptance.

JSON goes to stdout and progress/errors to stderr. Exit codes: 0 file operation
succeeded, 1 failure, 2 incomplete source analysis, 3 required reference parity not
met, 130 cancellation. Unsupported and invalid textures
remain unchanged in their original mods and are counted explicitly. Missing or
ambiguous source layers prevent building. Payload estimates are not measured VRAM.
