# HB_ScreenRecorder

Unity Recorder 기반 에디터 화면 녹화, 스크린샷 캡처 및 QA 진단 데이터(시스템 상태 및 로그) 덤프 도구입니다.

---

## 설치

```text
https://github.com/MoewMeowH/CustomUnityTools.git?path=Assets/Tools/HB_ScreenRecorder
```

---

## 주요 기능
- **MP4 비디오 녹화**: `UnityEditor.Recorder`를 제어하여 GameView 화면을 MP4 포맷으로 녹화합니다.
  - Play Mode에서만 시작할 수 있습니다. Edit Mode에서는 시작 버튼이 꺼지고 안내가 보입니다.
  - 출력은 1920×1080 · 비트레이트 `High` · 오디오 포함입니다. 녹화는 수동 모드(시작 · 정지 버튼)이며 재생 프레임 레이트는 가변(`Variable`)입니다.
  - **Target FPS**는 10~60 사이로 맞춥니다 (범위 밖 값은 잘림).
  - 녹화 중 Play Mode를 나가면 자동으로 녹화를 마치고 진단 파일을 저장합니다.
  - Recorder가 스스로 녹화를 멈추면 그때 바로 세션을 마무리(진단 파일 저장)합니다. 녹화 중 스크립트를 재컴파일해도 리로드 전에 마무리합니다.
- **스크린샷 캡처**: Game 뷰 화면을 PNG로 캡처합니다 (`ScreenCapture.CaptureScreenshot`). Play Mode가 아니어도 버튼을 누를 수 있습니다.
  - 이미지는 Game 뷰가 다음에 그릴 때 저장됩니다 (요청 뒤 에디터 뷰를 다시 그리게 합니다).
  - 창은 파일이 생기면 `Screenshot Saved`, 5초 안에 생기지 않으면 `Screenshot not saved yet`으로 알립니다.
    이때 요청은 취소되지 않습니다. Game 뷰 탭이 가려져 있다가 나중에 보이면 **그 시점 화면**이 이 이름으로 저장됩니다 (상태 · 로그 파일은 요청 시점).
  - 상태 · 로그 파일은 바로 씁니다 (저장 · 폴더 생성 실패는 에러 로그만).
  - 같은 초에 다시 찍으면 이름 뒤에 `_1`, `_2`…를 붙입니다(덮어쓰지 않음).
  - Unity는 캡처 요청을 하나만 둡니다. PNG가 저장되기 전에 다시 요청하면(예: 스크립트에서 같은 프레임에 두 번) 앞 요청의 PNG는 생기지 않고 상태 · 로그 파일만 남습니다.
- **진단 데이터 자동 덤프**: 녹화 종료 또는 스크린샷 캡처 시 당시의 씬/메모리/디바이스 상태와 인게임 콘솔 로그를 프로젝트 루트 `Recording/` 아래에 함께 저장합니다.

  | 동작 | 저장 위치 | 파일 |
  |---|---|---|
  | 녹화 | `Recording/Record_{yyyyMMdd_HHmmss}[_{태그}]/` | `recording.mp4` · `Diagnostic_State.json` · `Diagnostic_Logs.txt` · `QA_BugReport_Summary.txt` |
  | 스크린샷 | `Recording/Screenshots/` | `Screenshot_{yyyyMMdd_HHmmss}[_{태그}][_{번호}].png` · 같은 이름 + `_State.json` · `_Logs.txt` |

  - 요약 리포트(`QA_BugReport_Summary.txt`)는 녹화에만 만듭니다. 세션 종류 · 태그 · 시각 · 녹화 길이 · 활성 씬 · Play 여부 · Unity 버전 · OS · 영상 출력 해상도(1920x1080) · Game 뷰 해상도 · 메모를 적습니다.
  - 상태 JSON 필드

    | 필드 | 뜻 |
    |---|---|
    | `timestamp` · `activeSceneName` · `totalRootGameObjects` · `rootGameObjectNames` · `isPlayMode` | 시각, 활성 씬, 루트 오브젝트, Play 여부 |
    | `timeSinceStartup` | 에디터 시작 후 초 |
    | `recordingDurationSeconds` | 녹화 길이 (스크린샷은 0) |
    | `managedHeapBytes` · `managedHeapMB` | 관리 힙 (`GC.GetTotalMemory`) |
    | `unityAllocatedBytes` · `unityAllocatedMB` | Unity 네이티브 할당 (`Profiler.GetTotalAllocatedMemoryLong`). 관리 힙은 들어가지 않으므로 두 값을 더해 봅니다 |
    | `screenResolution` · `operatingSystem` · `graphicsDeviceName` · `unityVersion` | 환경 정보 |
    | `issueTag` · `qaNotes` | 입력한 태그 · 메모 |
  - 해상도는 Game 뷰 렌더 해상도(`Handles.GetMainGameViewSize`)입니다. 창 버튼에서 `Screen.width/height`를 읽으면 이 창 크기가 되기 때문입니다.
  - 예전 필드 `allocatedMemoryBytes` · `allocatedMemoryMB`는 `managedHeapBytes` · `managedHeapMB`로 이름이 바뀌었습니다(값은 같음).
  - 태그에 파일 이름으로 쓸 수 없는 문자가 있으면 `_`로 바꿉니다.
- **느슨한 결합(Loose Coupling) 로그 연동**: 리플렉션으로 `HLogCore`(어셈블리 `HB.InGameConsole`, `com.hb.ingameconsole`)가 있는지 확인하고, 있으면 `HLogCore.History`의 로그(시간 · 종류 · 메시지 · 스택 트레이스)와 에러 · 경고 개수를 `Diagnostic_Logs.txt`에 씁니다.
  - 없으면 로그 대신 "InGameConsole is not installed" 안내만 씁니다.
  - `HLogCore.History`는 InGameConsole에 `USE_HLOG` 정의가 있을 때만 로그를 담습니다. 정의가 없으면 로그 0개로 기록됩니다.
- **QA 리포트 메타데이터**: **Issue / Bug Tag** · **Target FPS** · **QA Notes**를 입력합니다. 태그는 폴더 · 파일 이름에 붙고, 태그 · 메모는 진단 파일에 들어갑니다. 녹화 중에는 바꿀 수 없습니다. 입력값은 창에 저장되어 스크립트 컴파일(도메인 리로드) 뒤에도 남습니다.
- **저장 경로 · 최근 세션**
  - 저장 경로(`{프로젝트 루트}/Recording`)를 보여 주고 **Open Recording Folder** · **Open Screenshots**로 탐색기에서 엽니다.
  - **최근 녹화 세션 히스토리**는 생성 시각 최신순으로 5개까지 보여 주며 **폴더 열기**로 엽니다 (`Screenshots` 폴더 제외).
  - 목록은 창을 열 때 · 포커스를 받을 때 · 녹화가 끝날 때 · **새로고침**을 누를 때만 디스크에서 다시 읽습니다 (그릴 때마다 읽지 않음).
  - 저장 폴더는 저장할 때 · 여는 버튼을 누를 때 만듭니다.

### 스크립트 API (`HB_ScreenRecorder`, 에디터 전용 정적 클래스)

| 멤버 | 설명 |
|---|---|
| `bool StartRecording(int targetFps = 30, string issueTag = "", string notes = "")` | 녹화 시작. 이미 녹화 중이거나 Play Mode가 아니면 경고 로그 후 `false` |
| `void StopRecording()` | 녹화 정지 후 진단 파일 저장. 녹화 중이 아니면 아무것도 안 함 |
| `string TakeScreenshot(string issueTag = "", string notes = "")` | 스크린샷 요청 + 상태 · 로그 파일 저장. PNG 경로 반환 (PNG는 Game 뷰가 다음에 그릴 때 생김). 저장 폴더를 만들지 못하면 `null` |
| `bool IsRecording` | 녹화 중인지 (Recorder 컨트롤러 상태까지 확인) |
| `float ElapsedTime` | 녹화 시작 후 경과 시간(초). 녹화 중이 아니면 0 |
| `string CurrentSessionFolder` | 현재(마지막) 녹화 세션 폴더 |
| `string GetRecordingRootPath()` | `{프로젝트 루트}/Recording` 경로 (폴더를 만들지 않음) |
| `string GetRecordingRootDirectory()` | `{프로젝트 루트}/Recording` 경로. 없으면 만듭니다 |
| `string GetGameViewResolution()` | Game 뷰 렌더 해상도 (`가로x세로`) |
| `void OpenFolder(string path)` | 폴더를 탐색기에서 엶. 없는 폴더면 `Recording` 루트를 엶 |

## 메뉴 경로
- `Tools > HB > Screen Recorder` (창 제목 `HB Recorder`)

## 의존성 (Dependencies)
- `com.unity.recorder` 5.1.7: `package.json` 의존성이라 이 패키지를 설치하면 함께 설치됩니다. 에디터 어셈블리(`HB.ScreenRecorder.Editor`)가 `Unity.Recorder.Editor`를 참조합니다.
  - Recorder가 없으면 에디터 어셈블리가 컴파일되지 않습니다(이 패키지를 Package Manager로 설치하면 의존성으로 함께 들어오므로 생기지 않음). 예전의 미설치 배너 · 설치 버튼과 `IsRecorderAvailable()`은 실행될 일이 없어 없앴습니다.
- 선택: `com.hb.ingameconsole` — 설치되어 있으면 로그 덤프에 콘솔 로그가 들어갑니다 (컴파일 참조 없음).
