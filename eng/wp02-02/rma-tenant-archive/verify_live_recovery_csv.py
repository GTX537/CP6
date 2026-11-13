"""Regression for the recovery boundary consuming large native bytea CSV fields.

These parser tests do not connect to PostgreSQL; the actual restore/rollback and
concurrency evidence is recorded separately in W526-01.
"""
from argparse import Namespace
from contextlib import redirect_stderr
import csv
import hashlib
import io
import json
import sys
import unittest
from unittest.mock import patch

sys.dont_write_bytecode = True
import live_recovery as live


class MemoryCsv:
    def __init__(self, content):
        self.content = content

    def open(self, **kwargs):
        return io.StringIO(self.content, newline="")


class NativeCsvRecoveryTests(unittest.TestCase):
    def test_large_native_payload_preserves_original_binding_and_restores_parser_limit(self):
        tenant = "565d8f3c-75a5-4e2a-bef1-e410c3fd4a10"
        event = dict(specversion="1.0", id="native-csv-boundary", source="urn:cp6:erp",
                     type="com.gtx537.erp.business-partner-sync-requested.v1", data={})
        payload = b" " * 70000 + json.dumps(event).encode("utf-8")
        digest = hashlib.sha256(payload).hexdigest()
        table = live.erp_delivery.COMMAND
        row = dict.fromkeys(table.columns, "")
        row.update(MessageId=event["id"], TenantId=tenant, EventType=event["type"],
                   AggregateId="855a8bfa-a575-4e2a-9c10-9ad4ef428792", AggregateVersion="1",
                   Payload="\\x" + payload.hex(), PayloadSha256=digest, Status="1", AttemptCount="1",
                   ConflictCount="0", RowVersion="\\x0000000000000003",
                   ReceivedAtUtc="2026-11-13 06:00:00+00", ProcessedAtUtc="2026-11-13 06:00:01+00")
        stream = io.StringIO(newline="")
        writer = csv.DictWriter(stream, fieldnames=table.columns)
        writer.writeheader()
        writer.writerow(row)
        native = MemoryCsv(stream.getvalue())
        previous = csv.field_size_limit(131072)
        try:
            with self.assertRaises(csv.Error):
                list(live.erp_delivery.archived_rows(native, table, tenant, 1))

            def consume(_):
                return list(live.erp_delivery.archived_rows(native, table, tenant, 1))

            with patch.object(live, "_execute", consume):
                rows = live.execute(Namespace())
            self.assertEqual(len(rows), 1)
            self.assertEqual(rows[0][0]["PayloadSha256"], digest)
            self.assertEqual(rows[0][0]["MessageId"], event["id"])
            self.assertEqual(csv.field_size_limit(), 131072)
        finally:
            csv.field_size_limit(previous)

    def test_malformed_csv_fails_explicitly_without_claiming_success(self):
        previous = csv.field_size_limit()

        def consume(_):
            return list(csv.reader(io.StringIO('"unterminated'), strict=True))

        error = io.StringIO()
        with patch.object(live, "_execute", consume), \
                patch("argparse.ArgumentParser.parse_args", return_value=Namespace(action="apply")), \
                redirect_stderr(error):
            result = live.main()
        self.assertEqual(result, 2)
        self.assertIn("unexpected end of data", error.getvalue())
        self.assertIn("No success is claimed", error.getvalue())
        self.assertEqual(csv.field_size_limit(), previous)


if __name__ == "__main__":
    unittest.main()
