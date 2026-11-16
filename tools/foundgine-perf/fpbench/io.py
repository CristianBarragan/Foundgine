import json
def _wj(path, obj):
    with open(path, "w") as f: json.dump(obj, f, indent=1)
def _wt(path, text):
    with open(path, "w") as f: f.write(text)
def _rj(path):
    with open(path) as f: return json.load(f)
