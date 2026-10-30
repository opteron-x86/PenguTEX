# Roadmap

Implementation advances through focused issues and PRs. Hardware and in-game
acceptance remain separate from implementation. No unmeasured performance claims.

| Milestone | Deliverable | Acceptance |
| --- | --- | --- |
| M0 — Project foundation | Design, tracking, contribution workflow, CI | Linked issues and reproducible build/test commands |
| M1 — Native optimization engine | Profile/folder catalog, DDS inventory, policy, mip reduction, resumable output, CLI | Correct winners; identical retained mip bytes; failure preserves output; complete CLI fixture |
| M2 — Desktop workflow | Source setup, preset editing, review/filtering, inspection, progress/cancel, results | CLI/desktop parity; rendered UI checks; native startup |
| M3 — Material-aware CPU processing | Explicit re-encoding, color/data handling, mip repair | Format/channel-preserving fixtures and independently decoded results |
| M4 — Integration and distribution | Portable/AppImage, distro compatibility, manifests, PenguLOD workflow | Extracted packages run; reproducible benchmarks; source/licenses/checksums |
| M5 — Measured compatibility | Game visuals/frame times and optional physical GPU acceleration | Repeatable shader/hardware evidence; measured benefit before automatic GPU selection |
| M6 — Optimizer replacement parity | Baseline comparison, explicit limits, broader DDS processing and bounded rebuilding | No unexplained replacement growth; independent file checks; game acceptance tracked under M5 |

M1–M2 form the first useful alpha. M3 is opt-in; format-preserving mip reduction stays
the default. M4 makes the application distributable. M5 remains open until evidence
exists; CI alone cannot close in-game or physical-GPU validation.
The [game acceptance protocol](GAME-VALIDATION.md) defines the evidence required for M5.

## Work breakdown

1. Architecture/tracking and solution/build/CI foundations.
2. Native profiles, archive activation, asset conflicts, folder sources, hidden files,
   case diagnostics and prior-optimizer exclusion.
3. DDS header/payload inspection and inventory.
4. Category/protection rules, JSON presets, explicit overrides and explainable plans.
5. Streaming mip reduction and independent validation.
6. Content-addressed cache, per-file resume, cancellation, transactional publication.
7. Inspect/analyze/build/verify CLI and structured reports.
8. Native desktop workflow and persistent preferences.
9. Before/after texture inspection and channel-aware preview.
10. CPU conversion and correct color/normal/packed-data processing.
11. Packaging, desktop integration, source/licenses and distro smoke tests.
12. Manifest/provenance handoff and reproducible performance/recovery benchmarks.
13. Repeatable in-game validation across legacy, complex and PBR materials.
14. Optional GPU backend and physical-device quality/performance acceptance.

Full NIF-driven inference, automated VRAM-budget fitting, runtime hooks, archive
repacking, Windows distribution and other games are later candidates, not hidden
first-alpha requirements. Every scope expansion needs an issue and acceptance checks.

Track execution in [issues](https://github.com/opteron-x86/PenguTEX/issues) and
[milestones](https://github.com/opteron-x86/PenguTEX/milestones).

## Implementation issues

### [M0 — Project foundation](https://github.com/opteron-x86/PenguTEX/milestone/1)

- [#1 — Define application architecture and milestone workflow](https://github.com/opteron-x86/PenguTEX/issues/1)
- [#2 — Establish .NET solution, regression fixtures and Linux CI](https://github.com/opteron-x86/PenguTEX/issues/2)

### [M1 — Native optimization engine](https://github.com/opteron-x86/PenguTEX/milestone/2)

- [#3 — Resolve native profiles, loaded archives and winning textures](https://github.com/opteron-x86/PenguTEX/issues/3)
- [#4 — Inspect DDS structures and produce an accurate texture inventory](https://github.com/opteron-x86/PenguTEX/issues/4)
- [#5 — Plan reductions with reusable policies and explicit reasons](https://github.com/opteron-x86/PenguTEX/issues/5)
- [#6 — Implement format-preserving existing-mip reduction](https://github.com/opteron-x86/PenguTEX/issues/6)
- [#7 — Add verified cache, resumable jobs and transactional output](https://github.com/opteron-x86/PenguTEX/issues/7)
- [#8 — Expose inspect, analyze, build and verify CLI commands](https://github.com/opteron-x86/PenguTEX/issues/8)

### [M2 — Desktop workflow](https://github.com/opteron-x86/PenguTEX/milestone/3)

- [#9 — Build native desktop analysis and optimization workflow](https://github.com/opteron-x86/PenguTEX/issues/9)
- [#10 — Add texture and channel inspection with before/after comparison](https://github.com/opteron-x86/PenguTEX/issues/10)

### [M3 — Material-aware CPU processing](https://github.com/opteron-x86/PenguTEX/milestone/4)

- [#11 — Add opt-in CPU mip rebuilding with material-aware filtering](https://github.com/opteron-x86/PenguTEX/issues/11)

### [M4 — Integration and distribution](https://github.com/opteron-x86/PenguTEX/milestone/5)

- [#12 — Package Linux applications and validate extracted distributions](https://github.com/opteron-x86/PenguTEX/issues/12)
- [#13 — Define provenance handoff and reproducible performance diagnostics](https://github.com/opteron-x86/PenguTEX/issues/13)

### [M5 — Measured compatibility](https://github.com/opteron-x86/PenguTEX/milestone/6)

- [#14 — Validate in-game texture quality and performance](https://github.com/opteron-x86/PenguTEX/issues/14)
- [#15 — Evaluate optional GPU acceleration on physical devices](https://github.com/opteron-x86/PenguTEX/issues/15)

### [M6 — Optimizer replacement parity](https://github.com/opteron-x86/PenguTEX/milestone/7)

- [#25 — Compare optimizer baselines to prevent larger replacement textures](https://github.com/opteron-x86/PenguTEX/issues/25)
- [#26 — Extend native DDS mip copying to verified uncompressed layouts and BC6H](https://github.com/opteron-x86/PenguTEX/issues/26)
- [#29 — Bound CPU mip rebuilding and preserve uncompressed byte channels](https://github.com/opteron-x86/PenguTEX/issues/29)
- [#31 — Retain unresolved reference textures with explicit provenance](https://github.com/opteron-x86/PenguTEX/issues/31)
- [#34 — Require replacement mip chains to retain reference coverage](https://github.com/opteron-x86/PenguTEX/issues/34)

Use original assets for processing and a prior optimizer output as a comparison
reference. Opt-in carry-forward may preserve valid reference files byte for byte;
report these separately from native reductions. Account for installation priority, missing overrides, protected paths and
unsupported formats. Keep all comparisons and detailed source reports local unless
the user explicitly chooses to share them. File verification and game validation
are distinct acceptance stages; preserve the previous playable setup until both pass.

