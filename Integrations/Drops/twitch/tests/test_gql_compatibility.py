from __future__ import annotations

import asyncio
import logging
import sys
import unittest
from contextlib import asynccontextmanager
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch


TWITCH_DIR = Path(__file__).resolve().parents[1]
CORE_DIR = TWITCH_DIR / "core"
DROPS_DIR = TWITCH_DIR.parent
for path in (str(DROPS_DIR), str(TWITCH_DIR), str(CORE_DIR)):
    if path not in sys.path:
        sys.path.insert(0, path)

import headless_gui

sys.modules["gui"] = headless_gui

import constants
constants.LANG_PATH = CORE_DIR / "lang"
import twitch
from exceptions import GQLException


class TwitchGqlCompatibilityTests(unittest.TestCase):
    EXPECTED_QUERY_HASHES = {
        "GetStreamInfo": "198492e0857f6aedead9665c81c5a06d67b25b58034649687124083ff288597d",
        "ClaimCommunityPoints": "46aaeebe02c99afdf4fc97c7c0cba964124bf6b0af229395f1f6d1feed05b3d0",
        "ClaimDrop": "a455deea71bdc9015b78eb49f4acfbce8baa7ccbedd28e549bb025bd0f751930",
        "ChannelPointsContext": "374314de591e69925fce3ddc2bcf085796f56ebb8cad67a0daa3165c03adc345",
        "Inventory": "8337eb8541b314040b0edde0c09c5c7a2783ba1960aa9edfbf3bac16d0fec404",
        "CurrentDrop": "4d06b702d25d652afb9ef835d2a550031f1cf762b193523a92166f40ea3d142b",
        "Campaigns": "c16bb890cc8ce7647a96ee69cd313d423a378a3dedadf630a1017cde18975feb",
        "CampaignDetails": "039277bf98f3130929262cc7c6efd9c141ca3749cb6dca442fc8ead9a53f77c1",
        "AvailableDrops": "782dad0f032942260171d2d80a654f88bdd0c5a9dddc392e9bc92218a0f42d20",
        "PlaybackAccessToken": "ed230aa1e33e07eebb8928504583da78a5173989fadfb1ac94be06a04f3cdbe9",
        "GameDirectory": "86bcceb4e8b1a51256ff8eed8bd8aae4acacf80d737efe904f84f3aeadf8cafd",
        "SlugRedirect": "1f0300090caceec51f33c5e20647aceff9017f740f223c3c532ba6fa59f6b6cc",
        "NotificationsView": "e8e06193f8df73d04a1260df318585d1bd7a7bb447afa058e52095513f2bfa4f",
        "NotificationsList": "11cdb54a2706c2c0b2969769907675680f02a6e77d8afe79a749180ad16bfea6",
        "NotificationsDelete": "13d463c831f28ffe17dccf55b3148ed8b3edbbd0ebadd56352f1ff0160616816",
    }

    def test_persisted_query_hashes_match_upstream_22d0c613(self) -> None:
        actual = {
            name: query["extensions"]["persistedQuery"]["sha256Hash"]
            for name, query in constants.GQL_QUERIES.items()
        }
        self.assertEqual(actual, self.EXPECTED_QUERY_HASHES)

    def test_web_user_agent_matches_upstream_chrome_153(self) -> None:
        self.assertIn("Chrome/153.0.0.0", constants.ClientType.WEB.USER_AGENT)
        self.assertIn("Chrome/153.0.0.0", constants.ClientType.SMARTBOX.USER_AGENT)
        self.assertIn("Chrome/153.", constants.ClientType.MOBILE_WEB.USER_AGENT)

    @staticmethod
    def _client_with_responses(responses: list[dict[str, object]]):
        client = object.__new__(twitch.Twitch)
        client._qgl_limiter = asyncio.Lock()
        client._client_type = constants.ClientType.WEB
        request_count = 0

        async def get_auth():
            return SimpleNamespace(headers=lambda **kwargs: {})

        @asynccontextmanager
        async def request(method, url, **kwargs):
            nonlocal request_count
            index = min(request_count, len(responses) - 1)
            payload = responses[index]
            request_count += 1

            async def response_json():
                return payload

            yield SimpleNamespace(json=response_json)

        client.get_auth = get_auth
        client.request = request
        return client, lambda: request_count

    def test_request_cancelled_retries_with_backoff_then_fails_at_limit(self) -> None:
        response = {
            "errors": [{"message": "request cancelled"}],
            "extensions": {"operationName": "Inventory"},
        }
        client, request_count = self._client_with_responses(
            [response] * twitch.GQL_TRANSIENT_MAX_ATTEMPTS
        )

        async def scenario() -> None:
            sleep = AsyncMock()
            delays = [1.0, 2.0, 4.0, 8.0, 16.0]
            with patch.object(twitch, "ExponentialBackoff", return_value=iter(delays)), \
                 patch.object(twitch.asyncio, "sleep", sleep):
                with self.assertRaises(GQLException):
                    await client.gql_request(constants.GQL_QUERIES["Inventory"])
            self.assertEqual(request_count(), twitch.GQL_TRANSIENT_MAX_ATTEMPTS)
            self.assertEqual(
                [call.args[0] for call in sleep.await_args_list],
                delays[:-1],
            )

        asyncio.run(scenario())

    def test_persisted_query_not_found_is_diagnostic_and_not_transient(self) -> None:
        response = {
            "errors": [{"message": "PersistedQueryNotFound"}],
            "extensions": {"operationName": "ViewerDropsDashboard"},
        }
        client, request_count = self._client_with_responses([response, response])

        async def scenario() -> None:
            sleep = AsyncMock()
            with patch.object(twitch, "ExponentialBackoff", return_value=iter([1.0, 2.0])), \
                 patch.object(twitch.asyncio, "sleep", sleep), \
                 self.assertLogs("TwitchDrops", level=logging.ERROR) as captured:
                with self.assertRaises(GQLException):
                    await client.gql_request(constants.GQL_QUERIES["Campaigns"])
            self.assertEqual(request_count(), 2)
            sleep.assert_awaited_once_with(5)
            self.assertTrue(any(
                "Twitch GQL persisted query 已失效，可能需要更新 Twitch core"
                in line for line in captured.output
            ))

        asyncio.run(scenario())


if __name__ == "__main__":
    unittest.main()
