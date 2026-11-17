"""Environment disclosure (full-disclosure requirement of the report)."""
import json, os, platform, shutil, subprocess

def _run(cmd):
    try:
        return subprocess.run(cmd, capture_output=True, text=True, timeout=10).stdout.strip()
    except Exception:
        return None

def _cpu_model():
    try:
        for line in open("/proc/cpuinfo"):
            if line.lower().startswith("model name"):
                return line.split(":", 1)[1].strip()
    except OSError:
        pass
    return platform.processor() or None

def _mem_gb():
    try:
        for line in open("/proc/meminfo"):
            if line.startswith("MemTotal"):
                return round(int(line.split()[1]) / 1024 / 1024, 2)
    except OSError:
        return None

def collect(extra=None):
    env = {
        "os": platform.platform(), "kernel": platform.release(),
        "cpu_model": _cpu_model(), "logical_cores": os.cpu_count(), "ram_gb": _mem_gb(),
        "python": platform.python_version(),
        "tools": {t: _run([t, "--version"]) for t in ("dotnet", "docker", "java", "psql") if shutil.which(t)},
        "git_commit": _run(["git", "rev-parse", "HEAD"]),
        "wsl": "microsoft" in platform.release().lower(),
        "cpu_governor": None,
    }
    try:
        env["cpu_governor"] = open("/sys/devices/system/cpu/cpu0/cpufreq/scaling_governor").read().strip()
    except OSError:
        pass
    if extra:
        env.update(extra)
    missing = [k for k in ("cpu_model", "logical_cores", "ram_gb", "os") if not env.get(k)]
    env["disclosure_complete"] = not missing
    env["missing"] = missing
    return env

if __name__ == "__main__":
    print(json.dumps(collect(), indent=2))
