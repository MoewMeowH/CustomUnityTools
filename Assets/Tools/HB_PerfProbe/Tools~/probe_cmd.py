"""HB_PerfProbe 한 번짜리 명령. 사람 · 지휘자가 직접 부른다.

사용:
    python probe_cmd.py status
    python probe_cmd.py capture [--reason manual]
    python probe_cmd.py census  [--reason census]
    python probe_cmd.py snapshot [--reason snapshot]
    python probe_cmd.py mark NAME
    python probe_cmd.py pull NAME      # 기기에 남은 캡처 폴더를 가져온다
    python probe_cmd.py drop NAME      # 기기에 남은 캡처 폴더를 지운다

공통 옵션: --config --serial --port --out --no-pull --keep
결과 JSON을 표준 출력에 한 줄로 쓰고, 캡처면 <out>/events.jsonl에도 남긴다.
명령 토큰은 기기 logcat에서 찾아 붙인다 (앱이 켜진 뒤 요약이 한 줄이라도 나와 있어야 함).
"""

import argparse
import json
import os
import sys

import probe_common as pc


def main(argv=None):
    parser = argparse.ArgumentParser(description="Send one command to HB_PerfProbe over adb.")
    parser.add_argument("--config")
    parser.add_argument("--serial")
    parser.add_argument("--port", type=int)
    parser.add_argument("--out")
    parser.add_argument("--no-pull", action="store_true", help="do not adb pull the capture folder")
    parser.add_argument("--keep", action="store_true", help="keep the capture folder on the device after pull")
    sub = parser.add_subparsers(dest="command", required=True)
    sub.add_parser("status")
    for kind in ("capture", "census", "snapshot"):
        p = sub.add_parser(kind)
        p.add_argument("--reason", default="manual" if kind == "capture" else kind, help=f"up to {pc.MAX_REASON} chars")
    p = sub.add_parser("mark")
    p.add_argument("name")
    p = sub.add_parser("pull")
    p.add_argument("name")
    p = sub.add_parser("drop")
    p.add_argument("name")
    args = parser.parse_args(argv)

    config = pc.load_config(args.config)
    if args.serial:
        config["serial"] = args.serial
    if args.port:
        config["port"] = args.port
    if args.out:
        config["out_dir"] = args.out
    port = config["port"]

    adb = pc.resolve_adb(config)
    try:
        adb.ensure_device()
        adb.forward(port)
        found = pc.discover(adb, port)
        if found is None:
            raise pc.AdbError("probe not found: no HBProbe summary with a valid token in logcat (is the app running?)")
        token = found[0]

        if args.command in ("capture", "census", "snapshot"):
            event = pc.run_capture(adb, port, token, args.command, args.reason, config["out_dir"],
                                   pull=not args.no_pull and config["pull"],
                                   drop=not args.keep and config["drop_after_pull"])
            event["source"] = "cmd"
            pc.Events(config["out_dir"]).write(event)
            result = event
        elif args.command == "mark":
            result = pc.send_command(port, f"mark name={pc.check_safe(args.name, 'name')}", token)
        elif args.command == "drop":
            result = pc.send_command(port, f"drop name={pc.check_safe(args.name, 'name')}", token)
        elif args.command == "pull":
            name = pc.check_safe(args.name, "name")
            status = found[2]
            if not status.get("ok"):
                result = status
            else:
                local = adb.pull(status["root"].rstrip("/") + "/" + name, os.path.join(config["out_dir"], "captures"))
                result = {"ok": True, "cmd": "pull", "name": name, "local": os.path.abspath(local)}
        else:
            result = pc.send_command(port, args.command, token)
    except (pc.AdbError, OSError, ValueError) as e:
        result = {"ok": False, "cmd": args.command, "error": f"{type(e).__name__}: {e}"}

    print(json.dumps(result, ensure_ascii=False))
    ok = result["ok"] if "ok" in result else (result.get("reply", {}).get("ok", False) and "error" not in result)
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
