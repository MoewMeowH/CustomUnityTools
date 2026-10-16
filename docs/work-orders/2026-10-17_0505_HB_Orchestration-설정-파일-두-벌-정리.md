# 작업 지시서: HB_Orchestration 설정 파일 두 벌(publish 설정 · project-files 템플릿) 정리

## 목적
1) publish가 사용 중 설정(Publish_Output의 modes.json · appsettings.json)을 지키느라, 원본에 새로 생긴 모드·역할·필드가 사용 중 설정에 들어가지 않는다. 사용 중 값은 덮지 않으면서 publish 때 이 차이를 알리거나 합치는 방법을 정하고 구현한다. appsettings.json도 같은 구조인지 확인하고, 같은 방식을 적용할지 정한다.
2) project-files를 손으로 폴더째 복사해도 .mcp.json · .claude/settings.json이 덮이지 않게 한다. 지금은 보호 규칙이 스크립트 안에만 있어서, 이 규칙을 파일 이름 자체에 드러낸다. 템플릿 이름 변경(.example 등)과 Unity 프로젝트 .gitignore에 .mcp.json을 넣는 것을 검토한다.

대상: HB_Orchestration 저장소 (F:/AI/ToolWithCS/Orchestration/HB_Orchestration, ExtraRoots 이름 `@orch`)
- `HB_Orchestration.bat:140-216` (:publish. 설정 백업 180-190, 복원 192-204)
- `src/HB.Orchestration.Mcp/HB.Orchestration.Mcp.csproj:16-24` (`CopyToPublishDirectory="Never"`)
- `src/HB.Orchestration.Mcp/modes.json`, `src/HB.Orchestration.Mcp/appsettings.json` (원본)
- `src/HB.Orchestration.Dashboard/ModesConfigService.cs` (publish 폴더 설정 편집, JsonNode로 모르는 필드 보존, 77·177-208줄)
- `src/HB.Orchestration.Agents/ModeStore.cs:23-33` (modes.json 찾는 순서)
- `tools/Sync-ProjectFiles.ps1:1-24, 95-142` (동기화 / 확인만 / 제외 규칙)
- `project-files/.mcp.json`, `project-files/.claude/settings.json`
- `README.md:53-56`(publish), `319-345`(5. Unity 프로젝트 쪽), `412-440`(8. bat)
- Unity 프로젝트 쪽 `.gitignore` (예: F:/AI/ToolCreate)

## 진행 방식
- 대상 프로젝트에서 이 기능을 연결할 위치를 검토한다.
- 코드를 먼저 조사하고 계획서만 낸다: docs/plans/YYYY-MM-DD_제목.md. 승인 전에는 코드를 고치지 않는다.
- 아래 의심 지점은 확인 전 추정이다. 확정 / 기각 / 범위 조정을 근거와 함께 계획서에 적는다.
- 항목마다 구현 → 컴파일 확인 → 테스트 → 커밋.
- 위험이 큰 항목(의심 지점 1~4: 설정 병합 로직)은 review로 검토받는다. reason 필수. ownReview는 결과를 보기 전에 네가 직접 찾은 것으로 채운다.
  - 아래 의심 지점과 그외 이상사항은 에이전트 input에 넣지 않는다 (독립성 유지).

## 원칙
- 옵션은 비워 두면 기존 동작과 같다. 여기서 "기존 동작"은 **publish가 사용 중 설정 파일의 내용을 바꾸지 않는다**는 뜻이다. 차이 알림을 출력하는 것은 이 원칙과 충돌하지 않는다.
- 사용 중 설정의 값은 어떤 경우에도 원본 값으로 덮지 않는다. 합치기를 넣는다면 "사용 중 설정에 없는 경로를 추가"만 한다. 같은 경로에서 값이 다르거나 자료형이 다르면 덮지 않고 알림만 한다.
- 합치기를 실행하면 쓰기 전에 백업한다. 이 백업은 실행할 때마다 지워지는 곳에 두지 않는다. 지금 `%TEMP%\HB_Orchestration_cfg_backup`은 publish마다 `rmdir`된다(bat:182-183). 무엇을 추가했는지는 JSON 경로 단위로 출력한다.
- 보호 규칙은 스크립트·문서에만 두지 않고 파일 이름으로 드러낸다. 손으로 복사해도 지켜져야 한다.

## 의심 지점 (추정)
1. `HB_Orchestration.bat:192-204` — 복원 로직은 "백업이 있으면 백업으로 되돌리고, 없을 때만 원본을 복사"한다. 그래서 원본에 새로 생긴 키는 영원히 들어가지 않는다.
   - 근거: 사용자 보고, 그리고 `docs/ch5_worklog.md:85,235`(RequireOwnReview를 publish 폴더에 손으로 넣은 기록)와 일치한다.
   - 조사 시점(2026-10-17 05시) 상태: modes.json은 원본과 publish의 키 경로 차이가 없다. 손으로 이미 맞춘 것으로 보인다. appsettings.json은 publish에만 있는 키가 있다(`Context.ExtraRoots`, `Debug`, `Providers.*.TimeoutSeconds`).
   - 확인 방법: 원본에 임시 키를 넣고 publish해서 재현한다.
2. 병합 판정 — 두 파일만 보고는 "원본에만 있는 키"가 새로 추가된 키인지, 사용자가 대시보드·메모장에서 일부러 지운 키인지 구분할 수 없다.
   - 계획서에서 정할 것: 마지막 publish 때의 원본 스냅샷(기준선)을 남겨 3-way로 판정할지, 2-way 비교에 알림만 할지.
   - 2-way로 하면 사용자가 지운 모드를 publish 때마다 다시 알리게 되어 알림이 소음이 된다.
3. 병합 단위와 충돌 규칙 — modes.json의 `Roles` · `Modes`는 이름→객체 딕셔너리라 키 단위로 합치기 쉽다. `Steps`는 배열이고, appsettings.json의 `Agents`는 `Name`으로 구분하는 배열이다(`ModeDefinitions.cs`).
   - 범위 후보: 배열은 원소 단위로 합치지 않고, 배열 전체가 원본에만 있을 때만 추가한다.
   - 정할 것: 같은 경로의 자료형이 다를 때, 사용 중 값이 `null`일 때, 배열 원소가 다를 때 어떻게 할지. 기본은 덮지 않고 알림이다.
4. appsettings.json — modes.json과 같은 백업·복원 루프에 들어 있다(bat:189, 193). 그래서 같은 문제가 있다.
   - 다른 점: Agents · Pricing은 사용자가 바꾸는 값이고, ApiKeyEnv · Endpoint가 섞여 있어 자동으로 합치면 modes.json보다 위험하다.
   - 계획서에서 정할 것: appsettings.json은 알림만 하고 합치기는 modes.json에만 할지.
5. 구현 위치 — bat 안의 PowerShell 한 줄로는 JSON을 합치기 어렵다.
   - 후보: 별도 스크립트(tools/*.ps1), 또는 C# 도구. 대시보드의 ModesConfigService는 이미 JsonNode로 모르는 필드를 보존하며 저장한다.
   - 기준: 테스트 가능성. 확인 결과 `tests/`에는 Sync-ProjectFiles.ps1을 부르는 테스트가 없다. C#은 기존 테스트 프로젝트에서 테스트할 수 있다.
   - 같이 볼 것: `dotnet publish`를 단독으로 실행했을 때와 bat으로 실행했을 때 결과가 어떻게 다른지(csproj:21-22 `Never`는 bat이 있어야 첫 복사가 된다). 알림은 bat에만 넣어도 되는지 정한다.
6. `ModeStore.cs:23-33` — modes.json 찾는 순서에서 `ModesPath`, `HB_MODES_PATH`, Unity 루트의 `hb-modes.json`이 publish 폴더보다 앞선다. 이 경우 publish 폴더 modes.json을 합쳐도 실제로 쓰이는 파일은 그대로다.
   - **기본 범위 밖.** 계획서에는 "이 경우 알림이 실제 사용 파일과 다를 수 있음"이라고 적기만 한다.
7. 대시보드 — 대시보드도 publish 폴더 modes.json을 편집한다. 원본과의 차이를 대시보드에서 보여 주는 것도 대안이다.
   - **기본 범위 밖** (요청은 publish 시점 처리). 대안으로만 기록한다.
8. `project-files/.mcp.json`, `project-files/.claude/settings.json` — 실제 파일과 이름이 같아서 폴더째 복사하면 덮인다.
   - 이름을 바꾸면 같이 고칠 곳: `Sync-ProjectFiles.ps1:10-11, 114`(checkOnlyFiles), 템플릿 이름 ↔ Unity 실제 이름의 비교 경로 매핑, README 5절 목록.
   - 이름 후보: 끝에 `.example`을 붙이면 편집기의 JSON 강조가 사라진다. `mcp.example.json` 형태도 후보다.
   - 계획서에 명시할 것: Unity 쪽에 실제 파일이 없을 때 비교 결과와 `-Apply` 동작. 폴더째 복사했을 때 `.example` 파일이 Unity 쪽에 생기는 것을 허용할지.
9. `README.md:320` — "`project-files/` 내용을 Unity 프로젝트 루트로 복사:"라는 문구가 폴더째 복사하라는 안내로 읽힌다. 이번 사고의 직접 원인 후보다.
   - 확인할 것: 첫 설치 순서를 "스크립트 `-Apply` → 템플릿을 실제 이름으로 복사하고 경로 수정"으로 바꿀지.
10. 첫 설치 — Unity 쪽에 .mcp.json이 없을 때 스크립트가 템플릿으로 만들어 줄지, "없음, 만드세요"라고 안내만 할지 정한다. 자리표시를 그대로 두면 연결이 실패한다.
    - 후보: Unity 쪽 .mcp.json에 `<publish 폴더>` 자리표시가 남아 있으면 스크립트가 감지해 경고한다.
11. Unity 프로젝트 .gitignore에 `.mcp.json` 넣기 — F:/AI/ToolCreate에서 확인한 사실:
    - .mcp.json은 추적되지 않는다(untracked). `.gitignore`에도 없다.
    - `.claude/settings.json`도 추적되지 않는다.
    - `.claude/settings.local.json`은 사용자 전역 git ignore로 무시된다.
    - .mcp.json은 기기별 절대 경로와 API 키 환경 변수 참조를 담는다. 확인할 것: ignore하면 팀원에게 어떤 영향이 있는지 (Claude Code 문서로 확인).
    - `claude mcp add --scope local`(저장소 밖에 기기별로 저장)은 ignore의 대안으로 **비교만 하고 이번 구현 범위에 넣지 않는다.**
    - 각 Unity 저장소의 .gitignore를 고치는 것은 HB_Orchestration 밖의 변경이라 따로 승인받는다.
12. 현재 작업 트리 — HB_Orchestration에 커밋하지 않은 변경이 있다. README, ModeRunner.cs, OrchestrationOptions.cs, modes.json, Sync-ProjectFiles.ps1, project-files/.claude/commands/work-order.md, project-files/.claude/scripts/, WorkOrdersFolderTests.cs이고, 모두 work_order 모드 작업이다.
    - 이번 작업의 대상 파일(modes.json, Sync-ProjectFiles.ps1, README)과 겹친다. 착수 전에 사용자에게 먼저 커밋할지 묻는다. 커밋을 전제하지 않는다.

## 함께 확인할 것
- 이 지시서는 work_order(gpt_mini_luna · gemini_main_flash)로 검토받았다 (runId ecdfa41e5c).
- 사용자에게 물을 것:
  - 합치기 기능을 넣을지. 넣는다면 옵션으로 켤지, 기본으로 켤지. 원칙에 따라 기본안은 "파일은 바꾸지 않고 알림만"이다.
  - 사용자가 일부러 지운 모드·역할을 어떻게 다룰지: 기준선을 둔 3-way 판정, 또는 매번 알림.
  - appsettings.json도 합치기 대상인지, 알림만 할지.
  - 템플릿 이름 형식: `.mcp.json.example` 또는 `mcp.example.json` 등.
  - Unity 프로젝트 .gitignore에 .mcp.json을 넣을지 (각 Unity 저장소에서 따로 승인).
  - HB_Orchestration의 커밋하지 않은 work_order 변경을 먼저 커밋할지.
- 계획서에서 답할 것 (완료 기준):
  - (a) 원본에만 새 모드·역할·필드를 넣고 publish하면 그 JSON 경로가 출력된다. 합치기를 켰다면 결과 파일에 그 경로가 실제로 추가되고, 백업이 남는다.
  - (b) 사용 중 설정에서 값이 바뀐 키, 자료형이 다른 키, 사용자가 지운 키(3-way일 때)는 publish 뒤에도 그대로다.
  - (c) 대시보드가 저장한 파일(JsonNode로 다시 직렬화해서 순서·포맷이 바뀐 것)에서도 (a)·(b)가 성립한다.
  - (d) 처음 publish할 때(publish 폴더에 설정이 없을 때) 원본이 그대로 복사되는 기존 동작이 유지된다.
  - (e) project-files를 폴더째 Unity에 복사해도 Unity 쪽 .mcp.json · .claude/settings.json의 SHA-256이 그대로다. Unity 쪽에 그 파일이 없을 때 어떻게 되는지도 적는다.
  - (f) Sync-ProjectFiles.ps1은 자동 테스트가 없다. 이름 변경 뒤 비교만 / `-Apply` / 대상 파일 없음 세 경우를 임시 폴더로 직접 실행해 확인하고, 출력을 작업 로그에 남긴다.

## 그외 이상사항
- 반영: "옵션 비우면 기존 동작"과 "알림 출력"이 모호하다 — 원칙 첫 줄에 "기존 동작 = 파일 내용 불변, 알림 출력은 허용"이라고 정의했다. 목적 1에서 "알림이 기본"이라고 확정한 표현은 빼고 사용자 질문으로 옮겼다. (공통 / luna · flash · Claude)
- 반영: 백업 위치 — bat:182-183이 publish마다 `%TEMP%` 백업을 `rmdir`하는 것을 확인했다. 원칙에 "실행할 때마다 지워지는 곳에 두지 않는다"를 추가했다. (luna)
- 반영: 자료형 충돌 · null · 배열 원소 차이에 대한 규칙이 없다 — 의심 지점 3과 원칙 2에 추가했다. (luna)
- 반영: 완료 기준 보강 — 합치기를 켰을 때의 결과 파일과 백업, 충돌 키 보존, 대시보드 저장 파일과의 호환, 첫 publish, Unity 쪽에 파일이 없을 때를 (a)~(e)로 넣었다. (luna · Claude)
- 반영: Sync-ProjectFiles.ps1 테스트 위치가 빠져 있다 — 확인해 보니 `tests/`에 이 스크립트를 부르는 테스트가 없다. 완료 기준 (f)를 직접 실행 확인으로 바꾸고, 의심 지점 5에 근거를 적었다. (flash)
- 반영: 의심 지점 6·7·11(local scope)은 범위를 넘어간다 — 세 곳에 "기본 범위 밖 / 비교만"이라고 명시했다. (공통 / luna · flash · Claude)
- 반영: csproj의 `Never`와 bat의 역할 분담(`dotnet publish`를 단독으로 실행했을 때 결과가 다름) — 의심 지점 5에 합쳤다. (flash)
- 반영: 템플릿 이름을 바꿨을 때 Unity 쪽에 파일이 없는 경우, 그리고 `.example` 파일이 Unity에 복사되어 생기는 경우의 동작 — 의심 지점 8에 추가했다. (luna)
- 반영: 커밋하지 않은 변경 — 선커밋을 전제하지 않고 사용자에게 묻는다고 고쳤다. 이번 작업 대상 파일과 겹친다는 것도 적었다. (luna · flash)
- 반영: runId 자리표시 — 값을 채웠다. (luna)
- 기각: "자료 날짜가 시스템 날짜(2026-10-16)와 다르다" — 이 세션 환경의 날짜는 2026-10-17이고, 셸 `date`로도 2026-10-17 05:05를 확인했다. (luna)
- 기각: "계획서 · 승인 · 항목별 커밋 · ownReview 절차는 요청에 없는 추가 절차" — CLAUDE.md의 "계획서와 작업 로그"와 "외부 에이전트" 규칙에 있는 저장소 공통 절차다. (luna)
- 기각: "ownReview를 어떤 양식으로 남길지 모호하다" — CLAUDE.md에 `run_mode`의 `ownReview` 인자로 넣는다고 정해져 있다. 이 지시서의 진행 방식도 같은 방식이다. (flash)
- 기각: "템플릿 안에 '직접 수정 금지' 주석을 넣는다" — JSON에는 주석을 넣을 수 없다. 이름을 바꾸는 것만으로 덮어쓰기 경로가 사라지므로, 이 대책으로 충분한지는 의심 지점 8·9에서 함께 판단한다. (flash)
- 사용자 판단 필요: 합치기를 넣을지와 그 기본값 — flash는 "기본은 알림만으로 확정하고, 합치기 옵션만 묻자"고 했다. luna는 "사용자가 알림과 합치기 둘 다 검토해 달라고 했으니 기본값을 확정하지 말자"고 했다. 이 지시서는 파일을 바꾸지 않는 쪽을 기본안으로 두고, 최종 결정은 사용자에게 묻는다.
