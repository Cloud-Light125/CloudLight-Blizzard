from __future__ import annotations

import asyncio
import sys
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import AsyncMock

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from api_compat import install_drops_compat, inventory_unavailable


class SoopApiCompatibilityTests(unittest.TestCase):
    def test_expiration_flag_does_not_block_a_future_expiry(self):
        self.assertFalse(inventory_unavailable({'expFlag': 'Y', 'expDate': '2099-01-01 00:00:00'}))
        self.assertTrue(inventory_unavailable({'expFlag': 'Y', 'expDate': '2000-01-01 00:00:00'}))
        self.assertTrue(inventory_unavailable({'expFlag': 'Y', 'expDate': 'invalid'}))
    def make_client(self, rows, block=lambda raw: None):
        class Base:
            def __init__(self):
                self.get_inventory = AsyncMock(return_value=rows)

        module = SimpleNamespace(DropsClient=Base, DROPS_ORIGIN='https://drops.sooplive.com',
                                 DROPS_API='https://drops.sooplive.com/api', USER_AGENT='test',
                                 DropsAuthenticationError=PermissionError)
        install_drops_compat(module, block)
        return module.DropsClient()

    def test_official_use_body_contains_fresh_item_idx(self):
        client = self.make_client([SimpleNamespace(item_code_idx='reward', claimed=False,
                                                  raw={'itemIdx': '42', 'itemType': '1'})])
        client._request = AsyncMock(return_value={'result': 1})
        asyncio.run(client.get_item_detail_response('reward'))
        self.assertEqual(client._request.call_args.kwargs['json_body'], {'itemCodeIdx': 'reward', 'itemIdx': 42})
        client.get_inventory.assert_awaited_once_with(with_codes=False)

    def test_missing_unbound_expired_or_renewing_item_never_sends_use(self):
        for rows, block in [([], lambda raw: None),
                            ([SimpleNamespace(item_code_idx='reward', claimed=False, raw={})], lambda raw: 'account_link_unknown'),
                            ([SimpleNamespace(item_code_idx='reward', claimed=False, raw={'renewFlag': 'Y'})], lambda raw: None),
                            ([SimpleNamespace(item_code_idx='reward', claimed=False, raw={'expFlag': 'Y'})], lambda raw: None)]:
            client = self.make_client(rows, block)
            client._request = AsyncMock()
            with self.subTest(rows=rows), self.assertRaises(ValueError):
                asyncio.run(client.get_item_detail_response('reward'))
            client._request.assert_not_awaited()

    def test_business_minus_one_is_not_auth_expiry_and_401_is(self):
        for status, expected in [(200, RuntimeError), (401, PermissionError)]:
            response = SimpleNamespace(status=status, json=AsyncMock(return_value={'result': -1}))

            class Context:
                async def __aenter__(self): return response
                async def __aexit__(self, *args): pass

            client = self.make_client([])
            client._session = SimpleNamespace(request=lambda *args, **kwargs: Context())
            with self.subTest(status=status), self.assertRaises(expected) as failure:
                asyncio.run(client._request('POST', 'get_drops_use_info.php', json_body={}))
            if status == 200:
                self.assertNotIsInstance(failure.exception, PermissionError)
