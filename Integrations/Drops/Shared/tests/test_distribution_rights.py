"""Public distribution must not infer upstream rights from publisher ownership."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('build_provenance', Path(__file__).resolve().parents[1]/'build_provenance.py')
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class DistributionRights(unittest.TestCase):
    def test_publisher_declaration_without_upstream_grant_is_blocked(self):
        record_path = Path(__file__).resolve().parents[2]/'soop/redistribution-authorization.json'
        record = json.loads(record_path.read_text('utf-8-sig'))
        core = {'commit': record['coreCommit'], 'sha256': record['coreSha256']}
        with self.assertRaisesRegex(RuntimeError, 'upstream'):
            module.authorize_core(core, record_path)
        self.assertNotIn('redistributionAuthorized', core)

    def test_review_cannot_authorize_a_different_core_commit(self):
        with tempfile.TemporaryDirectory() as root:
            path = Path(root)/'authorization.json'
            path.write_text(json.dumps({'upstreamRightsReview': {'status': 'confirmed', 'allowsFrozenBinaries': True,
                'coversCoreCommit': 'different', 'evidenceUrls': ['https://example.invalid/fixture'], 'rightsHolders': ['fixture']}}))
            with self.assertRaises(RuntimeError):
                module.authorize_core({'commit': 'actual', 'sha256': 'fixture'}, path)


if __name__ == '__main__':
    unittest.main()
