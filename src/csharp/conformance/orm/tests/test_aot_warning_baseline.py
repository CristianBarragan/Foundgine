import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).resolve().parents[1] / "compare-aot-warning-baseline.py"

class BaselineTests(unittest.TestCase):
    def run_compare(self, warnings, baseline):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d); report=root/"report.json"; base=root/"baseline.json"; out=root/"out.json"
            report.write_text(json.dumps({"schemaVersion":4,"warnings":warnings}), encoding="utf-8")
            base.write_text(json.dumps(baseline), encoding="utf-8")
            result=subprocess.run([sys.executable,str(SCRIPT),str(report),str(base),str(out)],capture_output=True,text=True)
            return result, json.loads(out.read_text()) if out.exists() else None
    def baseline(self, entries): return {"schemaVersion":1,"warnings":entries}
    def warning(self, fp="abc", owner="foundgine", code="IL2026"):
        return {"fingerprint":fp,"owner":owner,"code":code,"line":"sample"}
    def entry(self, fp="abc"):
        return {"fingerprint":fp,"owner":"foundgine","code":"IL2026","rationale":"Reviewed legacy warning","reviewedBy":"maintainer"}
    def test_new_foundgine_blocking_warning_fails(self):
        r,p=self.run_compare([self.warning()],self.baseline([])); self.assertEqual(r.returncode,2); self.assertEqual(p["enforcement"]["blockingCount"],1)
    def test_reviewed_baseline_warning_is_allowed(self):
        r,p=self.run_compare([self.warning()],self.baseline([self.entry()])); self.assertEqual(r.returncode,0); self.assertEqual(len(p["existingWarnings"]),1)
    def test_resolved_warning_is_reported(self):
        r,p=self.run_compare([],self.baseline([self.entry()])); self.assertEqual(r.returncode,0); self.assertEqual(len(p["resolvedWarnings"]),1)
    def test_new_dependency_warning_is_tracked_but_not_blocking(self):
        r,p=self.run_compare([self.warning(owner="provider-or-dependency")],self.baseline([])); self.assertEqual(r.returncode,0); self.assertEqual(len(p["newWarnings"]),1)
    def test_missing_review_metadata_fails_closed(self):
        bad={"fingerprint":"abc","code":"IL2026","owner":"foundgine"}
        r,_=self.run_compare([],self.baseline([bad])); self.assertNotEqual(r.returncode,0)
    def test_duplicate_baseline_fingerprint_fails_closed(self):
        r,_=self.run_compare([],self.baseline([self.entry(),self.entry()])); self.assertNotEqual(r.returncode,0)

if __name__ == "__main__": unittest.main()
