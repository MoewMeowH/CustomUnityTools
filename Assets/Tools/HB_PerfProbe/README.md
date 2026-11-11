# HB_PerfProbe

Android 개발 빌드를 폰에서 플레이하는 동안 성능 지표를 PC로 보내고, 이상(메모리 증가 · 과부하)이 보이거나 요청하면 캡처를 남기는 도구입니다 (Unity 6.3+, 6 / 7).

- 게임 쪽 (`HB.PerfProbe` 어셈블리): 1초마다 요약 한 줄을 logcat(태그 `HBProbe`)에 쓰고, `127.0.0.1` 명령 소켓으로 캡처 명령을 받습니다.
- PC 쪽 (`Tools~/`의 Python 스크립트, 표준 라이브러리만): logcat을 읽어 판정하고, 캡처를 `adb pull`로 가져와 Markdown 리포트를 만듭니다.

| 할 수 있는 일 | 설명서 |
|---|---|
| 상시 감시 · 자동 캡처, 한 번짜리 명령, 리포트 | [PC 스크립트](manual/pc-scripts.md) |

---

## 설치

```text
https://github.com/MoewMeowH/CustomUnityTools.git?path=Assets/Tools/HB_PerfProbe
```

버전을 고정하려면 URL 끝에 `#<태그>` 또는 `#<전체 40자 커밋 해시>`를 붙입니다.

## 켜는 법

1. `Project Settings > Player > Android > Scripting Define Symbols`에 `HB_PERFPROBE`를 넣습니다.
2. `Development Build`를 켜고 Android로 빌드합니다.
3. 앱이 첫 씬을 읽은 뒤 Probe가 저절로 켜집니다 (Android만).

켜지는 조건은 두 가지가 모두 맞을 때입니다.

| 조건 | 빠졌을 때 |
|---|---|
| `HB_PERFPROBE` 심볼 | 어셈블리가 컴파일되지 않습니다 (asmdef `defineConstraints`). 릴리즈 빌드에는 심볼을 넣지 않습니다. |
| `Debug.isDebugBuild` (개발 빌드) | 심볼이 있어도 켜지지 않고 경고만 남깁니다. |

- 에디터 Play와 Android 밖의 플랫폼에서는 저절로 켜지지 않습니다. 필요하면 Play 중에 코드에서 `PerfProbe.Start()`를 부릅니다.
- Unity 7.0에는 `DEVELOPMENT_BUILD` 심볼이 없어서 개발 빌드 판정은 `Debug.isDebugBuild`로 합니다.

## 코드 API

```csharp
using HB.PerfProbe;

PerfProbe.Start(new PerfProbeOptions { Port = 34999 }); // 이미 켜져 있으면 true. 개발 빌드가 아님 · Play 중 아님 · 포트 사용 중이면 false
PerfProbe.Mark("boss_start");  // 다음 요약 줄의 marks에 들어감 (영문 · 숫자 · _ - ., 처음 · 끝은 . 아님, 64자까지)
PerfProbe.Stop();              // 바로 꺼짐. 같은 프레임에서 다시 Start할 수 있음
```

- API는 메인 스레드에서 부릅니다.
- 한 구간의 marks가 1024자를 넘으면 뒤의 것은 버리고 끝에 `...`를 붙입니다 (logcat 한 줄 약 4KB 안에 들어가게).

`PerfProbeOptions` 기본값: 포트 34999, 요약 간격 1초, 캡처에 담는 최근 프레임 30초 · 요약 600개 · 로그 300줄, 기기에 남기는 캡처 20개.

## 모으는 지표

`ProfilerRecorder`로 읽습니다. 읽지 못하는 카운터는 `-1`로 씁니다 (기기 · 빌드에 따라 다름).

| 키 | 뜻 |
|---|---|
| `fps` · `frameAvg` · `frameMax` | 프레임 수 · 프레임 시간(ms, `Time.unscaledDeltaTime`) |
| `mainAvg` · `mainMax` | 메인 스레드 시간(ms, `Main Thread`) |
| `gcAlloc` | 구간 동안 GC 할당 합(바이트, `GC Allocated In Frame`). Probe도 요약 줄을 만들 때 1초에 한 번 조금 할당합니다 |
| `draw` · `setPass` | 프레임당 드로우콜 · SetPass 평균 |
| `gcUsed` · `totalUsed` · `totalReserved` · `systemUsed` | 구간 끝 메모리(바이트) |
| `marks` | 그 구간에 들어온 `mark` 이름들 |

## 명령 (소켓 한 줄 → JSON 한 줄)

PC에서 `adb forward tcp:34999 tcp:34999` 뒤 `127.0.0.1:34999`로 보냅니다. 보통은 [PC 스크립트](manual/pc-scripts.md)가 대신 보냅니다.

- 연결 하나에 명령 한 줄 · 답 한 줄입니다. 동시 연결은 4개까지 받습니다.
- 모든 명령 끝에 `token=<토큰>`을 붙입니다. 토큰은 Probe가 켜질 때 무작위로 만들고, logcat 요약 줄(`"token"`)에만 씁니다.
  Android에서는 같은 기기의 다른 앱도 127.0.0.1에 붙을 수 있지만 남의 logcat은 읽지 못하므로, 토큰 없이는 명령을 보낼 수 없습니다.

| 명령 | 하는 일 |
|---|---|
| `status` | 앱 · 기기 · 캡처 폴더 위치 · 최근 요약 |
| `capture [reason=..]` | 공통 캡처 (아래). 사유는 40자까지 |
| `census [reason=..]` | 공통 캡처 + `census.json` (타입별 오브젝트 수 · 메모리) |
| `snapshot [reason=..]` | 공통 캡처 + `memory.snap` (`MemoryProfiler.TakeSnapshot`) |
| `mark name=..` | 구간 표시 |
| `drop name=<폴더>` | 기기의 캡처 폴더 하나를 지움 (PC가 가져간 뒤). 캡처 이름 형식인 폴더만 |

공통 캡처 파일은 `meta.json`(기기 · 앱 정보), `frames.json`(최근 30초 프레임), `summaries.json`(최근 요약), `logs.txt`(최근 로그, 오류는 스택 포함), `screenshot.png`입니다.
저장 위치는 `Application.persistentDataPath/HBProbe/<yyyyMMdd_HHmmss_fff>_<사유>/`입니다.

## 주의

- `census`는 모든 오브젝트를 훑어서 찍는 순간 끊김이 생깁니다. PC 감시는 캡처 직후 몇 초를 판정에서 뺍니다.
- `.snap`은 에디터 Memory Profiler 패키지로 여는 사람용 파일입니다. 자동 비교는 `census`로 합니다.
- 소켓은 `127.0.0.1`에만 열려 같은 네트워크의 다른 기기는 붙을 수 없습니다. 같은 폰의 다른 앱은 붙을 수 있지만 토큰이 없으면 거절됩니다. 그래도 개발 빌드 전용으로만 씁니다.
- 캡처를 지우는 것(개수 상한 정리 · `drop`)은 캡처 폴더 위치 바로 아래의 캡처 이름 형식(`yyyyMMdd_HHmmss_fff_사유`) 폴더뿐입니다.
- 캡처에는 게임 로그 · 관리 힙(`memory.snap`) · 화면이 들어가므로 토큰 · 개인 정보가 담길 수 있습니다. 저장소에 올리지 않습니다 (PC 출력 폴더에는 `.gitignore`가 저절로 생김).
- 지원 플랫폼은 Android입니다. 게임 쪽 코드는 다른 플랫폼에서도 컴파일되지만 logcat 출력과 PC 스크립트는 Android 전용입니다.
