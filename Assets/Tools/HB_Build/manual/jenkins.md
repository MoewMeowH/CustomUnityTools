# Jenkins

- **Jenkinsfile (Groovy) 생성 및 CLI 연동**:
  - 에디터 설정값을 바탕으로 Jenkins Declarative Pipeline 스크립트를 생성합니다.
  - CLI 환경에서 `-executeMethod HB.CustomBuild.Editor.ProjectBuild.JenkinsBuild` 호출을 통한 배치를 지원합니다.
  - 창의 CLI 명령어 생성기는 같은 명령을 복사하거나 `.bat` · `.ps1`로 저장합니다. `.ps1`은 Unity 종료를 기다린 뒤 종료 코드를 그대로 돌려줍니다. `.bat`은 UTF-8(BOM 없음)로 쓰고 첫머리에서 `chcp 65001`로 코드 페이지를 맞춰 한글 경로가 깨지지 않게 합니다 (끝나면 원래 코드 페이지로 되돌림).
  - 생성한 Jenkinsfile은 `skipDefaultCheckout()`로 기본 checkout을 끄고 `Checkout SCM` 단계에서 한 번만 받습니다. 명령줄은 인자 하나마다 줄을 바꿉니다 (값 안의 ` -`는 그대로).
  - 소스를 받는 방식은 두 가지입니다 (창의 `Jenkins 체크아웃`): job의 SCM 설정으로 워크스페이스에 받는 `Scm`, 빌드 PC의 고정 작업 폴더를 git으로 맞추는 `LocalFolder` (아래 "로컬 폴더 모드").
- **Jenkinsfile**: 생성된 파이프라인은 빌드 전에 `BuildOutput/` 폴더만 지웁니다 (`Library/` 캐시는 유지). 소스 정리는 SCM 모드면 Jenkins job의 checkout 설정을, 로컬 폴더 모드면 생성된 `Update Source (git)` 단계를 따릅니다 (아래).
  - 생성하면 클립보드에 복사하고, 프로젝트 루트에 `Jenkinsfile`로 저장할지 묻습니다.
  - 단계: `Checkout SCM`(로컬 폴더 모드는 `Update Source (git)`) → `Clean Build Output` → `Unity Build Execution` → `Archive Artifacts`(`BuildOutput/**`). 끝나면 항상 경고 파일을 확인하고 `build.log`를 보관합니다.
  - 명령은 Unity 에디터를 직접 호출합니다. 경로는 에이전트 환경변수 `UNITY_PATH`, 없으면 `ProjectSettings/ProjectVersion.txt`의 버전으로 `${UNITY_EDITORS_ROOT}/<버전>/Editor/Unity.exe`(기본 `C:/Program Files/Unity/Hub/Editor`, macOS는 `/Applications/Unity/Hub/Editor/<버전>/Unity.app/...`)를 씁니다. Jenkinsfile에 경로를 박지 않으므로 Unity를 올려도 다시 생성할 필요가 없습니다. Unity Hub 헤드리스 CLI에는 프로젝트를 실행하는 명령이 없습니다.
  - 자유 입력 값은 명령줄에 들어가므로 검사합니다: `BUILD_VERSION` · `VERSION_CODE`는 영문 · 숫자 · `.` · `_` · `-`만(`-`로 시작 불가), 프로필 경로는 글자 · 숫자 · 공백 · `.` · `_` · `/` · `-`로 된 `.asset` 경로만 통과합니다.
  - 프로필 모드는 생성 시점의 "프로필 경로 → 플랫폼" 표로 `-buildTarget`을 넘깁니다. 프로필의 플랫폼을 바꾸면 Jenkinsfile을 다시 생성하세요 (그대로 두면 배치 빌드가 플랫폼 불일치로 실패).
  - 목록(`BUILD_PROFILE`)에 없는 프로필(빌드 PC 전용 프로필 등)은 `BUILD_PROFILE_PATH`에 경로를, `BUILD_TARGET`에 플랫폼을 직접 넣습니다. 플랫폼이 틀리면 배치 빌드가 불일치로 실패합니다.
  - 프로필 모드의 Jenkins 빌드는 프로필의 출력 경로 대신 `BuildOutput/<플랫폼>`에 만듭니다 (정리 · 보관 대상과 일치).
  - Linux 에이전트는 `UNITY_PATH`를 반드시 설정하세요 (기본 경로 찾기는 Windows · macOS Hub 경로만 지원).
  - **전체 풀기 모드**: `BUILD_TARGET` · `BUILD_TYPE` · `BUILD_VERSION` · `VERSION_CODE`와 `DEV_BUILD` · `DEEP_PROFILE` · `CLEAN_BUILD` · `IS_AAB` · `ZIP_PACKAGE`(booleanParam)가 Jenkins 파라미터입니다. 기본값은 생성한 사람의 창과 무관한 안전값(첫 선택지, 체크 모두 끔, 버전 · 버전 코드 빈 값)이고, Jenkins에서 바꾼 값이 우선합니다. `BUILD_VERSION`을 비우면 PlayerSettings의 버전을 씁니다. 씬 목록과 비활성 스텝은 생성 시 창 값으로 고정됩니다.
  - **프로필+오버라이드 모드**: 기본은 고른 프로필(`BUILD_PROFILE`)의 값입니다. Jenkins 파라미터 `BUILD_TYPE` · `DEV_BUILD`(`(profile)`이 아닌 값), `BUILD_VERSION` · `VERSION_CODE`(비어 있지 않은 값)를 넣은 것만 인자로 넘겨 프로필 값을 덮어씁니다.
  - **로컬 폴더 모드** (`Jenkins 체크아웃` = `LocalFolder`): 빌드 PC에 프로젝트 작업 폴더를 두고 Jenkins가 배치 git 명령으로 갱신한 뒤 그 폴더에서 빌드합니다.
    - job: 생성한 내용을 **Pipeline script**(SCM 없음)에 붙여 넣습니다. **job마다 폴더 하나**를 쓰세요 (예: 플랫폼별 job · 폴더). 같은 job의 동시 실행은 `disableConcurrentBuilds()`로 막지만, 서로 다른 job이 같은 폴더를 쓰는 것은 막지 못합니다.
    - 창 입력: **빌드 PC 프로젝트 폴더**(필수, Jenkinsfile에 상수로 들어감), 기본 브랜치, **에이전트 라벨**(그 폴더가 있는 노드. 비우면 `agent any` — 노드가 하나일 때만 쓰세요).
      - 폴더는 빌드 파라미터로 열지 않습니다. 빌드 권한만으로 같은 PC의 다른 git 폴더를 지정해 지우는 일을 막기 위해서입니다. 폴더를 바꾸려면 Jenkinsfile을 다시 생성하세요.
      - 끝의 `\` · `/`는 떼고, 네트워크 경로(UNC, `\\` · `//`로 시작) · `..` 구간 · 허용 밖 문자(글자 · 숫자 · 공백 · `. _ : \ / ( ) -` 외)가 있으면 생성하지 않습니다. 한글 경로는 bat 코드 페이지에 따라 깨질 수 있어 확인 전까지 **ASCII 경로를 권장**합니다.
      - Unity 프로젝트가 **저장소 루트**에 있어야 합니다 (`<폴더>/.git` 확인). 저장소 하위 폴더 구조는 지원하지 않습니다.
    - 파라미터: `BRANCH`(origin 기준 브랜치, 비우면 폴더의 현재 브랜치. 영문 · 숫자 · `. _ / -`만, `-`로 시작 · `..` 불가), `GIT_REF`(**태그**, 또는 원격 브랜치 · 태그에 들어 있는 **7~40자리 커밋 해시**. 넣으면 `BRANCH`보다 우선. 브랜치 이름 · `origin/x` · `HEAD` 등은 실패 — 로컬의 오래된 상태를 빌드하지 않게).
    - 첫 준비는 사람이 합니다: `git clone` → `git lfs install` → Unity로 한 번 열어 임포트. 폴더가 없거나 git 작업 폴더가 아니면 clone하지 않고 실패합니다.
      - Jenkins 서비스 계정이 폴더를 만든 계정과 다르면 git이 "dubious ownership"으로 거부합니다. 서비스 계정에서 `git config --global --add safe.directory <폴더>`를 해 두세요. LFS 필터는 단계가 `git lfs install --local`로 저장소에 직접 겁니다.
    - `Update Source (git)` 단계: `git lfs install --local` → 오래된(10분 넘은) `.git/index.lock` 삭제 → `git fetch origin --prune --prune-tags --tags --force`(원격에서 지운 태그는 로컬에서도 지움) → 버려질 것을 로그에 출력(`git status --porcelain`, HEAD와 로컬 브랜치 중 어느 원격에도 없는 커밋, `git clean -nd`) → `git checkout -f -B <BRANCH> origin/<BRANCH>`(또는 `--detach <커밋>`) → `git clean -fd` → (`.gitmodules`가 있으면 서브모듈 sync · 갱신 · 정리 · LFS) → `git lfs pull` → `git status`가 비어 있지 않으면 실패. 빌드한 커밋은 Jenkins 빌드 설명과 `BuildOutput/BUILD_COMMIT.txt`에 남습니다.
    - **빌드 PC 폴더에서 직접 고친 내용 · 로컬 커밋 · 추적되지 않는 파일은 다음 빌드에서 사라집니다.** 고칠 것은 커밋하세요. 빌드 PC 전용 파일(라이브 프로필 등)은 `.gitignore`에 넣으면 남습니다 — **`.asset`과 짝인 `.meta`(폴더째 무시하면 `폴더.meta`까지) 함께** 무시하세요. `.meta`를 함께 무시하지 않으면 그 `.meta`가 매 빌드 지워져 GUID가 바뀌고, 그 에셋을 참조하던 것이 끊깁니다.
    - `git clean -fd`는 `-x` 없이 실행하므로 `Library/` 등 무시된 파일은 남습니다 (재임포트 없음). `-fdx`는 `Library/`까지 지웁니다.
    - 갱신 뒤 `git status`가 비어 있지 않으면(줄바꿈 변환 · LFS 문제 · 브랜치를 바꿔 사라진 서브모듈 폴더 등) 실패합니다. 검사를 느슨하게 하지 말고 저장소를 정리하세요 (`.gitattributes` · 재정규화, 남은 서브모듈 폴더는 직접 삭제).
    - 에이전트 요구: `git` · `git-lfs`, Jenkins 서비스 계정에서 쓸 수 있는 git 인증. 모든 단계가 그 폴더(`dir(...)`) 안에서 실행되고, Jenkins가 폴더 옆에 `<폴더>@tmp`(배치 실행용 임시 폴더)를 만듭니다.
    - **macOS 빌드 PC** (확인 전, 추정 포함): 노드가 둘 이상(예: Windows + Mac mini)이면 **에이전트 라벨을 꼭** 넣으세요. launchd로 띄운 Jenkins는 PATH가 짧아 Homebrew의 `git-lfs`(`/opt/homebrew/bin`)를 못 찾으므로 노드 환경 변수 PATH에 추가하세요 (없으면 첫 단계 `git lfs version`에서 실패). 프로젝트 `.gitignore`에 `.DS_Store`를 넣고, 한글 파일 이름이 있으면 `git config core.precomposeunicode true`를 확인하세요 (아니면 갱신 뒤 상태 검사에서 실패할 수 있음).
    - **권한 주의**: 이 job의 빌드 권한이 있으면 origin의 아무 브랜치 · 커밋을 빌드할 수 있고, Unity 배치 빌드는 그 코드의 에디터 스크립트를 빌드 PC의 서비스 계정으로 실행합니다. 빌드 권한은 그 브랜치에 push할 수 있는 사람과 같은 범위로 두세요.

  | 파라미터 | 순수 프로필 | 전체 풀기 | 프로필+오버라이드 |
  |---|---|---|---|
  | `BUILD_PROFILE` | 선택 (생성 PC의 프로필, 고른 프로필이 첫 항목) | - | 선택 |
  | `BUILD_PROFILE_PATH` | 문자열 (비우면 `BUILD_PROFILE`) | - | 문자열 |
  | `BUILD_TARGET` | `(profile)` · `StandaloneWindows64` · `Android` · `iOS` | `StandaloneWindows64` · `Android` · `iOS` | `(profile)` · … |
  | `BUILD_TYPE` | - | `DEV` · `QA` · `LIVE` | `(profile)` · `DEV` · `QA` · `LIVE` |
  | `DEV_BUILD` | - | booleanParam (false) | `(profile)` · `true` · `false` |
  | `BUILD_VERSION` · `VERSION_CODE` | - | 문자열 (빈 값) | 문자열 (비우면 프로필 값) |
  | `DEEP_PROFILE` · `CLEAN_BUILD` · `IS_AAB` · `ZIP_PACKAGE` | - | booleanParam (false) | - |

## 운영 가이드: 라이브 프로필
- 프로필 에셋은 로컬 빌드와 Jenkins가 같이 쓰므로, 테스트하려고 바꾼 값(검증 스텝 끄기, Development Build 등)이 커밋되면 라이브 빌드에도 들어갑니다.
- 라이브 프로필은 빌드 PC 전용 폴더(예: `Assets/Editor/HBBuild/Profiles/BuildMachine/`)에 두고 그 폴더를 프로젝트 `.gitignore`에 등록하세요. Jenkins 워크스페이스는 `BuildOutput/`만 지우므로 빌드 PC에 둔 프로필이 유지됩니다 (Jenkins job의 checkout에서 `git clean -fdx`처럼 무시 파일까지 지우는 옵션을 쓰면 함께 지워지니 주의). Jenkins에서는 `BUILD_PROFILE_PATH`에 이 경로를, `BUILD_TARGET`에 플랫폼을 넣어 빌드합니다 (개발자 PC에서 만든 Jenkinsfile 목록에는 이 프로필이 없음).
