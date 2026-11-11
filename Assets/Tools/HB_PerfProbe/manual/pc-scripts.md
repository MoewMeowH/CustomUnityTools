# PC 스크립트

`Tools~/` 폴더의 Python 스크립트입니다 (Python 3.8+, 표준 라이브러리만). Unity는 `~`로 끝나는 폴더를 가져오지 않으므로 프로젝트에 영향을 주지 않습니다.

- git URL로 설치했다면 스크립트는 `Library/PackageCache/com.hb.perfprobe@<해시>/Tools~/`에 있습니다.
- 출력 폴더 기본값은 **실행한 폴더** 아래 `HBProbe_out/`입니다 (`--out`으로 바꿈).

## adb 찾기

순서: 환경 변수 `HB_ADB` → `PATH` → `ANDROID_HOME` · `ANDROID_SDK_ROOT` → Unity Hub 설치의 Android Build Support (`Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools`, 가장 새 버전).

- Windows에서는 PATH의 `adb.exe`만 받습니다 (현재 폴더의 `adb.bat` 같은 파일을 실행하지 않게).
- 기기가 여럿 연결돼 있으면 `--serial`로 고릅니다. 여러 기기를 동시에 감시할 때는 기기마다 `--port`를 다르게 줍니다 (같은 포트면 나중 forward가 앞의 것을 덮음). 앱 쪽 포트는 `PerfProbeOptions.Port`로 맞춥니다.

## 토큰 찾기

Probe 명령에는 토큰이 필요합니다. 스크립트는 다음 순서로 찾습니다.

1. `adb logcat -d`로 남아 있는 `HBProbe` 요약 줄에서 토큰과 그 줄의 pid를 읽습니다.
2. 그 토큰으로 `status`를 보내, 서버가 받아들이고 서버가 알려 준 pid가 줄의 pid와 같으면 그 토큰을 씁니다.
3. 감시(`probe_watch.py`)는 그 pid의 logcat만 읽습니다. 다른 앱이 같은 태그로 쓴 줄은 무시합니다.

그래서 앱이 켜져 요약이 한 줄이라도 나온 뒤에 명령을 보낼 수 있습니다.

## probe_watch.py — 상시 감시

```text
python probe_watch.py [--config probe_config.json] [--serial S] [--port 34999] [--out HBProbe_out]
```

1. 기기 연결을 확인하고 포트를 forward한 뒤 토큰을 찾습니다. 앱이 아직 없으면 나올 때까지 기다립니다.
2. 시작 직후 기준 census를 한 장 찍습니다 (`baseline_census`).
3. logcat의 `HBProbe` 줄을 읽어 규칙으로 판정합니다. `print_every`개마다 한 줄씩 상태를 보여 줍니다.
4. 캡처하면 `adb pull`로 `<out>/captures/<이름>/`에 가져오고 기기 쪽 폴더를 지웁니다. 모든 일은 `<out>/events.jsonl`에 남습니다.

- 요약이 `stale_sec`(기본 15초) 동안 없으면(앱 재시작 · 종료) 토큰을 다시 찾습니다. pid가 바뀌었으면 판정 상태를 비우고 기준 census부터 다시 찍습니다.
- 출력 폴더에는 `*` 한 줄짜리 `.gitignore`를 만듭니다. 캡처에 로그 · 힙 · 화면이 들어가므로 저장소에 올라가지 않게 하려는 것입니다.

Ctrl+C로 끝냅니다.

### 판정 규칙

| 규칙 | 조건 | 하는 일 |
|---|---|---|
| 릭 (`leak`) | `window_sec` 동안 `metric`(기본 `totalUsed`)의 직선 기울기가 `slope_kb_per_sec` 이상이고 R²가 `min_r2` 이상 | `census` |
| 과부하 (`overload`) | `frameAvg`가 `frame_budget_ms`를 넘는 상태가 `sustain_sec` 이상 이어짐 (첫 구간 길이는 `frames × frameAvg`) | `capture` |
| 발열 | `dumpsys thermalservice`의 단계가 오름 (`thermal_poll_sec`마다) | 기록만 |

- 같은 사유는 `cooldown_sec` 동안 다시 내지 않습니다.
- 캡처 직후 `post_capture_ignore_sec` 동안의 요약은 판정에서 뺍니다 (캡처 자체의 끊김).
- 요약의 `seq`나 시각이 줄면 앱이 다시 켜진 것으로 보고 판정 상태를 비웁니다.

### 설정 파일

`probe_config.example.json`을 `probe_config.json`으로 복사해 고칩니다. 스크립트 옆에 `probe_config.json`이 있으면 `--config` 없이도 읽습니다. 적지 않은 값은 기본값을 씁니다.

## probe_cmd.py — 한 번짜리 명령

```text
python probe_cmd.py status
python probe_cmd.py capture [--reason manual]
python probe_cmd.py census  [--reason census]
python probe_cmd.py snapshot [--reason snapshot]
python probe_cmd.py mark NAME
python probe_cmd.py pull NAME
python probe_cmd.py drop NAME
```

- 결과 JSON을 표준 출력에 한 줄로 씁니다. 성공이면 종료 코드 0입니다.
- 사유(`--reason`)는 40자까지, 이름은 64자까지입니다. 영문 · 숫자 · `_ - .`만 쓰고 처음 · 끝은 `.`일 수 없습니다.
- `capture` · `census` · `snapshot`은 가져온 뒤 기기 쪽 폴더를 지웁니다. `--no-pull`은 가져오지 않고, `--keep`은 기기에 남깁니다.
- `probe_watch.py`와 같은 출력 폴더 · `events.jsonl`을 씁니다. 감시 중에 같이 불러도 됩니다 (소켓은 동시 연결 4개까지 받음).

## probe_report.py — 리포트

```text
python probe_report.py latest [--out HBProbe_out]
python probe_report.py capture <캡처 폴더>
python probe_report.py census <이전 census.json 또는 폴더> <나중 것>
```

- `latest`: 가장 최근 캡처 리포트에, census가 두 장 이상이면 마지막 두 장의 차이를 붙입니다.
- `capture`: 기기 정보, 최근 프레임 통계(p50 · p95 · p99 · 예산 초과 수 · GC 할당), 요약 추이(메모리 지표별 기울기 · R²), mark, census 상위 타입.
- `census`: 타입별 개수 · 메모리 변화를 늘어난 순으로.
- `--write PATH`로 파일에도 씁니다.

## 시험

```text
cd Tools~
python -m unittest discover -s tests
```
