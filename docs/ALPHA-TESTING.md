# Alpha testing

PenguTEX builds a separate texture override mod from original sources. Start with
a fresh run; a previous optimizer, its output, and a reference comparison are not
required. Generation checks file integrity and provenance. Visual quality, live
VRAM use and gameplay performance need testing on your setup.

Install the matching [Windows or Linux package](DISTRIBUTION.md). Record the version
shown beside the title; hover over it for the full build identifier.

## Source setup

| Setup | Source selection |
| --- | --- |
| MO2 on Windows | Select **Mod-manager profile** and the instance folder containing `ModOrganizer.ini`. Portable instances keep it beside the manager; global instances commonly use `%LOCALAPPDATA%\ModOrganizer\<instance>`. |
| MO2 through Wine/Proton on Linux | Select **Mod-manager profile** and the instance folder containing `ModOrganizer.ini`. Under Locations, select the prefix if that INI contains Windows drive paths, or enter native Linux game/mods/profiles overrides. A Proton compatdata folder containing `pfx/dosdevices` is accepted. |
| Fluorine on Linux | Select **Mod-manager profile** and its instance folder containing `ModOrganizer.ini`. If Fluorine has mounted a merged Data folder, stop that VFS session or select an unmounted original Data directory under Locations. PenguTEX does not unmount it. |
| Loose original textures, either OS | Select **Texture folder** and an original mod/Data root or texture directory. Folder mode includes loose DDS files; it does not reconstruct archive activation or a manager's load order. |

Fluorine is a Linux manager. Linux users do not need to use Fluorine, and Windows
users do not need it. All profile modes read the manager's files directly; do not
launch PenguTEX inside a merged virtual filesystem. MO2 is not required for folder
mode. Other managers' deployed views have not been validated as profile sources.

Load the profile names and choose the intended profile. Enabled original mods,
activated archives, loose-file priority and Overwrite determine winners. Marked
previous optimizer outputs are excluded. Disabled mods are not restored. Use
**Excluded mods** for exact names of generated mods you want omitted; see the
[PenguLOD handoff](PENGULOD-HANDOFF.md) for LOD/grass output handling. No particular
modlist, manager location or previous optimizer is built into the application.

## First run

1. Choose a preset and review its limits and protection switches. Keep optional
   previous-output comparison off for a fresh run.
2. Choose separate output and cache locations outside the input folders. An empty
   output folder is accepted; an unrelated nonempty folder is not. Keep the cache
   for repeat runs.
3. Analyze, then review changed textures and Coverage. Resolve source errors before
   building. Protected and unresolved textures remain in their original mods.
4. Build the output. Wait for generation, Verify, Publish and Cleanup to finish.
   Install the selected output folder after **Output built**, including its manifest;
   temporary `.pengutex-stage-*` folders are working data.
5. Enable it at a higher texture priority in your manager. Preserve a way to return
   to the previous playable setup. PenguTEX does not enable mods or edit profiles.
6. Inspect representative armor, faces, foliage, terrain and PBR/complex materials.
   Compare the same save/scene, camera, weather and settings with output enabled
   and disabled. Keep frame generation settings identical and report them explicitly.

Useful feedback includes successful runs as well as failures: generation time,
changed/protected/unresolved counts, visible defects, stutters and comparable VRAM
or frame-time observations. File sizes and estimated savings are not measured VRAM
or FPS. Do not change multiple presets/mods at once when isolating a problem.

## Report results

Use the [alpha feedback form](https://github.com/opteron-x86/PenguTEX/issues/new?template=alpha-feedback.yml)
for a test result, or the bug form for a reproducible failure. Include:

- PenguTEX version, Windows edition/build or Linux distribution/session, and package type.
- Manager and version, source mode, preset/overrides, and whether an existing cache was reused.
- For game results: GPU/VRAM, driver, resolution, frame generation/upscaling and shader setup.
- Steps, expected/actual result, and any relevant error or affected game-relative texture path.

Reports and manifests can contain personal physical paths and mod names. Review
and redact them before sharing. Do not attach proprietary DDS, BSA, NIF or game
files. A small synthetic reproduction is preferable when possible.

## Current limits

- Windows is newly packaged for alpha testing. Native CI checks file operations,
  rendered controls and GUI startup; it does not prove Windows in-game compatibility.
- Linux CI covers Ubuntu, Debian and Fedora packages through X11/Xvfb. Other
  distributions, Wayland setups and hardware still benefit from testing.
- Coverage can list protected, unsupported or malformed originals. A verified output
  does not mean every source has been optimized or a target VRAM budget is guaranteed.
- The app uses CPU processing and has no automatic GPU encoder or live VRAM-budget
  fitting. Supported Skyrim SE/AE paths and material bindings are documented in
  [material coverage](MATERIALS.md); other games and VR need separate validation.

Broader gameplay acceptance remains [#14](https://github.com/opteron-x86/PenguTEX/issues/14).
