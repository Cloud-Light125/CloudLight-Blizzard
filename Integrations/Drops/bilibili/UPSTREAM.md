# Bilibili Drops integration provenance

CloudLight Blizzard integrates the non-GUI core of
[`mi0e/BiliBiliDropsMiner`](https://github.com/mi0e/BiliBiliDropsMiner) at
upstream commit
`a0d8bd51728aabaef66c651613324adba15d9ce8` (the `main` head fetched on
2026-09-01).

Vendored modules are under `vendor/bilibili_drops_miner/`.  The adapter keeps
the upstream `BilibiliClient`, QR login parsing, WBI signing, static live-room
task discovery, task/progress parsing, mission reward API, and x25Kn live
watch-time session implementation.  CloudLight adds the JSON Lines Worker,
dynamic per-room session pool, official progress polling, idempotent claiming,
DPAPI credential hand-off, and the direct-only network policy.

The upstream GUI, PySide6, Selenium/browser sniffing, colorama, and optional
Apprise integration are intentionally not runtime dependencies of the Worker.
The product flow is local QR login plus HTTP discovery.  Gotify, ServerChan,
WeCom webhook, and wxwork notification targets remain available through the
upstream native notifier when configured.

The upstream project is MIT licensed; the exact notice is preserved in
`LICENSE` beside this file.  Dependency license metadata is recorded in
`THIRD_PARTY_NOTICES.md` at the repository root.
