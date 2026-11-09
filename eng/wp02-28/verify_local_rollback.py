"""Assess the exact current-to-prior rollback observed in W347 using local evidence."""

import argparse
import json
from pathlib import Path
from subprocess import CalledProcessError

from verify_host import digest
from verify_service_token_pair import DRIVERS, RELEASES, verified_releases


SCHEMA = "CP6-WP02-28-LOCAL-ROLLBACK/1"
AUTHORITY = "LOCAL_EVIDENCE_ONLY"
W347_SHA256 = "11ec68897b08607832255eeff7d54e3f69895049cf88ef70acc59a5d0bff6ec5"
W347_RUNNER = "84cd5a3239b974088a7c432df996f6469a3f6e5d"
W347_ADOPTION = "3b3773bfd5d6a452ff25fbe67f5278c08f6c18a1"
DIRECTION = {"fromSource": RELEASES["current"][0], "toSource": RELEASES["prior"][0]}
REQUEST_FIELDS = {"direction", "database", "layout", "drivers"}
ARTIFACT_FIELDS = ("sourceSha", "receiptSha256", "entrySha256")


class Refused(RuntimeError):
    """A local input or compatibility check failed."""


def require(condition, reason):
    if not condition:
        raise Refused(reason)


def unique_object(pairs):
    value = dict(pairs)
    require(len(value) == len(pairs), "REQUEST_INVALID")
    return value


def validate_request(request):
    require(isinstance(request, dict) and request.keys() == REQUEST_FIELDS
            and all(isinstance(part, dict) for part in request.values()), "REQUEST_INVALID")
    require(request["direction"] == DIRECTION, "ROLLBACK_DIRECTION_MISMATCH")
    return request


def read_request(path):
    try:
        raw = path.read_bytes()
    except OSError as error:
        raise Refused("REQUEST_UNAVAILABLE") from error
    try:
        request = json.loads(raw, object_pairs_hook=unique_object)
    except (UnicodeError, json.JSONDecodeError) as error:
        raise Refused("REQUEST_INVALID") from error
    return validate_request(request)


def read_observation(path):
    try:
        raw = path.read_bytes()
    except OSError as error:
        raise Refused("OBSERVATION_UNAVAILABLE") from error
    # The pin was read back from the final, successful W347 run independently of the request.
    require(digest(raw) == W347_SHA256, "OBSERVATION_SHA_MISMATCH")
    return json.loads(raw)


def bind_observation(request, observed):
    releases = observed["releases"]
    drivers = {"dependencies": DRIVERS, "files": releases["current"]["driverFiles"]}
    require(observed["driverDeps"] == DRIVERS and releases["prior"]["driverFiles"] == drivers["files"]
            and request["drivers"] == drivers, "DRIVER_MISMATCH")

    physical = observed["database"]
    installation = observed["installation"]
    database = {
        "provider": "PostgreSql",
        "serverVersion": physical["serverVersion"],
        "port": physical["Port"],
        "name": physical["Database"],
        "oid": int(physical["Oid"]),
        "systemIdentifier": physical["SystemIdentifier"],
        "installationId": installation["InstallationId"],
    }
    require(database["serverVersion"] == "18.6" and installation["DatabaseOid"] == database["oid"]
            and installation["SystemIdentifier"] == database["systemIdentifier"]
            and all(type(request["database"].get(field)) is int for field in ("port", "oid"))
            and request["database"] == database, "DATABASE_IDENTITY_MISMATCH")

    catalog = observed["catalogAfter"]
    require(catalog == observed["catalogBefore"] and catalog["fingerprint"] == catalog["liveFingerprint"]
            and catalog["profile"] == installation["Profile"] == "service-token-v1"
            and catalog["installationId"] == installation["InstallationId"]
            and catalog["baselineSha256"] == installation["BaselineSha256"]
            and catalog["efHistoryTables"] == 0 and catalog["tokenRlsForced"] is True
            and catalog["defaultMayWriteToken"] is False, "LAYOUT_MISMATCH")
    layout = {
        "profile": catalog["profile"],
        "baselineSha256": catalog["baselineSha256"].lower(),
        "catalogFingerprint": catalog["fingerprint"].lower(),
        "efHistoryTables": catalog["efHistoryTables"],
    }
    require(type(request["layout"].get("efHistoryTables")) is int and request["layout"] == layout,
            "LAYOUT_MISMATCH")
    return {"direction": DIRECTION, "drivers": drivers, "database": database, "layout": layout}


def bind_releases(args, observed):
    try:
        actual = verified_releases(args)
    except (OSError, CalledProcessError) as error:
        raise Refused("RELEASE_INPUT_UNAVAILABLE") from error
    except RuntimeError as error:
        raise Refused("RELEASE_IDENTITY_MISMATCH") from error
    identity_fields = ARTIFACT_FIELDS + ("driverFiles",)
    require(all(actual[tag][key] == observed["releases"][tag][key]
                for tag in RELEASES for key in identity_fields), "RELEASE_OBSERVATION_MISMATCH")
    return actual


def verify_transition(observed):
    # The pinned W347 runner already checked business state and waits for current to stop before restarting prior.
    current = observed["processes"]["current"]
    prior = observed["processes"]["prior_rollback"]
    require(observed["completed"] is True and current["sourceSha"] == DIRECTION["fromSource"]
            and prior["sourceSha"] == DIRECTION["toSource"]
            and all(process["processStopped"] and process["listenerClosed"] for process in (current, prior))
            and observed["postgresStopped"] is True, "ROLLBACK_TRANSITION_MISMATCH")


def decision(scope, releases, observed):
    return {
        "schema": SCHEMA,
        "authority": AUTHORITY,
        "decision": "COMPATIBLE_FOR_OBSERVED_LOCAL_ROLLBACK",
        **scope,
        "artifacts": {
            "from": {key: releases["current"][key] for key in ARTIFACT_FIELDS},
            "to": {key: releases["prior"][key] for key in ARTIFACT_FIELDS},
        },
        "observation": {
            "runnerCommit": W347_RUNNER,
            "adoptedMain": W347_ADOPTION,
            "resultsSha256": W347_SHA256,
            "finishedUtc": observed["finishedUtc"],
            "reportPath": "docs/development/evidence/W347-01/DELIVERY.md",
            "releaseReceiptsPath": "docs/development/evidence/W345-01/DELIVERY.md",
        },
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("request", "evidence", "publish-root", "package-cache"):
        parser.add_argument("--" + name, required=True, type=Path)
    args = parser.parse_args()
    try:
        request = read_request(args.request)
        observed = read_observation(args.evidence)
        scope = bind_observation(request, observed)
        releases = bind_releases(args, observed)
        verify_transition(observed)
        result = decision(scope, releases, observed)
    except Refused as error:
        print(json.dumps({"schema": SCHEMA, "authority": AUTHORITY, "decision": "REFUSED", "reason": str(error)}))
        return 1
    print(json.dumps(result, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
