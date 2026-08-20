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

## Python runtime components

- aiohttp
- yarl
- Requests
- yt-dlp
- websocket-client
- PyInstaller (build tool/runtime bootloader)

Each component remains subject to its own published license. CustomTkinter, PySide6 and the original Tkinter GUI are not required by the new Worker UI architecture.
