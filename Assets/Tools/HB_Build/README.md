# HB_Build

Unity 멀티 플랫폼 커스텀 빌드 매니저 및 Jenkins CI/CD 파이프라인 연동 도구입니다.

---

## 📦 UPM 패키지 설치 (Installation)

```text
https://github.com/MoewMeowH/CustomUnityTools.git?path=Assets/Tools/HB_Build
```

---

## 주요 기능
- **멀티 플랫폼 빌드**: Windows 64bit, Android (APK/AAB), iOS 빌드를 지원합니다.
- **환경별 심볼 관리 (`ProjectDefine`)**: `DEV`, `QA`, `LIVE` 빌드 타입 및 플랫폼별 Scripting Define Symbols를 `ScriptableObject`로 관리합니다.
- **`TypeCache` 기반 빌드 스텝 파이프라인 (`BuildStepRegistry`)**:
  - 프로젝트 내 `IPreBuildStep`, `IPostBuildStep` 구현체를 자동 감지하여 `ExecutionOrder` 순으로 실행합니다.
  - 상속 관계에 있는 빌드 스텝이 있을 경우 파생 클래스가 부모 스텝을 자동으로 오버라이드하도록 필터링합니다.
- **Jenkinsfile (Groovy) 생성 및 CLI 연동**:
  - 에디터 설정값을 바탕으로 Jenkins Declarative Pipeline 스크립트를 생성합니다.
  - CLI 환경에서 `-executeMethod ProjectBuild.JenkinsBuild` 호출을 통한 배치를 지원합니다.

---

## CLI 빌드 예시
```bash
Unity.exe -batchmode -quit -projectPath "<ProjectPath>" \
  -executeMethod ProjectBuild.JenkinsBuild \
  -logFile "build.log" \
  -buildTarget StandaloneWindows64 \
  -buildType DEV \
  -buildVersion 1.0.0 \
  -versionCode 1 \
  -devBuild true \
  -cleanBuild false \
  -isAab false \
  -outputPath "BuildOutput"
```

## 에디터 메뉴 경로
- `Tools > HB > Build Manager`
