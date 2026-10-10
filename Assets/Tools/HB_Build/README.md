# HB Custom Build Manager

Unity 멀티플랫폼 커스텀 빌드 매니저 및 **Jenkins CI/CD 자동화 파이프라인 (Groovy)** 지원 도구입니다.

## 주요 기능
- **멀티플랫폼 지원**: Windows 64bit, Android (APK/AAB), iOS 지원
- **환경별 빌드 모드**: DEV, QA, LIVE 모드 분기 및 Scripting Define Symbols 연동
- **로컬 원클릭 빌드**: 에디터 내에서 설정 후 즉시 플레이어 빌드 수행
- **Jenkinsfile (Groovy) 원클릭 생성**:
  - 에디터 내 설정값을 바탕으로 Jenkins Declarative Pipeline 스크립트를 자동 생성
  - 클립보드 복사 및 프로젝트 루트에 `Jenkinsfile` 파일 저장 지원
- **CLI 커맨드라인 빌드 인터페이스**:
  - Jenkins, GitHub Actions 등에서 `-executeMethod ProjectBuild.JenkinsBuild` 호출을 통한 헤드리스 빌드 지원

## Jenkins 연동 방법

### 1. Unity CLI 빌드 명령어
```bash
<Unity.exe 경로> -batchmode -quit -projectPath "<프로젝트 경로>" \
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

### 2. Jenkinsfile (Groovy) 자동 생성
1. Unity 상단 메뉴 `Tools > HeartBeat > Custom Build Manager` 열기
2. 플랫폼, 버전, 빌드 옵션, 씬 목록 지정
3. **[Generate Jenkinsfile (Groovy Pipeline 생성)]** 버튼 클릭
4. 프로젝트 루트에 생성된 `Jenkinsfile`을 Git 저장소에 커밋/푸시하여 Jenkins 파이프라인 잡(Job)에 연결

## UPM 설치 방법 (내 다른 프로젝트에서 가져오기)
Unity Package Manager 창에서 **Add package from git URL...** 선택:
```text
https://github.com/MoewMeowH/CustomUnityTools.git?path=Assets/Tools/Build
```
또는 로컬 디스크 `package.json` 참조:
```json
{
  "dependencies": {
    "com.hb.custombuild": "file:F:/AI/ToolCreate/Assets/Tools/Build"
  }
}
```
