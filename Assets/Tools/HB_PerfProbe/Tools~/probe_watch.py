"""HB_PerfProbe 상시 감시. 기기 logcat(태그 HBProbe)의 1초 요약을 읽어 규칙으로 판정하고, 이상이면 캡처해 PC로 가져온다.

사용:
    python probe_watch.py [--config probe_config.json] [--serial SERIAL] [--port 34999] [--out HBProbe_out]

Ctrl+C로 끝낸다. 결과: <out>/events.jsonl, <out>/captures/<이름>/
"""

import argparse
import queue
import sys
import threading
import time

import probe_common as pc


def main(argv=None):
    parser = argparse.ArgumentParser(description="Watch HB_PerfProbe metrics over adb and capture on anomalies.")
    parser.add_argument("--config", help="JSON config (default: probe_config.json next to this script, if present)")
    parser.add_argument("--serial", help="adb device serial")
    parser.add_argument("--port", type=int, help="probe command port")
    parser.add_argument("--out", help="output folder")
    args = parser.parse_args(argv)

    config = pc.load_config(args.config)
    if args.serial:
        config["serial"] = args.serial
    if args.port:
        config["port"] = args.port
    if args.out:
        config["out_dir"] = args.out

    adb = pc.resolve_adb(config)
    try:
        serial = adb.ensure_device()
        adb.forward(config["port"])
    except pc.AdbError as e:
        sys.exit(str(e))

    events = pc.Events(config["out_dir"])
    port = config["port"]
    print(f"[watch] adb={adb.path} device={serial} port={port} out={events.out_dir}", flush=True)
    events.write({"type": "watch_start", "device": serial, "port": port, "config": config})

    watcher = Watcher(adb, port, config, events)
    try:
        watcher.run()
    except KeyboardInterrupt:
        events.write({"type": "watch_end", "why": "interrupted"})
        return 0
    finally:
        watcher.stop_logcat()


class Watcher:
    def __init__(self, adb, port, config, events):
        self.adb = adb
        self.port = port
        self.config = config
        self.events = events
        self.rules = pc.Rules(config)
        self.token = None
        self.pid = None
        self.logcat = None
        self.lines = queue.Queue()
        self.count = 0
        self.baseline_done = not config["baseline_census"]
        self.thermal_last = None
        self.thermal_next = 0.0
        self.last_summary = time.time()

    def run(self):
        self.connect()
        while True:
            try:
                line = self.lines.get(timeout=1.0)
            except queue.Empty:
                line = None

            if time.time() >= self.thermal_next:
                self.thermal_next = time.time() + self.config["thermal_poll_sec"]
                self.thermal_last = _poll_thermal(self.adb, self.events, self.thermal_last)

            # 요약이 한동안 없으면(앱 재시작 · 종료 · logcat 끊김) 토큰과 pid를 다시 찾는다.
            if time.time() - self.last_summary > self.config["stale_sec"]:
                print("[watch] no summary for a while; looking for the probe again", flush=True)
                self.connect()
                continue
            if line is None:
                continue
            try:
                self.handle(line)
            except Exception as e:  # 줄 하나가 이상해도 감시는 이어 간다.
                print(f"[watch] error handling a line: {type(e).__name__}: {e}", flush=True)
                self.events.write({"type": "error", "where": "line", "error": f"{type(e).__name__}: {e}"})

    def connect(self):
        """토큰 · pid를 찾을 때까지 기다린 뒤, 그 pid의 logcat만 읽는다."""
        self.stop_logcat()
        waited = False
        while True:
            try:
                self.adb.forward(self.port)
                found = pc.discover(self.adb, self.port)
            except (pc.AdbError, OSError) as e:
                found = None
                print(f"[watch] {e}", flush=True)
            if found:
                break
            if not waited:
                print("[watch] waiting for the app (development build with HB_PERFPROBE) ...", flush=True)
                waited = True
            time.sleep(3)

        token, pid, status = found
        if pid != self.pid:
            self.rules.reset()
            self.baseline_done = not self.config["baseline_census"]
        self.token, self.pid = token, pid
        print(f"[watch] probe found: app={status.get('app')} pid={pid}", flush=True)
        self.events.write({"type": "probe_found", "app": status.get("app"), "version": status.get("version"), "pid": pid})

        # -T 1: 버퍼의 마지막 줄 하나부터 이어 읽는다 (오래된 세션 줄을 판정에 섞지 않음).
        self.logcat = self.adb.popen("logcat", "-T", "1", "-v", "brief", f"--pid={pid}", "-s", f"{pc.LOGCAT_TAG}:I")
        self.lines = queue.Queue()
        threading.Thread(target=_pump, args=(self.logcat.stdout, self.lines), daemon=True).start()
        self.last_summary = time.time()

    def stop_logcat(self):
        if self.logcat is not None:
            self.logcat.terminate()
            self.logcat = None

    def handle(self, line):
        summary = pc.parse_logcat_line(line)
        if summary is None or summary["_pid"] != self.pid:
            return
        self.last_summary = time.time()

        self.count += 1
        every = self.config["print_every"]
        if every and self.count % every == 0:
            print(f"[watch] seq={summary.get('seq')} fps={summary.get('fps')} frame={summary.get('frameAvg')}ms "
                  f"main={summary.get('mainAvg')}ms used={pc.fmt_mb(summary.get('totalUsed'))} "
                  f"gc={pc.fmt_mb(summary.get('gcUsed'))}", flush=True)
        if summary.get("marks"):
            self.events.write({"type": "mark", "marks": summary["marks"], "t": summary.get("t")})

        actions = self.rules.feed(summary)
        if not self.baseline_done:
            self.baseline_done = True
            actions.insert(0, {"kind": "census", "reason": "baseline", "detail": {}})

        for action in actions:
            self.capture(summary, action)

    def capture(self, summary, action):
        print(f"[watch] {action['reason']} -> {action['kind']} {action.get('detail', {})}", flush=True)
        try:
            event = pc.run_capture(self.adb, self.port, self.token, action["kind"], action["reason"], self.config["out_dir"],
                                   pull=self.config["pull"], drop=self.config["drop_after_pull"])
        except (OSError, ValueError, pc.AdbError) as e:
            event = {"type": "capture", "kind": action["kind"], "reason": action["reason"], "error": f"{type(e).__name__}: {e}"}
        event["source"] = "watch"
        event["detail"] = action.get("detail", {})
        event["t"] = summary.get("t")
        self.events.write(event)
        # 캡처에 걸린 시간만큼 기기 시계도 흘렀으므로 그만큼 더 뺀다.
        self.rules.notify_capture(summary.get("t", 0) + event.get("seconds", 0))
        self.last_summary = time.time()
        where = event.get("local") or event.get("error") or event.get("reply", {}).get("error")
        print(f"[watch]   -> {where}", flush=True)


def _pump(stream, out):
    for line in stream:
        out.put(line)


def _poll_thermal(adb, events, last):
    """발열 단계가 오르면 기록만 한다. 읽지 못하면 이전 값을 그대로 둔다."""
    try:
        status = pc.parse_thermal_status(adb.run("shell", "dumpsys", "thermalservice", timeout=15))
    except (pc.AdbError, OSError):
        return last
    if status is not None and last is not None and status > last:
        print(f"[watch] thermal status {last} -> {status}", flush=True)
        events.write({"type": "thermal", "from": last, "to": status})
    return status if status is not None else last


if __name__ == "__main__":
    sys.exit(main())
