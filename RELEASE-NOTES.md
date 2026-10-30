# PenguTEX 0.1.0-alpha.3

Show what happens after texture generation. Large builds previously appeared stuck
near the final texture while PenguTEX silently verified the staged output.

- Report Verify counts and texture paths, followed by Publish and Cleanup phases.
- Always log phase changes and final counts, even within the log throttle interval.
- Keep parallel Build counts ordered and prevent queued desktop progress from
  overwriting completion, failure or cancellation status.
- Report progress for standalone CLI verification and check cancellation while
  traversing the output tree.

Wait for Complete, then install the chosen output folder. The temporary
`.pengutex-stage-*` folder holds work pending verification and publication.
Existing successful outputs remain usable and do not need rebuilding. Reopen the
application after the current job finishes to load this progress update.

Portable Linux x64 and AppImage packages include source, dependency notices and
checksums. In-game appearance, resident VRAM/frame-time benefits and GPU acceleration
remain unvalidated. Use an unmounted original Data directory for profile baselines.
