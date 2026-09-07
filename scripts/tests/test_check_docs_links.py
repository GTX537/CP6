"""Behavior checks for the documentation link checker; no external services."""

import importlib.util
import tempfile
import unittest
import subprocess
from pathlib import Path

SPEC = importlib.util.spec_from_file_location(
    'check_docs_links', Path(__file__).resolve().parents[1] / 'check-docs-links.py')
CHECKER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(CHECKER)


class DocsLinksTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        (self.root / 'docs').mkdir()
        (self.root / 'docs/Target.md').write_text('# Target', encoding='utf-8')
        (self.root / 'docs/图 (1).svg').write_text('<svg/>', encoding='utf-8')

    def check(self, text):
        (self.root / 'docs/README.md').write_text(text, encoding='utf-8')
        return CHECKER.check(self.root, ['docs/README.md'])

    def test_navigation_images_unicode_and_root_relative_paths(self):
        count, errors = self.check('[page](Target.md#title)\n![image](<图 (1).svg>)\n'
                                   '[encoded](%E5%9B%BE%20%281%29.svg)\n[root](/docs/Target.md)')
        self.assertEqual((count, errors), (4, []))

    def test_moved_document_with_stale_relative_link_fails(self):
        (self.root / 'docs/sub').mkdir()
        (self.root / 'docs/sub/README.md').write_text('[page](Target.md)', encoding='utf-8')
        count, errors = CHECKER.check(self.root, ['docs/sub/README.md'])
        self.assertEqual(count, 1)
        self.assertEqual(len(errors), 1)
        self.assertIn('docs/sub/Target.md', errors[0])

    def test_wrong_case_fails_on_windows_too(self):
        count, errors = self.check('[page](target.md)')
        self.assertEqual((count, len(errors)), (1, 1))

    def test_ignores_code_examples_external_urls_and_anchors(self):
        count, errors = self.check('```md\n[x](missing.md)\n```\n'
                                   '~~~\n[x](missing2.md)\n~~~\n'
                                   '`[x](missing3.md)` [web](https://example.invalid/a) '
                                   '[mail](mailto:a@example.invalid) [anchor](#heading)')
        self.assertEqual((count, errors), (0, []))

    def test_reference_definition_html_and_directory(self):
        count, errors = self.check('[page][ref]\n[ref]: Target.md "title"\n'
                                   '<img src="%E5%9B%BE%20%281%29.svg">\n[dir](../docs/)')
        self.assertEqual((count, errors), (3, []))

    def test_escape_and_missing_input_fail(self):
        count, errors = self.check('[escape](../../outside.md)')
        self.assertEqual((count, len(errors)), (1, 1))
        count, errors = CHECKER.check(self.root, ['docs/missing.md'])
        self.assertEqual((count, len(errors)), (0, 1))

    def test_parenthesized_prose_is_not_a_partial_markdown_link(self):
        count, errors = self.check('[Attribute](2 controllers exempt)\n'
                                   '[page](Target.md "A valid title")')
        self.assertEqual((count, errors), (1, []))

    def test_all_docs_includes_drafts_but_excludes_raw_and_ignored_assets(self):
        subprocess.run(['git', 'init', '-q', str(self.root)], check=True)
        (self.root / '.gitignore').write_text('docs/ignored/\n', encoding='utf-8')
        for directory in ('file', 'ignored'):
            (self.root / 'docs' / directory).mkdir()
            (self.root / 'docs' / directory / 'sample.md').write_text('[x](missing.md)', encoding='utf-8')
        self.assertEqual(CHECKER.doc_sources(self.root), ['docs/Target.md'])


if __name__ == '__main__':
    unittest.main()
