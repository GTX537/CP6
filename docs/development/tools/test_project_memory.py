"""验证状态同步可重跑，且不会覆盖历史记录或损坏的摘要边界。"""
from pathlib import Path
from tempfile import TemporaryDirectory
import unittest

import project_memory as memory


class ProjectMemoryTests(unittest.TestCase):
    def setUp(self):
        directory = TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        self.root = Path(directory.name)
        self.data = {
            "dispatch_policy": {"card_execution_policy": {"closed_cards": ["A"], "next_card": "B"}},
            "targets": [{"id": "T", "name": "测试辅助", "delivery_state": "DONE",
                         "completion_audit": {"report": "docs/development/evidence/A.md", "classification": "TEST_ONLY"}}],
            "implementation_tasks": [
                {"id": "A", "title": "来源核对", "card_execution": {"result": "SPLIT", "new_splits": ["B"],
                 "report": "docs/development/evidence/A.md", "verified_at": "2026-11-17T10:00:00Z"}},
                {"id": "B", "title": "余项", "plan_path": "docs/development/task-plans/WP02.md", "dependencies": ["A"]},
            ]}
        self.counts = {"original_total": 2, "original_done": 1, "fully_done": 0}
        self.history = b"\r\n" + "历史原文".encode() + b"\n\n"
        for name in memory.sections(self.data, self.counts):
            (self.root / name).write_bytes(b"\xef\xbb\xbf# Title\r\n" + self.history)

    def sync(self, *, write):
        return memory.synchronize(self.root, self.data, self.counts, write=write)

    def test_history_and_retry(self):
        self.assertEqual(len(self.sync(write=False)), 4)
        self.assertEqual(self.sync(write=True), [])
        before = {p.name: p.read_bytes() for p in self.root.iterdir()}
        for content in before.values():
            self.assertTrue(content.startswith(b"\xef\xbb\xbf# Title\r\n"))
            self.assertTrue(content.endswith(self.history))
        self.assertIn("完整 DONE 0/2", before["PROJECT_STATE.md"].decode())
        self.assertIn("测试支撑", before["05-Completed.md"].decode())
        self.assertIn("[B 余项]", before["06-Todo.md"].decode())
        self.assertIn("拆卡，余项 B", before["CHANGELOG-AI.md"].decode())
        self.assertEqual(self.sync(write=True), [])
        self.assertEqual(before, {p.name: p.read_bytes() for p in self.root.iterdir()})

        self.data["dispatch_policy"]["card_execution_policy"]["next_card"] = None
        self.assertEqual(len(self.sync(write=False)), 1)
        self.assertEqual(self.sync(write=True), [])
        self.assertIn("尚未指定", (self.root / "06-Todo.md").read_text(encoding="utf-8"))
        self.assertEqual(self.sync(write=False), [])

    def test_incomplete_marker_leaves_all_pages_intact(self):
        path = self.root / "CHANGELOG-AI.md"
        path.write_bytes(path.read_bytes() + memory.START)
        before = {p.name: p.read_bytes() for p in self.root.iterdir()}
        self.assertIn("标记", self.sync(write=True)[0])
        self.assertEqual(before, {p.name: p.read_bytes() for p in self.root.iterdir()})

    def test_legacy_ledger_has_no_generated_sections(self):
        self.data["dispatch_policy"].clear()
        self.assertEqual(self.sync(write=False), [])
