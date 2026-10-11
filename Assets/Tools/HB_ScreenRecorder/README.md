# HB_ScreenRecorder

Unity Recorder 기반 에디터 화면 녹화, 스크린샷 캡처 및 QA 진단 데이터(시스템 상태 및 로그) 덤프 도구입니다.

## 주요 기능
- **MP4 비디오 녹화**: `UnityEditor.Recorder`를 제어하여 GameView 화면을 MP4 포맷으로 녹화합니다.
- **스크린샷 캡처**: 현재 화면을 PNG로 즉시 캡처합니다.
- **진단 데이터 자동 덤프**: 녹화 종료 또는 스크린샷 캡처 시 당시의 씬/메모리/디바이스 상태(`Diagnostic_State.json`), 요약 리포트(`QA_BugReport_Summary.txt`), 그리고 인게임 콘솔 로그(`Diagnostic_Logs.txt`)를 프로젝트 루트 `Recording/` 하위 세션 폴더에 함께 저장합니다.
- **느슨한 결합(Loose Coupling) 로그 연동**: 리플렉션을 통해 프로젝트 내 `HB_Log`(`HB.InGameConsole`) 존재 여부를 확인하고, 설치되어 있을 경우 최근 로그 및 스택 트레이스를 추출합니다.

## 메뉴 경로
- `Tools > HB > Screen Recorder`

## 의존성
- `com.unity.recorder` (미설치 시 에디터 윈도우에서 Package Manager `Client.Add`를 통한 설치 지원)
