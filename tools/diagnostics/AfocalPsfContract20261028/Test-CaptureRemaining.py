"""Offline regressions: missing native context never means a complete contract."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
spec = importlib.util.spec_from_file_location('capture_remaining', Path(__file__).with_name('Capture-Remaining.py'))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

class CaptureContextTests(unittest.TestCase):
    def assess(self, value=None, raw=None):
        with tempfile.TemporaryDirectory() as directory:
            p = Path(directory)
            if value is not None:
                (p/'afocal-nsc-context.json').write_text(json.dumps(value))
            if raw is not None:
                (p/'afocal-nsc-context.json').write_text(raw)
            return module.assess_native_context(p, 2)

    def test_missing_context_is_explicit(self):
        result = self.assess()
        self.assertFalse(result['evidenceComplete'])
        self.assertIn('context-file-missing', result['issues'])

    def test_malformed_context_is_explicit(self):
        self.assertIn('context-file-invalid', self.assess(raw='{')['issues'])

    def test_wrong_configuration_cannot_be_accepted(self):
        self.assertIn('configuration-mismatch', self.assess({'configuration':1,'sections':[]})['issues'])

    def test_empty_sections_are_not_zero_glue(self):
        result = self.assess({'configuration':2,'sections':[]})
        self.assertEqual([], result['glueCandidates'])
        self.assertIn('nsc-sections-missing', result['issues'])

    def test_getter_error_is_retained(self):
        result = self.assess({'configuration':2,'sections':[{'name':'NSC','error':'getter failed'}]})
        self.assertIn('nsc-section-error',result['issues'])

    def test_unavailable_or_boolean_is_not_numeric_glue(self):
        for value in ['Unavailable: no API', True, None, float('nan'), -1]:
            result = self.assess({'configuration':2,'sections':[{'name':'NSC','properties':{'GlueDistance':value}}]})
            self.assertEqual([], result['glueCandidates'])
            self.assertIn('glue-field-invalid-or-unavailable',result['issues'])

    def test_numeric_readback_does_not_prove_diagonal_or_edge_semantics(self):
        result = self.assess({'configuration':2,'completeNativeEdgeContract':True,
            'sections':[{'name':'NSC','properties':{'GlueDistance':1e-6}}]})
        self.assertEqual(1e-6,result['glueCandidates'][0]['value'])
        self.assertFalse(result['evidenceComplete'])
        self.assertIn('native-diagonal-and-edge-semantics-unverified',result['issues'])
        self.assertEqual(3,module.evidence_exit_code([result]))

    def test_frozen_record_preserves_tokens_without_guessing_their_meaning(self):
        line='NSCD 100 500 0 1.00E-03 5 1.00E-06 0 0'
        for raw in [line.encode(),line.encode('utf-16')]:
            result=module.raw_nsc_records(raw)
            self.assertEqual([line],result['records'])
            self.assertFalse(result['tokenSemanticsVerified'])
            self.assertNotIn('glueDistance',result)

if __name__=='__main__': unittest.main()
