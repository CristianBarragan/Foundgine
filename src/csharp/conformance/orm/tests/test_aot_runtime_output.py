import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from importlib.machinery import SourceFileLoader
VERIFY = SourceFileLoader(
    'verify_aot_runtime_output',
    str(Path(__file__).resolve().parents[1] / 'verify-aot-runtime-output.py'),
).load_module()


class NativeAotRuntimeOutputTests(unittest.TestCase):
    def test_exact_success_marker_is_accepted(self):
        with tempfile.TemporaryDirectory() as directory:
            log = Path(directory) / 'runtime.log'
            log.write_text('startup\nEXPECTED PASS\n', encoding='utf-8')
            VERIFY.verify(log, 'EXPECTED PASS')

    def test_missing_marker_is_rejected_even_when_log_exists(self):
        with tempfile.TemporaryDirectory() as directory:
            log = Path(directory) / 'runtime.log'
            log.write_text('process exited without semantic assertion output\n', encoding='utf-8')
            with self.assertRaisesRegex(ValueError, 'success marker'):
                VERIFY.verify(log, 'EXPECTED PASS')

    def test_marker_must_be_a_complete_line(self):
        with tempfile.TemporaryDirectory() as directory:
            log = Path(directory) / 'runtime.log'
            log.write_text('prefix EXPECTED PASS suffix\n', encoding='utf-8')
            with self.assertRaisesRegex(ValueError, 'success marker'):
                VERIFY.verify(log, 'EXPECTED PASS')

    def test_missing_log_is_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaises(FileNotFoundError):
                VERIFY.verify(Path(directory) / 'missing.log', 'EXPECTED PASS')


    def test_efcore_workflow_requires_program_success_marker(self):
        root = Path(__file__).resolve().parents[5]
        workflow = (root / '.github/workflows/orm-conformance.yml').read_text(encoding='utf-8')
        program = (root / 'src/csharp/aot/Foundgine.Aot.EfCore/Program.cs').read_text(encoding='utf-8')
        marker = 'Foundgine EF Core Native AOT pipeline passed (efcore): tenant isolation + generated map + projection.'
        self.assertIn("--expected-marker '" + marker + "'", workflow)
        self.assertIn('Console.WriteLine($"Foundgine EF Core Native AOT pipeline passed ({FoundgineEfCore.ProviderName}): tenant isolation + generated map + projection.")', program)
        self.assertIn('tee artifacts/efcore-aot/runtime.log', workflow)

    def test_empty_expected_marker_is_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            log = Path(directory) / 'runtime.log'
            log.write_text('anything\n', encoding='utf-8')
            with self.assertRaisesRegex(ValueError, 'must not be empty'):
                VERIFY.verify(log, '  ')


if __name__ == '__main__':
    unittest.main()
