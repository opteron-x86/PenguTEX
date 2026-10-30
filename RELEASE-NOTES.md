# PenguTEX 0.1.0-alpha.10

This alpha adds native Windows x64 distribution alongside the Linux portable
archive and AppImage, with shared texture processing and presets.

- **Windows:** download the `win-x64.zip`, extract the entire archive, and open
  `win-x64/PenguTEX.Desktop.exe`. The package includes the CLI and .NET runtime;
  no SDK or installer is required. The alpha executables are unsigned.
- **Linux:** download the `linux-x64.tar.gz` or `x86_64.AppImage`. PenguTEX runs
  natively and does not require Wine.
- **Source setup:** MO2 on Windows, MO2 through Wine/Proton on Linux, Fluorine on
  Linux, or loose original texture folders on either platform. Fluorine is a Linux
  manager and is not required for other setups.
- The source picker now uses “Mod-manager profile,” with platform-specific guidance.
  Windows settings/cache use LocalAppData; Linux keeps its existing XDG locations.
- Fix Windows folder-case aliases bypassing output locks or source/reference
  separation. Original textures remain untouched and output is a separate mod.

Fresh runs require no VRAMr installation or previous optimizer output. Every
replacement comes from selected originals; profile runs use enabled sources only.
Texture recipes, presets and output format are unchanged, so existing generated
output does not need to be rebuilt just to update the application.

See [installation](https://github.com/opteron-x86/PenguTEX/blob/v0.1.0-alpha.10/docs/DISTRIBUTION.md)
and the [alpha testing guide](https://github.com/opteron-x86/PenguTEX/blob/v0.1.0-alpha.10/docs/ALPHA-TESTING.md).
Successful runs and failures on different setups are both useful; submit
[alpha feedback](https://github.com/opteron-x86/PenguTEX/issues/new?template=alpha-feedback.yml).

Release checks cover engine/rendered UI tests, extracted CLI workflows and native
GUI startup on Windows Server 2025, Ubuntu 24.04, Debian 13 and Fedora 43. Windows
in-game results and broader hardware/driver compatibility still need testers.
Coverage may include protected or unresolved originals; file verification is not
a promise of complete optimization or a particular VRAM/FPS gain.

Downloads include corresponding source, dependency licenses and `SHA256SUMS`.
