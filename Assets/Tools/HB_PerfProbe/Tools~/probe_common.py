"""HB_PerfProbe PC 스크립트 공통: adb 찾기 · 명령 소켓 · logcat 줄 해석 · 판정 규칙 · 이벤트 기록.

표준 라이브러리만 쓴다.
"""

import glob
import json
import math
import os
import re
import shutil
import socket
import subprocess
import sys
import time
from collections import deque

LOGCAT_TAG = "HBProbe"
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))

DEFAULT_CONFIG = {
    "port": 34999,
    "stale_sec": 15,
    "serial": None,
    "out_dir": "HBProbe_out",
    "pull": True,
    "drop_after_pull": True,
    "baseline_census": True,
    "print_every": 10,
    "thermal_poll_sec": 10,
    "post_capture_ignore_sec": 5,
    "cooldown_sec": 120,
    "leak": {
        "enabled": True,
        "metric": "totalUsed",
        "window_sec": 180,
        "min_samples": 30,
        "slope_kb_per_sec": 64,
        "min_r2": 0.8,
    },
    "overload": {
        "enabled": True,
        "frame_budget_ms": 33.3,
        "sustain_sec": 5,
    },
}


# ---------------------------------------------------------------- 설정

def load_config(path=None):
    """기본값 위에 설정 파일(JSON)을 덮는다. path가 없으면 스크립트 옆 probe_config.json이 있을 때만 읽는다."""
    config = json.loads(json.dumps(DEFAULT_CONFIG))
    if path is None:
        candidate = os.path.join(SCRIPT_DIR, "probe_config.json")
        path = candidate if os.path.exists(candidate) else None
    if path:
        with open(path, encoding="utf-8") as f:
            _merge(config, json.load(f))
    return config


def _merge(base, override):
    for key, value in override.items():
        if isinstance(value, dict) and isinstance(base.get(key), dict):
            _merge(base[key], value)
        else:
            base[key] = value


# ---------------------------------------------------------------- adb

def _version_key(path):
    m = re.search(r"[\\/](\d+)\.(\d+)\.(\d+)([a-z])(\d+)[\\/]", path)
    if not m:
        return (0, 0, 0, "", 0)
    return (int(m.group(1)), int(m.group(2)), int(m.group(3)), m.group(4), int(m.group(5)))


def _which_adb(name):
    """PATH에서 adb를 찾는다. Windows에서는 PATH 항목만 보고 adb.exe만 받는다 —
    shutil.which는 현재 폴더를 먼저 보고 .bat · .cmd도 받아서, 믿을 수 없는 폴더에서 실행하면 그 파일이 돌 수 있다."""
    if os.name != "nt":
        return shutil.which(name)
    for folder in os.environ.get("PATH", "").split(os.pathsep):
        folder = folder.strip().strip('"')
        if not folder or not os.path.isabs(folder):
            continue
        candidate = os.path.join(folder, name + ".exe")
        if os.path.isfile(candidate):
            return candidate
    return None


def find_adb(environ=None, which=_which_adb, unity_roots=None):
    """adb 경로. 순서: 환경 변수 HB_ADB → PATH → ANDROID_HOME/ANDROID_SDK_ROOT → Unity 설치에 든 adb(가장 새 버전). 없으면 None."""
    environ = os.environ if environ is None else environ
    exe = "adb.exe" if os.name == "nt" else "adb"

    explicit = environ.get("HB_ADB")
    if explicit and os.path.isfile(explicit):
        return explicit

    on_path = which("adb")
    if on_path:
        return on_path

    for var in ("ANDROID_HOME", "ANDROID_SDK_ROOT"):
        sdk = environ.get(var)
        if sdk:
            candidate = os.path.join(sdk, "platform-tools", exe)
            if os.path.isfile(candidate):
                return candidate

    if unity_roots is None:
        unity_roots = [
            r"C:\Program Files\Unity\Hub\Editor\*\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools",
            "/Applications/Unity/Hub/Editor/*/PlaybackEngines/AndroidPlayer/SDK/platform-tools",
        ]
    found = []
    for pattern in unity_roots:
        found.extend(p for p in glob.glob(os.path.join(pattern, exe)) if os.path.isfile(p))
    if found:
        found.sort(key=_version_key)
        return found[-1]
    return None


class AdbError(RuntimeError):
    pass


class Adb:
    def __init__(self, path, serial=None):
        self.path = path
        self.serial = serial

    def _base(self):
        cmd = [self.path]
        if self.serial:
            cmd += ["-s", self.serial]
        return cmd

    def run(self, *args, timeout=60):
        try:
            result = subprocess.run(self._base() + list(args), capture_output=True, text=True,
                                    encoding="utf-8", errors="replace", timeout=timeout)
        except subprocess.TimeoutExpired:
            raise AdbError(f"adb {' '.join(args)} timed out after {timeout}s")
        if result.returncode != 0:
            raise AdbError(f"adb {' '.join(args)} failed ({result.returncode}): {result.stderr.strip() or result.stdout.strip()}")
        return result.stdout

    def popen(self, *args):
        return subprocess.Popen(self._base() + list(args), stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                                text=True, encoding="utf-8", errors="replace", bufsize=1)

    def devices(self):
        """연결돼 있고 권한이 있는(device 상태) 기기 serial 목록."""
        try:
            out = subprocess.run([self.path, "devices"], capture_output=True, text=True, timeout=30).stdout
        except subprocess.TimeoutExpired:
            raise AdbError("adb devices timed out")
        return parse_devices(out)

    def ensure_device(self):
        devices = self.devices()
        if self.serial:
            if self.serial not in devices:
                raise AdbError(f"device {self.serial} not connected (connected: {devices or 'none'})")
            return self.serial
        if not devices:
            raise AdbError("no device connected (adb devices is empty or unauthorized)")
        if len(devices) > 1:
            raise AdbError(f"several devices connected, pass --serial: {devices}")
        self.serial = devices[0]
        return self.serial

    def forward(self, port):
        self.run("forward", f"tcp:{port}", f"tcp:{port}")

    def pull(self, remote, local_parent):
        ensure_out_dir(os.path.dirname(os.path.abspath(local_parent)))
        os.makedirs(local_parent, exist_ok=True)
        self.run("pull", remote, local_parent, timeout=600)
        return os.path.join(local_parent, remote.rstrip("/").split("/")[-1])


def parse_devices(text):
    serials = []
    for line in text.splitlines()[1:]:
        parts = line.split()
        if len(parts) >= 2 and parts[1] == "device":
            serials.append(parts[0])
    return serials


# ---------------------------------------------------------------- 명령 소켓

MAX_REPLY_BYTES = 1 << 20
# 기기 쪽 snapshot 대기 상한(240초) · 서버 답 대기(300초)보다 길게.
REPLY_TIMEOUT = 330


def send_command(port, line, token=None, timeout=REPLY_TIMEOUT):
    """한 줄 보내고 한 줄(JSON) 받는다. 연결 하나에 명령 하나다. token이 있으면 줄 끝에 붙인다."""
    if token is not None:
        line = f"{line} token={check_safe(token, 'token')}"
    with socket.create_connection(("127.0.0.1", port), timeout=10) as sock:
        sock.settimeout(timeout)
        sock.sendall((line + "\n").encode("utf-8"))
        buf = b""
        while not buf.endswith(b"\n"):
            chunk = sock.recv(65536)
            if not chunk:
                break
            buf += chunk
            if len(buf) > MAX_REPLY_BYTES:
                raise ConnectionError(f"reply longer than {MAX_REPLY_BYTES} bytes")
    text = buf.decode("utf-8", errors="replace").strip()
    if not text:
        raise ConnectionError("no reply (is the probe running and adb forward set?)")
    reply = json.loads(text)
    if not isinstance(reply, dict):
        raise ValueError("reply is not a JSON object")
    return reply


SAFE_VALUE = re.compile(r"^[A-Za-z0-9_\-]([A-Za-z0-9_.\-]*[A-Za-z0-9_\-])?$")
MAX_REASON = 40


def check_safe(value, what, max_len=64):
    """기기 쪽 ProbeCommand.IsSafeValue와 같은 규칙: 영문 · 숫자 · _ - . , 처음 · 끝은 . 아님."""
    if not isinstance(value, str) or len(value) > max_len or not SAFE_VALUE.match(value):
        raise ValueError(f"{what} '{value}': only A-Z a-z 0-9 _ - . (not first or last '.'), up to {max_len} chars")
    return value


# ---------------------------------------------------------------- logcat · thermal

# brief 형식은 태그를 8칸으로 채우므로 "I/HBProbe ( 1234): " 처럼 괄호 앞에 빈칸이 올 수 있다.
LOGCAT_PID_RE = re.compile(r"^\w/" + LOGCAT_TAG + r"\s*\(\s*(\d+)\)")
NUMBER_KEYS = ("t", "frames", "fps", "frameAvg", "frameMax", "mainAvg", "mainMax", "gcAlloc", "draw", "setPass",
               "gcUsed", "totalUsed", "totalReserved", "systemUsed")


def _is_number(v):
    return isinstance(v, (int, float)) and not isinstance(v, bool)


def parse_logcat_line(line):
    """`logcat -v brief`의 HBProbe 줄에서 JSON 요약을 꺼낸다. 형식 · 타입이 안 맞으면 None.

    다른 앱도 같은 태그로 쓸 수 있으므로 값을 믿지 않고 검사한다. 줄의 pid는 "_pid"로 붙인다."""
    m = LOGCAT_PID_RE.match(line.strip())
    if not m:
        return None
    start = line.find("{")
    if start < 0:
        return None
    try:
        data = json.loads(line[start:].strip())
    except ValueError:
        return None
    if not isinstance(data, dict) or not isinstance(data.get("seq"), int) or isinstance(data.get("seq"), bool):
        return None
    for key in NUMBER_KEYS:
        if key in data and data[key] is not None and not _is_number(data[key]):
            return None
    if "t" not in data or not _is_number(data["t"]):
        return None
    if "marks" in data and not isinstance(data["marks"], str):
        return None
    if "token" in data and not isinstance(data["token"], str):
        return None
    data["_pid"] = int(m.group(1))
    return data


def discover(adb, port):
    """기기 logcat에 남은 HBProbe 줄에서 토큰을 찾아 서버에 확인받는다. (token, pid, status) 또는 None.

    가짜 줄의 토큰은 서버가 거절하고, 서버가 알려 준 pid가 그 줄의 pid와 같아야 받는다 — 이후 logcat은 이 pid로만 거른다."""
    out = adb.run("logcat", "-d", "-v", "brief", "-s", f"{LOGCAT_TAG}:I", timeout=30)
    tried = set()
    for line in reversed(out.splitlines()):
        data = parse_logcat_line(line)
        if not data or not data.get("token"):
            continue
        key = (data["token"], data["_pid"])
        if key in tried:
            continue
        tried.add(key)
        try:
            status = send_command(port, "status", data["token"], timeout=10)
        except (OSError, ValueError):
            continue
        if status.get("ok") and status.get("pid") == data["_pid"]:
            return data["token"], data["_pid"], status
    return None


THERMAL_RE = re.compile(r"Thermal Status:\s*(\d+)")


def parse_thermal_status(text):
    m = THERMAL_RE.search(text)
    return int(m.group(1)) if m else None


# ---------------------------------------------------------------- 판정 규칙

def linear_fit(points):
    """최소제곱 직선. (기울기, R²). 점이 2개 미만이거나 x가 모두 같으면 (0, 0)."""
    n = len(points)
    if n < 2:
        return 0.0, 0.0
    mx = sum(p[0] for p in points) / n
    my = sum(p[1] for p in points) / n
    sxx = sum((p[0] - mx) ** 2 for p in points)
    syy = sum((p[1] - my) ** 2 for p in points)
    sxy = sum((p[0] - mx) * (p[1] - my) for p in points)
    if sxx == 0:
        return 0.0, 0.0
    slope = sxy / sxx
    r2 = (sxy * sxy) / (sxx * syy) if syy > 0 else 0.0
    return slope, r2


class Rules:
    """요약을 하나씩 받아 캡처할 일을 돌려준다. 시각은 요약의 t(기기 Time.realtimeSinceStartup, 초)를 쓴다.

    - 릭: window_sec 동안 metric(기본 totalUsed)의 직선 기울기가 slope_kb_per_sec 이상이고 R²가 min_r2 이상 → census
    - 과부하: frameAvg가 frame_budget_ms를 넘는 구간이 sustain_sec 이상 이어짐 → capture
    - 같은 사유는 cooldown_sec 동안 다시 내지 않는다. 캡처 직후 post_capture_ignore_sec 동안의 요약은 판정에서 뺀다.
    - seq나 t가 줄면 앱이 다시 켜진 것으로 보고 상태를 비운다.
    """

    def __init__(self, config):
        self.config = config
        self.leak = config["leak"]
        self.overload = config["overload"]
        self.reset()

    def reset(self):
        self.window = deque()
        self.over_since = None
        self.last_fired = {}
        self.ignore_until = -math.inf
        self.last_seq = None
        self.last_t = None

    def notify_capture(self, t):
        self.ignore_until = t + self.config["post_capture_ignore_sec"]
        self.over_since = None

    def feed(self, s):
        t = s.get("t")
        seq = s.get("seq")
        if t is None or seq is None:
            return []
        if self.last_seq is not None and (seq <= self.last_seq or t < self.last_t):
            self.reset()
        self.last_seq, self.last_t = seq, t
        if t < self.ignore_until:
            return []

        actions = []
        if self.leak.get("enabled", True):
            action = self._leak(s, t)
            if action:
                actions.append(action)
        if self.overload.get("enabled", True):
            action = self._overload(s, t)
            if action:
                actions.append(action)
        return actions

    def _cooled(self, reason, t):
        last = self.last_fired.get(reason)
        return last is None or t - last >= self.config["cooldown_sec"]

    def _leak(self, s, t):
        value = s.get(self.leak["metric"])
        if value is None or value < 0:
            return None
        self.window.append((t, float(value)))
        while self.window and t - self.window[0][0] > self.leak["window_sec"]:
            self.window.popleft()
        if len(self.window) < self.leak["min_samples"]:
            return None
        if t - self.window[0][0] < self.leak["window_sec"] * 0.9:
            return None
        slope, r2 = linear_fit(list(self.window))
        if slope * 1.0 / 1024 >= self.leak["slope_kb_per_sec"] and r2 >= self.leak["min_r2"] and self._cooled("leak", t):
            self.last_fired["leak"] = t
            return {"kind": "census", "reason": "leak",
                    "detail": {"metric": self.leak["metric"], "slope_kb_per_sec": round(slope / 1024, 1), "r2": round(r2, 3),
                               "window_sec": round(t - self.window[0][0], 1)}}
        return None

    def _overload(self, s, t):
        frame = s.get("frameAvg")
        if frame is None or frame <= self.overload["frame_budget_ms"]:
            self.over_since = None
            return None
        if self.over_since is None:
            # 이 요약이 덮는 구간 시작부터 센다. 구간 길이 = 프레임 수 × 평균 프레임 시간.
            frames = s.get("frames")
            span = frames * frame / 1000.0 if _is_number(frames) and frames > 0 else 1.0
            self.over_since = t - span
        if t - self.over_since >= self.overload["sustain_sec"] and self._cooled("overload", t):
            self.last_fired["overload"] = t
            sustained = t - self.over_since
            self.over_since = None
            return {"kind": "capture", "reason": "overload",
                    "detail": {"frameAvg": frame, "budget_ms": self.overload["frame_budget_ms"], "sustained_sec": round(sustained, 1)}}
        return None


# ---------------------------------------------------------------- 캡처 · 기록

class Events:
    def __init__(self, out_dir):
        self.out_dir = out_dir
        ensure_out_dir(out_dir)
        self.path = os.path.join(out_dir, "events.jsonl")

    def write(self, event):
        event = dict(event)
        event.setdefault("time", time.strftime("%Y-%m-%dT%H:%M:%S%z"))
        with open(self.path, "a", encoding="utf-8") as f:
            f.write(json.dumps(event, ensure_ascii=False) + "\n")
        return event


def ensure_out_dir(out_dir):
    """출력 폴더를 만들고 `*` 한 줄짜리 .gitignore를 둔다 — 캡처(로그 · 힙 스냅샷 · 화면)가 저장소에 커밋되지 않게."""
    os.makedirs(out_dir, exist_ok=True)
    ignore = os.path.join(out_dir, ".gitignore")
    if not os.path.exists(ignore):
        with open(ignore, "w", encoding="utf-8") as f:
            f.write("*\n")


def run_capture(adb, port, token, kind, reason, out_dir, pull=True, drop=True):
    """기기에 캡처 명령을 보내고, 받으면 out_dir/captures/<이름>/으로 가져온다. 결과 이벤트(dict)를 돌려준다."""
    check_safe(reason, "reason", MAX_REASON)
    started = time.time()
    reply = send_command(port, f"{kind} reason={reason}", token)
    event = {"type": "capture", "kind": kind, "reason": reason, "reply": reply}
    if not reply.get("ok"):
        return event
    name, remote = reply.get("name"), reply.get("dir")
    try:
        check_safe(name, "name")
    except ValueError as e:
        event["error"] = f"bad reply: {e}"
        return event
    if not isinstance(remote, str) or remote.rstrip("/").split("/")[-1] != name:
        event["error"] = "bad reply: dir does not end with name"
        return event
    if pull and adb is not None:
        local = adb.pull(remote, os.path.join(out_dir, "captures"))
        event["local"] = os.path.abspath(local)
        if drop:
            drop_reply = send_command(port, f"drop name={name}", token)
            event["dropped"] = bool(drop_reply.get("ok"))
    event["seconds"] = round(time.time() - started, 2)
    return event


def resolve_adb(config, need=True):
    path = find_adb()
    if path is None:
        if need:
            sys.exit("adb not found. Set HB_ADB to adb path, or install Android Build Support in Unity Hub.")
        return None
    return Adb(path, config.get("serial"))


def fmt_mb(value):
    return "-" if value is None or value < 0 else f"{value / 1048576:.1f}MB"
