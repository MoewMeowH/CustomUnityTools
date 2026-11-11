"""PC 스크립트 단위 시험. 실행: python -m unittest discover -s tests (Tools~ 폴더에서)"""

import json
import os
import socket
import sys
import tempfile
import threading
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import probe_common as pc  # noqa: E402
import probe_report as pr  # noqa: E402


def config(**overrides):
    c = json.loads(json.dumps(pc.DEFAULT_CONFIG))
    pc._merge(c, overrides)
    return c


def summary(seq, t, used=100 * 1048576, frame=16.0):
    return {"seq": seq, "t": float(t), "frameAvg": frame, "totalUsed": used}


class ParseTests(unittest.TestCase):
    def test_logcat_line(self):
        line = 'I/HBProbe(12345): {"seq":3,"t":12.5,"fps":60,"totalUsed":1024}\n'
        data = pc.parse_logcat_line(line)
        self.assertEqual(data["seq"], 3)
        self.assertEqual(data["_pid"], 12345)
        # brief 형식은 태그를 8칸으로 채운다.
        self.assertEqual(pc.parse_logcat_line('I/HBProbe ( 777): {"seq":1,"t":1}')["_pid"], 777)

    def test_logcat_rejects_wrong_types(self):
        for bad in ('{"seq":"a","t":1}', '{"seq":true,"t":1}', '{"seq":1}', '{"seq":1,"t":"x"}',
                    '{"seq":1,"t":1,"totalUsed":"x"}', '{"seq":1,"t":1,"frameAvg":[1]}', '{"seq":1,"t":1,"marks":5}',
                    '{"seq":1,"t":1,"token":1}', '[1,2]'):
            self.assertIsNone(pc.parse_logcat_line("I/HBProbe(1): " + bad), bad)
        self.assertIsNone(pc.parse_logcat_line('I/Other(1): {"seq":1,"t":1}'))

    def test_logcat_other_lines(self):
        self.assertIsNone(pc.parse_logcat_line("--------- beginning of main"))
        self.assertIsNone(pc.parse_logcat_line("I/HBProbe(1): not json"))
        self.assertIsNone(pc.parse_logcat_line('I/HBProbe(1): {"broken":'))
        self.assertIsNone(pc.parse_logcat_line('I/HBProbe(1): {"no_seq":1}'))

    def test_thermal(self):
        self.assertEqual(pc.parse_thermal_status("IsStatusOverride: false\nThermal Status: 2\n"), 2)
        self.assertIsNone(pc.parse_thermal_status("nothing"))

    def test_devices(self):
        text = "List of devices attached\nR5CT1\tdevice\nemulator-5554\tunauthorized\n\n"
        self.assertEqual(pc.parse_devices(text), ["R5CT1"])

    def test_check_safe(self):
        self.assertEqual(pc.check_safe("leak_1.a-b", "x"), "leak_1.a-b")
        self.assertEqual(pc.check_safe("a", "x"), "a")
        for bad in ("..", "...", ".a", "a.", "a/b", "a b", "", "x" * 65, "한글", None):
            with self.assertRaises(ValueError):
                pc.check_safe(bad, "x")
        with self.assertRaises(ValueError):
            pc.check_safe("x" * (pc.MAX_REASON + 1), "reason", pc.MAX_REASON)


class FindAdbTests(unittest.TestCase):
    def test_env_then_path(self):
        with tempfile.TemporaryDirectory() as d:
            fake = os.path.join(d, "adb.exe")
            open(fake, "w").close()
            self.assertEqual(pc.find_adb(environ={"HB_ADB": fake}, which=lambda _: "/on/path/adb", unity_roots=[]), fake)
            self.assertEqual(pc.find_adb(environ={}, which=lambda _: "/on/path/adb", unity_roots=[]), "/on/path/adb")

    def test_newest_unity_install(self):
        exe = "adb.exe" if os.name == "nt" else "adb"
        with tempfile.TemporaryDirectory() as d:
            made = []
            for version in ("6000.3.28f1", "7000.0.0a8", "6000.6.8f1"):
                tools = os.path.join(d, version, "platform-tools")
                os.makedirs(tools)
                open(os.path.join(tools, exe), "w").close()
                made.append(os.path.join(tools, exe))
            found = pc.find_adb(environ={}, which=lambda _: None, unity_roots=[os.path.join(d, "*", "platform-tools")])
            self.assertEqual(found, made[1])

    def test_none(self):
        self.assertIsNone(pc.find_adb(environ={}, which=lambda _: None, unity_roots=[]))


class LinearFitTests(unittest.TestCase):
    def test_perfect_line(self):
        slope, r2 = pc.linear_fit([(0, 1), (1, 3), (2, 5)])
        self.assertAlmostEqual(slope, 2.0)
        self.assertAlmostEqual(r2, 1.0)

    def test_degenerate(self):
        self.assertEqual(pc.linear_fit([(1, 1)]), (0.0, 0.0))
        self.assertEqual(pc.linear_fit([(1, 1), (1, 2)]), (0.0, 0.0))


class RulesTests(unittest.TestCase):
    def test_leak_fires_on_steady_growth_once_per_cooldown(self):
        rules = pc.Rules(config(leak={"window_sec": 60, "min_samples": 30, "slope_kb_per_sec": 64}, cooldown_sec=120))
        fired = []
        for i in range(200):
            used = 100 * 1048576 + i * 100 * 1024  # 100KB/s
            for a in rules.feed(summary(i + 1, i, used)):
                fired.append((i, a))
        self.assertEqual([a["reason"] for _, a in fired], ["leak", "leak"])
        self.assertEqual(fired[0][1]["kind"], "census")
        self.assertGreaterEqual(fired[0][0], 54)  # 창이 0.9 이상 찬 뒤
        self.assertGreaterEqual(fired[1][0] - fired[0][0], 120)

    def test_no_leak_on_flat_noise_or_slow_growth(self):
        rules = pc.Rules(config(leak={"window_sec": 60, "min_samples": 30, "slope_kb_per_sec": 64}))
        for i in range(200):
            noise = (i * 7919 % 13) * 1048576  # 0~12MB 들쭉날쭉
            self.assertEqual(rules.feed(summary(i + 1, i, 200 * 1048576 + noise)), [])
        rules = pc.Rules(config(leak={"window_sec": 60, "min_samples": 30, "slope_kb_per_sec": 64}))
        for i in range(200):
            self.assertEqual(rules.feed(summary(i + 1, i, 100 * 1048576 + i * 10 * 1024)), [])

    def test_overload_needs_sustain(self):
        rules = pc.Rules(config(overload={"frame_budget_ms": 33.3, "sustain_sec": 5}, leak={"enabled": False}))
        frames = [50, 50, 50, 16, 50, 50, 50, 50, 50, 50]
        fired = [i for i, f in enumerate(frames) if rules.feed(summary(i + 1, i, frame=f))]
        # 3번째 프레임에서 끊기고, 4번부터 다시 세어 (t-1) 기준 5초가 되는 8번에서 낸다.
        self.assertEqual(fired, [8])

    def test_overload_counts_interval_length(self):
        rules = pc.Rules(config(overload={"frame_budget_ms": 33.3, "sustain_sec": 5}, leak={"enabled": False}))
        # 간격 10초 요약 하나(50ms × 200프레임 = 10초)로 이미 5초를 넘는다.
        s = dict(summary(1, 10, frame=50), frames=200)
        self.assertEqual(len(rules.feed(s)), 1)

    def test_ignore_after_capture_and_reset_on_restart(self):
        rules = pc.Rules(config(overload={"frame_budget_ms": 33.3, "sustain_sec": 2}, leak={"enabled": False},
                                post_capture_ignore_sec=5, cooldown_sec=0))
        self.assertEqual(rules.feed(summary(1, 1, frame=50)), [])
        self.assertEqual(len(rules.feed(summary(2, 2, frame=50))), 1)
        rules.notify_capture(2)
        for i in range(3, 7):
            self.assertEqual(rules.feed(summary(i, i, frame=50)), [], i)
        # 앱 재시작: seq · t가 처음으로 돌아감 → 무시 구간도 풀린다.
        self.assertEqual(rules.feed(summary(1, 0.5, frame=50)), [])
        self.assertEqual(len(rules.feed(summary(2, 1.5, frame=50))), 1)


class SocketTests(unittest.TestCase):
    def test_send_command_roundtrip(self):
        server = socket.socket()
        server.bind(("127.0.0.1", 0))
        server.listen(1)
        port = server.getsockname()[1]
        received = []

        def serve():
            conn, _ = server.accept()
            with conn:
                data = b""
                while not data.endswith(b"\n"):
                    data += conn.recv(1024)
                received.append(data.decode())
                conn.sendall(b'{"ok":true,"cmd":"status"}\n')
            server.close()

        t = threading.Thread(target=serve, daemon=True)
        t.start()
        reply = pc.send_command(port, "status", "abc123", timeout=5)
        t.join(5)
        self.assertEqual(received, ["status token=abc123\n"])
        self.assertTrue(reply["ok"])

    def test_send_command_caps_reply_size(self):
        port = _serve_once(b"x" * (pc.MAX_REPLY_BYTES + 70000))
        with self.assertRaises(ConnectionError):
            pc.send_command(port, "status", timeout=5)

    def test_run_capture_without_pull(self):
        port = _serve_once(b'{"ok":true,"cmd":"census","name":"n","dir":"/x/n"}\n')
        with tempfile.TemporaryDirectory() as d:
            event = pc.run_capture(None, port, "tok", "census", "leak", d, pull=False)
        self.assertEqual(event["reply"]["name"], "n")
        self.assertNotIn("local", event)
        self.assertNotIn("error", event)
        with self.assertRaises(ValueError):
            pc.run_capture(None, port, "tok", "census", "../x", ".", pull=False)
        with self.assertRaises(ValueError):
            pc.run_capture(None, port, "tok", "census", "r" * (pc.MAX_REASON + 1), ".", pull=False)

    def test_run_capture_rejects_bad_reply(self):
        for reply in (b'{"ok":true,"name":"../up","dir":"/x/../up"}\n', b'{"ok":true,"name":"n","dir":"/sdcard/other"}\n'):
            port = _serve_once(reply)
            with tempfile.TemporaryDirectory() as d:
                event = pc.run_capture(None, port, "tok", "capture", "manual", d)
            self.assertIn("error", event, reply)
            self.assertNotIn("local", event)

    def test_discover_checks_token_and_pid(self):
        received = []
        port = _serve_once(b'{"ok":true,"cmd":"status","pid":200,"app":"com.x"}\n', received)

        class FakeAdb:
            def run(self, *args, timeout=60):
                return ('I/HBProbe(  300): {"seq":5,"t":1,"token":"fake","pid":300}\n'
                        'I/HBProbe(  200): {"seq":9,"t":2,"token":"real","pid":200}\n')

        token, pid, status = pc.discover(FakeAdb(), port)
        self.assertEqual((token, pid), ("real", 200))
        self.assertEqual(received, ["status token=real\n"])


class OutDirTests(unittest.TestCase):
    def test_events_creates_gitignore(self):
        with tempfile.TemporaryDirectory() as d:
            out = os.path.join(d, "HBProbe_out")
            pc.Events(out).write({"type": "x"})
            with open(os.path.join(out, ".gitignore"), encoding="utf-8") as f:
                self.assertEqual(f.read(), "*\n")


def _serve_once(reply, received=None):
    """연결 하나를 받아 한 줄 읽고 reply를 보낸 뒤 닫는 시험 서버. 포트를 돌려준다."""
    server = socket.socket()
    server.bind(("127.0.0.1", 0))
    server.listen(4)
    port = server.getsockname()[1]

    def serve():
        conn, _ = server.accept()
        with conn:
            data = b""
            while not data.endswith(b"\n"):
                chunk = conn.recv(1024)
                if not chunk:
                    break
                data += chunk
            if received is not None:
                received.append(data.decode())
            try:
                conn.sendall(reply)
            except OSError:
                pass
        server.close()

    threading.Thread(target=serve, daemon=True).start()
    return port


class ReportTests(unittest.TestCase):
    def test_census_diff_orders_by_growth(self):
        before = {"types": [{"type": "Mesh", "count": 2, "bytes": 100}, {"type": "Texture2D", "count": 1, "bytes": 1000}]}
        after = {"types": [{"type": "Mesh", "count": 50, "bytes": 5000}, {"type": "Texture2D", "count": 1, "bytes": 1000},
                           {"type": "Material", "count": 3, "bytes": 30}]}
        rows = pr.census_diff(before, after)
        self.assertEqual([r["type"] for r in rows], ["Mesh", "Material", "Texture2D"])
        self.assertEqual(rows[0]["count_delta"], 48)
        self.assertEqual(rows[0]["bytes_delta"], 4900)

    def test_frame_stats(self):
        doc = {"columns": ["t", "frameMs", "mainMs", "gcAlloc", "draw", "setPass"],
               "frames": [[i * 0.016, 10.0 if i < 90 else 50.0, 5.0, 0 if i % 2 else 256, 10, 2] for i in range(100)]}
        st = pr.frame_stats(doc, 33.3)
        self.assertEqual(st["count"], 100)
        self.assertEqual(st["over_budget"], 10)
        self.assertEqual(st["frame_max"], 50.0)
        self.assertAlmostEqual(st["frame_p50"], 10.0)
        self.assertEqual(st["alloc_frames"], 50)

    def test_percentile(self):
        self.assertIsNone(pr.percentile([], 0.5))
        self.assertAlmostEqual(pr.percentile([1, 2, 3, 4], 0.5), 2.5)

    def test_report_capture_and_census_files(self):
        with tempfile.TemporaryDirectory() as d:
            for name, total in (("20261111_000001_000_baseline", 10), ("20261111_000100_000_leak", 40)):
                folder = os.path.join(d, "captures", name)
                os.makedirs(folder)
                with open(os.path.join(folder, "census.json"), "w", encoding="utf-8") as f:
                    json.dump({"time": name, "totalObjects": total, "totalBytes": total * 100,
                               "types": [{"type": "Leaky", "count": total, "bytes": total * 100}]}, f)
                with open(os.path.join(folder, "summaries.json"), "w", encoding="utf-8") as f:
                    json.dump({"summaries": [summary(1, 0, 100), dict(summary(2, 10, 2148), marks="boss")]}, f)
            text = pr.report_latest(d, 33.3)
        self.assertIn("# 캡처 20261111_000100_000_leak", text)
        self.assertIn("| Leaky | 10→40 | +30 |", text)
        self.assertIn("mark: boss@10s", text)


if __name__ == "__main__":
    unittest.main()
