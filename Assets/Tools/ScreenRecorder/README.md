# HB Screen Recorder

Unity Recorder 기반 에디터 화면 녹화, 스크린샷 및 QA 진단 덤프(시스템 상태/로그) 도구입니다.

## 주요 기능
- **MP4 비디오 녹화**: 에디터 및 GameView 화면을 고화질 MP4로 녹화
- **즉시 스크린샷 캡처**: 단축키/버튼 클릭으로 즉시 스크린샷 캡처
- **QA 진단 덤프**: 녹화 종료 또는 스크린샷 시 시스템 상태(JSON) 및 콘솔 로그(TXT) 자동 수집
- **원클릭 폴더 열기**: 프로젝트 루트 `Recording/` 디렉터리에 체계적으로 저장

## 설치 방법 (Unity Package Manager)

### 방법 1: Git URL로 설치 (추천)
Unity Package Manager 창 (`Window > Package Manager`)에서 `+` 버튼 클릭 후 **Add package from git URL...** 선택:
```text
https://github.com/<사용자>/<저장소>.git?path=Assets/Tools/ScreenRecorder
```
*(또는 저장소 분리 시 해당 Git URL 입력)*

### 방법 2: 로컬 패키지로 설치 (Local Disk)
Unity Package Manager 창에서 **Add package from disk...** 선택 후 `package.json` 파일 선택:
```text
F:/AI/ToolCreate/Assets/Tools/ScreenRecorder/package.json
```
또는 대상 프로젝트의 `Packages/manifest.json`에 다음 라인 추가:
```json
{
  "dependencies": {
    "com.hb.screenrecorder": "file:F:/AI/ToolCreate/Assets/Tools/ScreenRecorder"
  }
}
```

## 의존성
- `com.unity.recorder` (4.0.0 이상): 패키지 임포트 시 자동 설치됩니다.
