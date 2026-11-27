# PenguTEX

PenguTEX reduces texture resolution in Skyrim Special Edition modlists on Windows
and Linux. Preview the changes, then save the optimized textures as a separate mod.
Your original files stay unchanged.

It works with MO2, MO2 through Wine/Proton, Fluorine on Linux, and loose texture
folders. Profile scans include textures in BSAs and respect mod priority.

## Download

Download an x64 alpha build from [Releases](https://github.com/opteron-x86/PenguTEX/releases).

- Windows: extract the ZIP and run `win-x64/PenguTEX.Desktop.exe`.
- Linux: extract the ZIP and run `linux-x64/PenguTEX.Desktop`, or make
  the AppImage executable and launch it.
- macOS (Apple Silicon): no release package yet; build `PenguTEX.app` from source
  with `bash tools/build-macos.sh`.

See the [installation guide](docs/DISTRIBUTION.md) for more details.

## Use

1. Select a texture folder or mod-manager profile. For a profile, choose the
   instance folder containing `ModOrganizer.ini`, click **Load profiles**, and
   select your profile.
2. Choose **Quality**, **Balanced** or **Compact**, then adjust the size limits
   and protection options if needed.
3. Choose an output folder and a separate work cache folder, both outside your
   source folders. Use an empty output folder for your first build.
4. Click **Analyze textures**. Review the changes and previews; the **Coverage**
   tab lists protected textures and problems.
5. Click **Build output mod** and wait for **Complete**.
6. Install and enable the output in your mod manager with higher priority than
   the original texture mods. Check the results in game.

Disable the output mod to undo the changes.

More help: [desktop options](docs/DESKTOP.md), [CLI commands](docs/CLI.md),
[replacing another optimizer's output](docs/REPLACEMENT.md),
[building from source](docs/DISTRIBUTION.md#build-and-release).

Licensed under [GPL-3.0-only](LICENSE).
