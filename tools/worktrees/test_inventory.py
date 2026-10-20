"""Safety behavior checks for the read-only inventory (no cleanup exercised)."""
import tempfile
import unittest
from pathlib import Path
import inventory

class InventorySafety(unittest.TestCase):
    def test_exact_repository_allowlist(self):
        for url in ['https://github.com/GTX537/CP6.git','git@github.com:GTX537/CP6.CRM.git','ssh://git@github.com/GTX537/CP6.Platform.git']:
            self.assertIsNotNone(inventory.repository(url))
        for url in ['https://github.com/Other/CP6.git','https://github.com.evil/GTX537/CP6.git','https://github.com/GTX537/CP6.CloudPackages.git','https://github.com/GTX537/CP6.git/extra']:
            self.assertIsNone(inventory.repository(url))

    def test_nested_checkout_not_double_counted_and_scan_leaves_files(self):
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder);(root/'file.txt').write_bytes(b'abc');child=root/'child';child.mkdir();(child/'file.txt').write_bytes(b'12345')
            result=inventory.size_scan(root,{inventory.norm(root),inventory.norm(child)})
            self.assertEqual(result['logical_bytes'],3)
            self.assertEqual(len(result['nested_roots']),1)
            self.assertEqual((child/'file.txt').read_bytes(),b'12345')

    def test_missing_directory_reports_error_instead_of_safe_empty(self):
        with tempfile.TemporaryDirectory() as folder:
            result=inventory.size_scan(Path(folder)/'absent',set())
            self.assertTrue(result['scan_errors'])
            self.assertEqual(result['logical_bytes'],0)

    def test_windows_extended_path_has_same_identity(self):
        if inventory.os.name=='nt':
            self.assertEqual(inventory.norm('D:/CP6-worktrees/example'),inventory.norm('\\\\?\\D:\\CP6-worktrees\\example'))

    def test_unregistered_source_copy_is_reported_without_assuming_ownership(self):
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder);copy=root/'old-copy';copy.mkdir();source=copy/'source.cs';source.write_bytes(b'original')
            registered=root/'retained';registered.mkdir();(registered/'file').write_bytes(b'kept')
            nested=root/'other-project'/'repo';nested.mkdir(parents=True);(nested/'.git').mkdir()
            rows=inventory.unregistered_candidates([root],[{'path':str(registered)}])
            self.assertEqual(len(rows),1)
            self.assertEqual(rows[0]['path'],str(copy))
            self.assertIsNone(rows[0]['repository'])
            self.assertEqual(rows[0]['logical_bytes'],8)
            self.assertEqual(source.read_bytes(),b'original')

if __name__=='__main__':unittest.main()
