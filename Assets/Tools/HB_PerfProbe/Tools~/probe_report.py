"""HB_PerfProbe 캡처를 Markdown 리포트로 만든다 (지휘자 · 사람이 읽는 용도).

사용:
    python probe_report.py capture <캡처 폴더>          # 한 캡처: 프레임 · 요약 추이 · (census 있으면) 상위 타입
    python probe_report.py census <이전 census.json 또는 폴더> <나중 것>   # 두 census 차이
    python probe_report.py latest [--out HBProbe_out]   # 가장 최근 캡처 + census가 둘 이상이면 마지막 두 장 차이

--write PATH 를 주면 파일로도 쓴다.
"""

import argparse
import json
import os
import sys

import probe_common as pc


# ---------------------------------------------------------------- 계산

def percentile(sorted_values, p):
    if not sorted_values:
        return None
    k = (len(sorted_values) - 1) * p
    lo = int(k)
    hi = min(lo + 1, len(sorted_values) - 1)
    return sorted_values[lo] + (sorted_values[hi] - sorted_values[lo]) * (k - lo)


def frame_stats(frames_doc, budget_ms):
    cols = frames_doc["columns"]
    rows = frames_doc["frames"]
    fi, mi, ai = cols.index("frameMs"), cols.index("mainMs"), cols.index("gcAlloc")
    frames = sorted(r[fi] for r in rows)
    mains = sorted(r[mi] for r in rows if r[mi] is not None and r[mi] >= 0)
    allocs = [r[ai] for r in rows if r[ai] is not None and r[ai] >= 0]
    if not frames:
        return None
    return {
        "count": len(frames),
        "span_sec": round(rows[-1][0] - rows[0][0], 1),
        "frame_avg": sum(frames) / len(frames),
        "frame_p50": percentile(frames, 0.5),
        "frame_p95": percentile(frames, 0.95),
        "frame_p99": percentile(frames, 0.99),
        "frame_max": frames[-1],
        "over_budget": sum(1 for f in frames if f > budget_ms),
        "main_avg": sum(mains) / len(mains) if mains else None,
        "main_max": mains[-1] if mains else None,
        "alloc_per_frame": sum(allocs) / len(allocs) if allocs else None,
        "alloc_frames": sum(1 for a in allocs if a > 0),
    }


def summary_trend(summaries, metric):
    points = [(s["t"], s[metric]) for s in summaries if s.get(metric) is not None and s[metric] >= 0]
    if len(points) < 2:
        return None
    slope, r2 = pc.linear_fit(points)
    return {"first": points[0][1], "last": points[-1][1], "span_sec": points[-1][0] - points[0][0],
            "slope_kb_per_sec": slope / 1024, "r2": r2}


def census_diff(before, after):
    """타입별 (개수 · 메모리) 차이. 메모리 증가가 큰 순."""
    a = {e["type"]: e for e in before["types"]}
    b = {e["type"]: e for e in after["types"]}
    rows = []
    for name in set(a) | set(b):
        ea = a.get(name, {"count": 0, "bytes": 0})
        eb = b.get(name, {"count": 0, "bytes": 0})
        rows.append({"type": name, "count_before": ea["count"], "count_after": eb["count"],
                     "count_delta": eb["count"] - ea["count"], "bytes_delta": eb["bytes"] - ea["bytes"],
                     "bytes_after": eb["bytes"]})
    rows.sort(key=lambda r: (-r["bytes_delta"], -r["count_delta"], r["type"]))
    return rows


# ---------------------------------------------------------------- 출력

def _ms(v):
    return "-" if v is None else f"{v:.1f}"


def _kb(v):
    return "-" if v is None else f"{v / 1024:+.1f}KB" if abs(v) < 1048576 else f"{v / 1048576:+.2f}MB"


def _load(path):
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def report_capture(folder, budget_ms):
    out = [f"# 캡처 {os.path.basename(os.path.normpath(folder))}", ""]
    meta_path = os.path.join(folder, "meta.json")
    if os.path.exists(meta_path):
        m = _load(meta_path)
        out += [f"- 종류 · 사유: {m.get('kind')} · {m.get('reason')}",
                f"- 시각: {m.get('time')} (기기 시작 후 {m.get('realtime')}초)",
                f"- 기기: {m.get('device')} / {m.get('os')} / {m.get('gpu')} / RAM {m.get('systemMemoryMB')}MB",
                f"- 앱: {m.get('app')} {m.get('version')} (Unity {m.get('unity')}), 화면 {m.get('screen')}, "
                f"targetFrameRate {m.get('targetFrameRate')}, 품질 {m.get('qualityLevel')}", ""]

    frames_path = os.path.join(folder, "frames.json")
    if os.path.exists(frames_path):
        st = frame_stats(_load(frames_path), budget_ms)
        out += ["## 최근 프레임", ""]
        if st:
            out += [f"- {st['count']}프레임 / {st['span_sec']}초",
                    f"- 프레임 시간 ms: 평균 {_ms(st['frame_avg'])} · p50 {_ms(st['frame_p50'])} · p95 {_ms(st['frame_p95'])} · "
                    f"p99 {_ms(st['frame_p99'])} · 최대 {_ms(st['frame_max'])}",
                    f"- 예산 {budget_ms}ms 넘은 프레임: {st['over_budget']}",
                    f"- 메인 스레드 ms: 평균 {_ms(st['main_avg'])} · 최대 {_ms(st['main_max'])}",
                    f"- GC 할당: 프레임당 평균 {_kb(st['alloc_per_frame'])}, 할당이 있던 프레임 {st['alloc_frames']}", ""]
        else:
            out += ["- 프레임 없음", ""]

    summaries_path = os.path.join(folder, "summaries.json")
    if os.path.exists(summaries_path):
        summaries = _load(summaries_path)["summaries"]
        out += ["## 요약 추이 (최근 요약 전체)", "", "| 지표 | 처음 | 마지막 | 기울기 | R² | 구간 |", "|---|---|---|---|---|---|"]
        for metric in ("totalUsed", "gcUsed", "totalReserved", "systemUsed"):
            tr = summary_trend(summaries, metric)
            if tr:
                out.append(f"| {metric} | {pc.fmt_mb(tr['first'])} | {pc.fmt_mb(tr['last'])} | "
                           f"{tr['slope_kb_per_sec']:+.1f}KB/s | {tr['r2']:.2f} | {tr['span_sec']:.0f}초 |")
        marks = [(s["t"], s["marks"]) for s in summaries if s.get("marks")]
        if marks:
            out += ["", "mark: " + ", ".join(f"{name}@{t:.0f}s" for t, name in marks)]
        out.append("")

    census_path = os.path.join(folder, "census.json")
    if os.path.exists(census_path):
        c = _load(census_path)
        out += ["## census 상위 15 (메모리 순)", "", f"전체 {c['totalObjects']}개 · {pc.fmt_mb(c['totalBytes'])}", "",
                "| 타입 | 개수 | 메모리 |", "|---|---|---|"]
        for e in c["types"][:15]:
            out.append(f"| {e['type']} | {e['count']} | {pc.fmt_mb(e['bytes'])} |")
        out.append("")

    for extra in ("screenshot.png", "memory.snap", "logs.txt"):
        if os.path.exists(os.path.join(folder, extra)):
            out.append(f"- 파일: {extra}")
    return "\n".join(out) + "\n"


def report_census(path_a, path_b, top=20):
    a = _load(_census_path(path_a))
    b = _load(_census_path(path_b))
    rows = census_diff(a, b)
    out = ["# census 차이", "",
           f"- 이전: {path_a} ({a.get('time')}) {a['totalObjects']}개 · {pc.fmt_mb(a['totalBytes'])}",
           f"- 나중: {path_b} ({b.get('time')}) {b['totalObjects']}개 · {pc.fmt_mb(b['totalBytes'])}",
           f"- 변화: 개수 {b['totalObjects'] - a['totalObjects']:+d} · 메모리 {_kb(b['totalBytes'] - a['totalBytes'])}", "",
           f"## 늘어난 타입 상위 {top}", "", "| 타입 | 개수 (이전→나중) | 개수 변화 | 메모리 변화 | 나중 메모리 |", "|---|---|---|---|---|"]
    grown = [r for r in rows if r["bytes_delta"] > 0 or r["count_delta"] > 0]
    for r in grown[:top]:
        out.append(f"| {r['type']} | {r['count_before']}→{r['count_after']} | {r['count_delta']:+d} | "
                   f"{_kb(r['bytes_delta'])} | {pc.fmt_mb(r['bytes_after'])} |")
    if not grown:
        out.append("| (없음) | | | | |")
    return "\n".join(out) + "\n"


def _census_path(p):
    return os.path.join(p, "census.json") if os.path.isdir(p) else p


def report_latest(out_dir, budget_ms):
    captures = os.path.join(out_dir, "captures")
    if not os.path.isdir(captures):
        return f"캡처 없음: {captures}\n"
    names = sorted(n for n in os.listdir(captures) if os.path.isdir(os.path.join(captures, n)))
    if not names:
        return f"캡처 없음: {captures}\n"
    text = report_capture(os.path.join(captures, names[-1]), budget_ms)
    with_census = [n for n in names if os.path.exists(os.path.join(captures, n, "census.json"))]
    if len(with_census) >= 2:
        text += "\n" + report_census(os.path.join(captures, with_census[-2]), os.path.join(captures, with_census[-1]))
    return text


def main(argv=None):
    parser = argparse.ArgumentParser(description="Make a Markdown report from HB_PerfProbe captures.")
    parser.add_argument("--config")
    parser.add_argument("--write", help="also write the report to this file")
    sub = parser.add_subparsers(dest="command", required=True)
    p = sub.add_parser("capture")
    p.add_argument("folder")
    p = sub.add_parser("census")
    p.add_argument("before")
    p.add_argument("after")
    p = sub.add_parser("latest")
    p.add_argument("--out")
    args = parser.parse_args(argv)

    config = pc.load_config(args.config)
    budget = config["overload"]["frame_budget_ms"]
    if args.command == "capture":
        text = report_capture(args.folder, budget)
    elif args.command == "census":
        text = report_census(args.before, args.after)
    else:
        text = report_latest(args.out or config["out_dir"], budget)

    if args.write:
        with open(args.write, "w", encoding="utf-8") as f:
            f.write(text)
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stdout.write(text)
    return 0


if __name__ == "__main__":
    sys.exit(main())
