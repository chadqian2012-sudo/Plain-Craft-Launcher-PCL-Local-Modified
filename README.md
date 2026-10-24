# Plain Craft Launcher (PCL) 本地修改版 2.99.1

**Third-party modification based on PCL 2.99.0.0. This is not an official PCL release.**

This public repository contains the upstream source snapshot, the targeted patch tool source, and the modified launcher release package. The launcher disables its self-updater and restores the original-game download entry, including for offline accounts. Game files still require network access.

## Version archive

See [VERSIONS.md](VERSIONS.md) for the modification status and declarations for every locally verified version archived here:

- `2.13.1.1` — upstream original, unmodified.
- `2.99.0.0` — upstream original, unmodified.
- `2.99.0.0 local` — third-party modified historical build.
- `2.99.1 local` — current third-party modified release.

## Contributors

- PCL original author: 龙腾猫跃
- Third-party modifications and packaging: chadqian2012-sudo

See [CONTRIBUTORS.md](CONTRIBUTORS.md), [release notes](发布说明.md), and [upstream LICENCE](LICENCE).

## Repositories

- This third-party repository: https://github.com/chadqian2012-sudo/Plain-Craft-Launcher-PCL-Local-Modified
- Upstream PCL repository: https://github.com/Meloong-Git/PCL
- Original author support: https://meloong.com/afd/a/LTCat

## Current download

See the GitHub Release tagged `v2.99.1` for the ZIP containing the launcher and sources. The ZIP does not include `.git` history, Minecraft game files, account data, logs, or world saves.

## Verification and scope

The 2.99.1 binary was generated from the verified official PCL 2.99.0.0 executable. Patch audit: 18 intended method changes; 6,321 other methods, 3,603 fields/constants, and 2 embedded resources unchanged. See `patch-audit.json`. The 2.99.1 revision itself has not been re-tested by downloading or launching the game.

The upstream `LICENCE` is included unchanged. This repository does not relicense upstream PCL or grant additional rights.