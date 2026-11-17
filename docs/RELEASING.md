# Releasing

`Directory.Build.props` holds the version. Versions follow SemVer; alpha, beta and rc
versions are published as pre-releases.

## Steps

1. Set `<Version>` in `Directory.Build.props` and add a matching `<release>` entry at the
   top of `packaging/io.github.opteron_x86.PenguTEX.metainfo.xml`.
2. Rename `## [Unreleased]` in `CHANGELOG.md` to `## [<version>] - YYYY-MM-DD` and start a
   new empty `## [Unreleased]` above it. The section becomes the release notes on GitHub
   and Nexus. Write one line per change, starting with Added, Changed, Fixed or Removed,
   and say only what changed. If outputs have to be built again, say so in one line above
   the list. Causes, measurements and how a fix was checked belong in the pull request.
3. Check locally: `bash tools/test.sh`, `bash tools/build.sh`, `python3 tools/version.py`.
4. Open a pull request, wait for every check to pass, and merge it.
5. Tag the merge commit and push only the tag:

   ```bash
   git tag -a v0.1.0-alpha.11 -m 'PenguTEX 0.1.0-alpha.11'
   git push origin v0.1.0-alpha.11
   ```

The tag build checks that the tag, metainfo and changelog match the version, runs every
Linux, Windows and distribution check, and publishes the GitHub release only when all of
them pass.

## What is published

- `PenguTEX-<version>-win-x64.zip`
- `PenguTEX-<version>-linux-x64.tar.gz` and `PenguTEX-<version>-x86_64.AppImage`
- `PenguTEX-<version>-source.tar.gz`
- `SHA256SUMS`

## Upload a release to Nexus Mods

The **Upload release to Nexus** workflow is started by hand. It downloads the Windows and
Linux packages of a published GitHub release, checks them against the release checksums,
the tag's successful build and the version recorded inside each package, and adds each
one as a new version of its file on the mod page. The previous versions are archived, and
the mod's version and changelog are updated from `CHANGELOG.md`.

Nexus accepts ZIP, 7z and RAR archives, so the Linux package is uploaded as a ZIP with the
same contents and file permissions. The AppImage stays on GitHub.

One-time setup:

1. On the Nexus mod page, upload the Windows and Linux packages once by hand so that each
   has a file ID.
2. In the repository, open **Settings → Environments** and create an environment named
   `nexus`.
3. Add the secret `NEXUSMODS_API_KEY` with your Nexus Mods API key. Keep the key out of
   files, issues and chat.
4. Add the variables `NEXUSMODS_MOD_ID`, `NEXUSMODS_FILE_ID_WINDOWS` and
   `NEXUSMODS_FILE_ID_LINUX`. The IDs are shown under **Advanced** in the mod page's Files
   tab and in the edit menu of Manage Files.

For each release, once the tag build has finished:

1. Open **Actions → Upload release to Nexus → Run workflow**.
2. Select `master` and enter the tag, such as `v0.1.0-alpha.11`.

If an upload fails partway, check the Files and Changelog tabs on Nexus before running it
again, so that a file is not uploaded twice. The page description and images are edited on
Nexus.
