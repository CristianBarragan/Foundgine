"""Resource sampler. Runs on the HOST (never inside the load-generator container),
which is the root cause of the all-zero metrics in the existing reports: the C# load
generator shells out to `docker stats` from inside its own container.

A cell is INVALID if a declared resource produced zero samples. No placeholder zeros.

Resource spec (per target):
  {"kind":"pid","pids":[123]}                      /proc based, no dependencies
  {"kind":"cgroup","path":"/sys/fs/cgroup/xyz"}    cgroup v2 cpu.stat + memory.current
  {"kind":"docker","containers":["name"]}          `docker stats --no-stream`, parsed in bytes
"""
import asyncio, os, re, subprocess, time

_UNITS = {"b": 1, "kb": 1e3, "mb": 1e6, "gb": 1e9, "kib": 1024, "mib": 1024**2, "gib": 1024**3}

def parse_bytes(text):
    m = re.match(r"\s*([\d.]+)\s*([A-Za-z]+)", text)
    if not m:
        raise ValueError(f"cannot parse size {text!r}")
    return float(m.group(1)) * _UNITS[m.group(2).lower()]

def _pid_cpu_ticks(pid):
    with open(f"/proc/{pid}/stat") as fh:
        f = fh.read().rsplit(")", 1)[1].split()
    return int(f[11]) + int(f[12])  # utime + stime

def _pid_rss(pid):
    with open(f"/proc/{pid}/status") as fh:
        for line in fh:
            if line.startswith("VmRSS"):
                return int(line.split()[1]) * 1024
    return 0

def _downsample(xs, n):
    """Mean over equal strides so a 24 h series stays small; keeps the first and last behaviour visible."""
    if len(xs) <= n: return [round(x, 3) for x in xs]
    k = len(xs) / n
    return [round(sum(xs[int(i * k):int((i + 1) * k)]) / max(1, len(xs[int(i * k):int((i + 1) * k)])), 3) for i in range(n)]

class Sampler:
    def __init__(self, spec, interval=1.0):
        self.spec, self.interval = spec or {}, interval
        self.cpu, self.mem, self.errors = [], [], []
        self._task = None
        self.hz = os.sysconf("SC_CLK_TCK")

    def _read(self):
        k = self.spec.get("kind")
        if k == "pid":
            ticks = sum(_pid_cpu_ticks(p) for p in self.spec["pids"])
            return ticks / self.hz, sum(_pid_rss(p) for p in self.spec["pids"])
        if k == "cgroup":
            p = self.spec["path"]
            with open(f"{p}/cpu.stat") as fh:
                usec = next(int(l.split()[1]) for l in fh if l.startswith("usage_usec"))
            with open(f"{p}/memory.current") as fh:
                mem = int(fh.read())
            return usec / 1e6, mem
        if k == "docker":
            out = subprocess.run(["docker", "stats", "--no-stream", "--format", "{{.CPUPerc}};{{.MemUsage}}",
                                  *self.spec["containers"]], capture_output=True, text=True, timeout=15)
            if out.returncode or not out.stdout.strip():
                raise RuntimeError(out.stderr.strip() or "empty docker stats output")
            cpu = mem = 0.0
            for line in out.stdout.strip().splitlines():
                c, m = line.split(";", 1)
                cpu += float(c.strip().rstrip("%")) / 100.0
                mem += parse_bytes(m.split("/")[0])
            return ("pct", cpu), mem
        raise ValueError(f"unknown resource kind {k!r}")

    async def _loop(self):
        prev_cpu, prev_t = None, None
        while True:
            try:
                cpu, mem = await asyncio.to_thread(self._read)
                now = time.perf_counter()
                if isinstance(cpu, tuple):               # docker already gives a rate
                    self.cpu.append(cpu[1])
                elif prev_cpu is not None:
                    self.cpu.append((cpu - prev_cpu) / (now - prev_t))   # cores used
                prev_cpu, prev_t = (cpu if not isinstance(cpu, tuple) else None), now
                self.mem.append(mem)
            except Exception as e:                       # never swallow silently
                self.errors.append(repr(e))
            await asyncio.sleep(self.interval)

    def start(self):
        if self.spec:
            self._task = asyncio.create_task(self._loop())

    async def stop(self):
        if self._task:
            self._task.cancel()
            try: await self._task
            except asyncio.CancelledError: pass

    def summary(self):
        if not self.spec:
            return {"declared": False}
        valid = len(self.cpu) > 0 and len(self.mem) > 0
        s = {"declared": True, "valid": valid, "samples": len(self.mem), "errors": self.errors[:3]}
        if valid:
            s.update(cpu_cores_mean=sum(self.cpu) / len(self.cpu), cpu_cores_max=max(self.cpu),
                     rss_mb_mean=sum(self.mem) / len(self.mem) / 1e6, rss_mb_max=max(self.mem) / 1e6)
            s["interval_s"] = self.interval
            s["rss_mb_series"] = _downsample([m / 1e6 for m in self.mem], 720)   # for soak / leak analysis
        return s
