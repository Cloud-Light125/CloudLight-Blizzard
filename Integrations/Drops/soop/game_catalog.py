"""Official SOOP game IDs are identity; directory titles are replaceable display metadata."""
from __future__ import annotations

import json
from pathlib import Path


class GameCatalog:
    def __init__(self, path: Path):
        self.path = path
        self.names = {}
        # Bundled, sanitized official directory is a verified offline seed.
        seed = json.loads(Path(__file__).with_name('game-catalog.official.json').read_text('utf-8'))
        self.update(seed.get('retainedVerifiedNames', []), persist=False)
        self.update(seed['data'], persist=False)
        try:
            self.update(json.loads(path.read_text('utf-8')).get('data', []), persist=False)
        except (OSError, ValueError, TypeError):
            pass

    def update(self, rows, persist=True):
        if not isinstance(rows, list): raise ValueError('SOOP 游戏目录不是列表')
        for row in rows:
            if not isinstance(row, dict): continue
            key, name = str(row.get('gameIdx') or row.get('gameNo') or '').strip(), str(row.get('gameTitle') or '').strip()
            if key and name and not name.startswith('game:'):
                self.names[key] = name
        if persist:
            from Shared.protocol import atomic_write_json
            try:
                atomic_write_json(self.path, {'data': [{'gameIdx': k, 'gameTitle': v} for k, v in self.names.items()]})
            except OSError:
                import logging
                logging.getLogger(__name__).warning('已读取官方游戏名称，但无法写入名称缓存；本次会话继续使用已验证目录')

    def name(self, game_id, fallback=''):
        key = str(game_id).removeprefix('game:')
        name = self.names.get(key) or (fallback if fallback and fallback not in {game_id, '游戏 ' + key, key} and not fallback.startswith('game:') else '')
        # Only the user-verified localization is used; all other titles stay official.
        return '守望先锋（오버워치）' if key == '12' and name == '오버워치' else name or f'未知游戏（ID: {key}）'

    def decorate(self, row):
        if row.get('gameId', '').startswith('game:'):
            row['gameName'] = self.name(row['gameId'], row.get('gameName', ''))
        return row
