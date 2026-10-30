# PenguTEX 0.1.0-alpha.8

The desktop now describes a successful build as finished output. The previous
"game validation pending" message appeared after every build without tracking any
in-game check.

- Remove the introductory subheading under the application title.
- Rename the build action to "Build output mod" and the result to "Output built".
- Show the verified texture count, output size and build time in the result summary.
- Keep protected/unresolved source coverage and optional comparison assessments
  available for review.
- Update the desktop guide to match the current Coverage tab and optional
  comparison controls.

Texture processing, presets, cache recipes and output manifests are unchanged.
Existing generated output does not need to be rebuilt for this interface update.
