# PenguTEX 0.1.0-alpha.2

Fix first-run builds into a folder created through a file picker or file manager.
An empty output folder is accepted and receives its manifest after successful
validation. Nonempty folders without a PenguTEX manifest remain protected.

- Check the destination before a large source rescan in desktop and CLI Build.
- Label Build’s source refresh as Recheck textures/sources/archives.
- Recheck output ownership before publication, preserving files added during a job.
- Recover interrupted publication when the original destination was empty.

Policies, texture processing, source files and saved preferences are unchanged.
Close and reopen the application to load the fix; the same empty output path works.

Portable Linux x64 and AppImage packages include source, dependency notices and
checksums. In-game appearance, resident VRAM/frame-time benefits and GPU acceleration
remain unvalidated. Use an unmounted original Data directory for profile baselines.
