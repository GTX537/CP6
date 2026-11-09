"""Local compatibility refusal tests. The real W347 observation is exercised by the CLI."""

from copy import deepcopy
from pathlib import Path
from types import SimpleNamespace
import unittest
from unittest.mock import patch

from verify_local_rollback import (
    Refused, bind_observation, bind_releases, read_observation, validate_request, verify_transition,
)
from verify_service_token_pair import DRIVERS, RELEASES


class LocalRollbackTests(unittest.TestCase):
    def setUp(self):
        files = {"Npgsql.dll": "a" * 64, "Npgsql.EntityFrameworkCore.PostgreSQL.dll": "b" * 64}
        direction = {"fromSource": RELEASES["current"][0], "toSource": RELEASES["prior"][0]}
        self.request = {
            "direction": direction,
            "drivers": {"dependencies": DRIVERS, "files": files},
            "database": {"provider": "PostgreSql", "serverVersion": "18.6", "port": 55438,
                         "name": "isolated", "oid": 17, "systemIdentifier": "1987",
                         "installationId": "12121212-1212-1212-1212-121212121212"},
            "layout": {"profile": "service-token-v1", "baselineSha256": "c" * 64,
                       "catalogFingerprint": "d" * 64, "efHistoryTables": 0},
        }
        catalog = {"profile": "service-token-v1", "installationId": self.request["database"]["installationId"],
                   "baselineSha256": "C" * 64, "fingerprint": "D" * 64, "liveFingerprint": "D" * 64,
                   "efHistoryTables": 0, "tokenRlsForced": True, "defaultMayWriteToken": False}
        self.observed = {
            "database": {"serverVersion": "18.6", "Port": 55438, "Database": "isolated",
                         "Oid": "17", "SystemIdentifier": "1987"},
            "installation": {"Profile": "service-token-v1", "InstallationId": catalog["installationId"],
                             "BaselineSha256": "C" * 64, "DatabaseOid": 17, "SystemIdentifier": "1987"},
            "catalogBefore": dict(catalog),
            "catalogAfter": dict(catalog),
            "driverDeps": dict(DRIVERS),
            "releases": {tag: {"sourceSha": source, "receiptSha256": pin,
                               "entrySha256": tag, "driverFiles": dict(files)}
                         for tag, (source, pin) in RELEASES.items()},
        }

    def assert_refused(self, reason, action, *args):
        with self.assertRaisesRegex(Refused, f"^{reason}$"):
            action(*args)

    def test_missing_observation_and_unpinned_bytes_are_refused(self):
        file = Path(__file__)
        self.assert_refused("OBSERVATION_UNAVAILABLE", read_observation, file.with_suffix(".missing"))
        self.assert_refused("OBSERVATION_SHA_MISMATCH", read_observation, file)

    def test_only_current_to_prior_direction_is_recognized(self):
        known = self.request["direction"]
        for direction in (
            {"fromSource": known["toSource"], "toSource": known["fromSource"]},
            {"fromSource": known["fromSource"], "toSource": known["fromSource"]},
            {"fromSource": "0" * 40, "toSource": known["toSource"]},
        ):
            with self.subTest(direction=direction):
                self.assert_refused("ROLLBACK_DIRECTION_MISMATCH", validate_request,
                                    self.request | {"direction": direction})

    def test_driver_database_and_layout_requests_must_match_actual_observation(self):
        self.assertEqual(self.request, bind_observation(self.request, self.observed))
        for field, key, bad, reason in (
            ("drivers", "files", {"Npgsql.dll": "e" * 64}, "DRIVER_MISMATCH"),
            ("drivers", "dependencies", {"Npgsql": "8.0.9"}, "DRIVER_MISMATCH"),
            ("database", "oid", 18, "DATABASE_IDENTITY_MISMATCH"),
            ("database", "oid", 17.0, "DATABASE_IDENTITY_MISMATCH"),
            ("database", "installationId", "other-installation", "DATABASE_IDENTITY_MISMATCH"),
            ("layout", "profile", "identity-directory-v2", "LAYOUT_MISMATCH"),
            ("layout", "catalogFingerprint", "e" * 64, "LAYOUT_MISMATCH"),
            ("layout", "efHistoryTables", 1, "LAYOUT_MISMATCH"),
            ("layout", "efHistoryTables", False, "LAYOUT_MISMATCH"),
        ):
            with self.subTest(field=field, key=key):
                request = deepcopy(self.request)
                request[field][key] = bad
                self.assert_refused(reason, bind_observation, request, self.observed)

    def test_changed_live_catalog_and_installation_tuple_are_refused(self):
        self.observed["catalogAfter"]["liveFingerprint"] = "E" * 64
        self.assert_refused("LAYOUT_MISMATCH", bind_observation, self.request, self.observed)
        self.observed["catalogAfter"]["liveFingerprint"] = "D" * 64
        self.observed["installation"]["DatabaseOid"] = 18
        self.assert_refused("DATABASE_IDENTITY_MISMATCH", bind_observation, self.request, self.observed)

    def test_exact_verified_release_identities_must_match_the_observation(self):
        verified = deepcopy(self.observed["releases"])
        verified["current"]["directory"] = "verified-input-location"
        self.observed["releases"]["current"]["directory"] = "original-observation-location"
        with patch("verify_local_rollback.verified_releases", return_value=verified):
            self.assertEqual(verified, bind_releases(SimpleNamespace(), self.observed))
            for key in ("sourceSha", "entrySha256", "receiptSha256", "driverFiles"):
                with self.subTest(key=key):
                    other = deepcopy(self.observed)
                    other["releases"]["current"][key] = "different"
                    self.assert_refused("RELEASE_OBSERVATION_MISMATCH", bind_releases, SimpleNamespace(), other)

    def test_recorded_completion_requires_the_exact_stopped_pair(self):
        evidence = {
            "completed": True, "postgresStopped": True,
            "processes": {
                "current": {"sourceSha": RELEASES["current"][0], "processStopped": True, "listenerClosed": True},
                "prior_rollback": {"sourceSha": RELEASES["prior"][0], "processStopped": True, "listenerClosed": True},
            },
        }
        verify_transition(evidence)
        current = evidence["processes"]["current"]
        self.assert_refused("ROLLBACK_TRANSITION_MISMATCH", verify_transition,
                            evidence | {"completed": False})
        for key, wrong in (("processStopped", False), ("sourceSha", RELEASES["prior"][0])):
            with self.subTest(key=key):
                changed = evidence["processes"] | {"current": current | {key: wrong}}
                self.assert_refused("ROLLBACK_TRANSITION_MISMATCH", verify_transition,
                                    evidence | {"processes": changed})

    def test_failed_receipt_preflight_cannot_return_compatibility(self):
        with patch("verify_local_rollback.verified_releases", side_effect=RuntimeError("Publish receipt pin mismatch")):
            self.assert_refused("RELEASE_IDENTITY_MISMATCH", bind_releases, SimpleNamespace(), self.observed)


if __name__ == "__main__":
    unittest.main()
