# Third-Party Notices

CloudLight Blizzard is licensed separately under GNU GPLv3. The following notices describe components whose authorship and license status remain independent of that project license.

## TwitchDropsMiner-NoAutoClaim / DevilXD TwitchDropsMiner

- Integration source: <https://github.com/yundan125/TwitchDropsMiner-NoAutoClaim>
- Upstream: <https://github.com/DevilXD/TwitchDropsMiner>
- Original author: DevilXD
- License: MIT License

The Drops integration retains the Twitch campaign, inventory, channel selection, settings, HTTP/GQL and WebSocket business core and replaces its Tkinter GUI with a JSON Lines headless facade. The complete MIT text is retained at `Integrations/Drops/twitch/core/LICENSE` and must be included in binary distributions.

Copyright (c) 2024 DevilXD

## CloudLight SOOP Drops Miner

- Source: <https://github.com/yundan125/cloudlight-soop-drops-miner>
- Direct upstream: <https://github.com/tom1230123/soop-drops-miner>
- CloudLight maintainer: cloudlight
- License status checked 2026-08-20: no explicit license was found in the CloudLight repository or its direct upstream.

Attribution records provenance only; it does not grant or imply redistribution rights. CloudLight Blizzard does not relicense this third-party source as GPLv3 or claim it as original CloudLight Blizzard code. The adapter is kept separate and release builds must not automatically bundle unlicensed upstream source without confirmed permission or a valid clean-room implementation.

## CloudLight Overwatch YouTube Watcher

- Source: <https://github.com/yundan125/CloudLight-Overwatch-YouTube-Watcher>
- Direct upstream: <https://github.com/ucarno/ow-league-tokens>
- License status checked 2026-08-20: the direct upstream has no declared license.

The CloudLight Blizzard Worker is a new GUI-independent implementation of the documented behavior and public YouTube/Chromium interfaces. It does not copy or redistribute the upstream PySide6 code and does not claim the unlicensed upstream source as GPLv3 code. This work was performed after a compatibility audit and is therefore described as an independent reimplementation, not as a formally separated clean-room process.

## BiliBiliDropsMiner

- Source: <https://github.com/mi0e/BiliBiliDropsMiner>
- Integrated upstream commit: `a0d8bd51728aabaef66c651613324adba15d9ce8`
- License: MIT License
- Copyright: (c) 2026 mi0e

CloudLight Blizzard vendors the non-GUI core under `Integrations/Drops/bilibili/vendor/` and keeps the upstream MIT notice at `Integrations/Drops/bilibili/LICENSE`. The integrated Worker uses the upstream HTTP client, QR login parsing, WBI signing, static live-room task discovery, official task/progress and reward APIs, and x25Kn watch-time session implementation. The upstream PySide6 GUI, Selenium/browser sniffing path, and optional Apprise dependency are not included in the product runtime.

The Bilibili Worker runtime dependencies are `httpx` (BSD-3-Clause), `qrcode` (BSD), `Pillow` (HPND), and their installed transitive dependencies `httpcore` (BSD-3-Clause), `h11` (MIT), `anyio` (MIT), `idna` (BSD-3-Clause), `certifi` (MPL-2.0), and Windows-only `colorama` (BSD-3-Clause). Each dependency remains subject to its own license; the build output must retain the corresponding package notices where required.

The exact runtime package license texts used by the current Worker build are
kept under `Integrations/Drops/bilibili/licenses/` and are published under
`THIRD_PARTY_LICENSES/Bilibili/` alongside the upstream notice.

## Python runtime components

- `aiohttp` (Apache-2.0 AND MIT)
- `yarl` (Apache-2.0)
- `Requests` (Apache-2.0)
- `yt-dlp` (public domain)
- `websocket-client` (Apache-2.0)
- `truststore` (MIT)
- `PyInstaller` (GPLv2-or-later with the special exception for distributing built applications; build tool/runtime bootloader)

The YouTube Worker currently pins Requests 2.34.2, yt-dlp 2026.7.4 and
websocket-client 1.9.0. SOOP and Twitch use the version ranges declared in
their requirements files. Each component remains subject to its own published
license. CustomTkinter, PySide6 and the original Tkinter GUI are not required
by the new Worker UI architecture.

## .NET application packages

- `Microsoft.Data.Sqlite` 8.0.7 — MIT License. Used for local SQLite storage;
  the package is referenced by `CloudLight Blizzard.csproj` and its runtime
  assembly is included by the application publish.
- `CommunityToolkit.WinUI.Notifications` 7.1.2 — MIT License. Used for
  Windows Toast notifications; the package is referenced by
  `CloudLight Blizzard.csproj` and its runtime assembly is included by the
  application publish.

The NuGet package metadata remains the authoritative source for any
transitive package notices.
