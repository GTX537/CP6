import importlib.util
import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch


MODULE_PATH = Path(__file__).with_name("space_inventory.py")
SPEC = importlib.util.spec_from_file_location("space_inventory", str(MODULE_PATH))
inventory = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(inventory)


class SpaceInventoryTests(unittest.TestCase):
    def test_git_ref_reader_uses_frozen_blob_not_worktree_content(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "tracked.cs").write_text("worktree", encoding="utf-8")
            inventory._GIT_BLOB_CACHE.clear()

            with patch.object(
                inventory, "run_git_bytes", return_value=b"frozen"
            ) as git:
                content = inventory.read_text(
                    root, "tracked.cs", "deadbeef"
                )

        self.assertEqual("frozen", content)
        git.assert_called_once_with(
            root, ["show", "deadbeef:tracked.cs"]
        )

    def test_parse_csharp_endpoints_resolves_route_owner_and_permission(self):
        source = """
using Microsoft.AspNetCore.Mvc;
[ApiController]
[Authorize]
[Route("api/space/design/v1")]
public sealed class DemoController : ControllerBase
{
    [HttpGet("models/{id:guid}")]
    public Task<string> Get(Guid id) => Task.FromResult("");

    [HttpPost("models")]
    [RequirePermission("space:model", "edit")]
    public async Task<IActionResult> Create() => Ok();
}
"""
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            path = "CP6.WebApi/Controllers/Space/DemoController.cs"
            target = root / path
            target.parent.mkdir(parents=True)
            target.write_text(source, encoding="utf-8")
            endpoints = inventory.parse_csharp_endpoints(
                root, [path], "baseline"
            )

        self.assertEqual(2, len(endpoints))
        by_method = {item["method"]: item for item in endpoints}
        self.assertEqual(
            "/api/space/design/v1/models/{id:guid}", by_method["GET"]["route"]
        )
        self.assertIsNone(by_method["GET"]["permission"])
        self.assertEqual("space:model:edit", by_method["POST"]["permission"])
        self.assertTrue(all(item["authorized"] for item in endpoints))

    def test_endpoint_ownership_rejects_duplicate_method_and_route(self):
        endpoints = [
            {"method": "GET", "route": "/api/space/site", "owner": "A#Get"},
            {"method": "GET", "route": "/API/SPACE/SITE", "owner": "B#Get"},
        ]
        result = inventory.endpoint_ownership_check(endpoints, "baseline")
        self.assertFalse(result["passed"])
        self.assertEqual(1, len(result["duplicates"]))

    def test_endpoint_discovery_detects_unparsed_http_attribute(self):
        source = """
[Route("api/space/demo")]
public class DemoController
{
    [HttpGet("one")]
    public string One() => "";

    [HttpPost("two")]
    private string Hidden() => "";
}
"""
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            path = "DemoController.cs"
            (root / path).write_text(source, encoding="utf-8")
            endpoints = inventory.parse_csharp_endpoints(
                root, [path], "baseline"
            )
            result = inventory.endpoint_discovery_check(
                root, [path], endpoints, "baseline"
            )

        self.assertEqual(1, len(endpoints))
        self.assertFalse(result["passed"])
        self.assertEqual(2, result["httpAttributeCount"])

    def test_contract_statuses_cover_all_three_states(self):
        contracts = {
            "contracts": [
                {
                    "id": "implemented",
                    "name": "Implemented",
                    "rationale": "",
                    "evidence": [
                        {
                            "id": "one",
                            "source": "baseline",
                            "glob": "one.cs",
                            "description": "one",
                        }
                    ],
                },
                {
                    "id": "partial",
                    "name": "Partial",
                    "rationale": "",
                    "completeStatus": "Partial",
                    "evidence": [
                        {
                            "id": "one",
                            "source": "candidate",
                            "glob": "*.cs",
                            "description": "one",
                        },
                        {
                            "id": "two",
                            "source": "candidate",
                            "glob": "*.json",
                            "description": "two",
                        },
                    ],
                },
                {
                    "id": "missing",
                    "name": "Missing",
                    "rationale": "",
                    "evidence": [
                        {
                            "id": "none",
                            "source": "candidate",
                            "glob": "*.md",
                            "description": "none",
                        }
                    ],
                },
            ]
        }
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "one.cs").write_text("class One {}", encoding="utf-8")
            results = inventory.evaluate_contracts(
                contracts,
                {"baseline": root, "candidate": root},
                {"baseline": ["one.cs"], "candidate": ["one.cs"]},
            )

        self.assertEqual(
            ["Implemented", "Partial", "NotStarted"],
            [item["status"] for item in results],
        )

    def test_stable_json_is_repeatable_and_has_no_ascii_escaping(self):
        document = {"z": "空间", "a": [2, 1]}
        first = inventory.stable_json(document)
        second = inventory.stable_json(document)
        self.assertEqual(first, second)
        self.assertEqual(document, json.loads(first))
        self.assertIn("空间", first)
        self.assertTrue(first.index('"a"') < first.index('"z"'))

    def test_porcelain_parser_handles_normal_and_untracked_files(self):
        raw = b" M tracked.cs\0?? new.cs\0"
        self.assertEqual(
            [
                {"status": "??", "path": "new.cs"},
                {"status": " M", "path": "tracked.cs"},
            ],
            inventory.parse_porcelain_z(raw),
        )

    def test_table_inventory_excludes_non_space_tables(self):
        source = """
[Table("Space_Site")]
public class SpaceSite {}
modelBuilder.Entity<Other>().ToTable("Sys_User");
"""
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            path = "CP6.Core/EFDbContext/CP6Context.cs"
            target = root / path
            target.parent.mkdir(parents=True)
            target.write_text(source, encoding="utf-8")
            tables = inventory.parse_tables(root, [path], "baseline")

        self.assertEqual(["Space_Site"], [item["name"] for item in tables])
        self.assertEqual("Implemented", tables[0]["implementationStatus"])

    def test_candidate_test_projects_are_discovered(self):
        tests = inventory.discover_tests(
            [
                "CP6.Space.UnitTests/SpaceDomainStateTests.cs",
                "CP6.Space.Application/SpaceDesignService.cs",
            ],
            "candidate",
        )
        self.assertEqual(1, len(tests))
        self.assertEqual("Partial", tests[0]["implementationStatus"])


if __name__ == "__main__":
    unittest.main()
