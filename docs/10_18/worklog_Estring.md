# HB_E2String 패키지 점검·수정 로그 (2026-10-18)

계획서: [docs/plans/2026-10-18_e2string-package-check.md](../plans/2026-10-18_e2string-package-check.md)

## 0. 기준
- 패키지: `Assets/Tools/HB_E2String` (1.0.0), 원본 생성기: `F:\AI\ToolWithCS\EString\Script\E2StringGenerator.cs`
- 패키지 DLL = 원본 `bin/Release/netstandard2.0/EString.dll` (SHA-256 `ca0d7e10…587e`, 확정). 원본 저장소 미커밋 변경 없음.

## 1. 실제 확인 (ToolTest6.3, 6000.3.23f1)

### 준비
- 백업: `Packages/manifest.json`(`48b5d225…b613`), `packages-lock.json`(`4931fbfa…8a04`) → 세션 scratchpad `tt63_backup/`
- manifest에 추가: `com.hb.e2string` = `…?path=Assets/Tools/HB_E2String#10c3f807d47bd20b2ba2bb48e7f9607364fa3d9c`
- 테스트 폴더 `Assets/E2StringCheck/` (asmdef마다 시나리오 분리, 실패 시나리오는 `autoReferenced:false`로 다른 어셈블리를 막지 않게)

| asmdef | 내용 | 대상 항목 |
|---|---|---|
| `E2Check.Attr` | 매니저 생성 결과와 같은 어트리뷰트 (글로벌 + `EString.Script`) | 공통 |
| `E2Check.Game` (→Attr) | `[E2String(All)] enum GameState` + 생성 멤버 호출 | 4 |
| (없음, Assembly-CSharp) | `[E2String(All)] enum CsState` + 호출 | 4 대조군 |
| `E2Check.Bad` (참조 없음) | `[E2String] enum BadState` | 1 |
| `E2Check.Ambig` (→Attr) | `Outer1.State`, `Outer2.State` + `"X".ToStateFast()` | 5 |
| `E2Check.Case` (→Attr) | `E2Check.Case.UI.Kind`, `E2Check.Case.ui.Kind` + 호출 | 6 |

- 실행: `Unity.exe -batchmode -nographics -quit -projectPath F:/AI/ToolTest6.3/ToolTest6.3` → 예상대로 컴파일 에러로 exit 1 (Bad/Ambig/Case).

### 결과 (모두 확정)
- **4번 — 문제 없음.** UPM 설치(`Library/PackageCache/com.hb.e2string@620dcf56ae2c/Plugins/EString.dll`)에서도 분석기가 프로젝트의 **모든** 컴파일 어셈블리에 붙는다.
  - `.rsp`에 `-analyzer:…EString.dll`이 있는 어셈블리: Assembly-CSharp(-Editor), E2Check.* 전부, HB.*, Unity.* 패키지 어셈블리 전부.
  - `E2Check.Game.dll`(asmdef)과 `Assembly-CSharp.dll`이 에러 없이 생성됨 → 생성 멤버(`ToStringFast`, `ToGameStateFast`, `Values`)가 실제로 만들어짐.
  - 문서(analyzer-scope-and-diagnostics, 6000.3)의 "root of Assets → predefined assemblies"와 다르다. 실제 동작 기준으로 README의 "별도 설정이 필요 없습니다"는 맞다.
  - 부수 관측: 생성기가 Unity 패키지 어셈블리 수십 개에도 돈다. 구문 필터(`EnumDeclarationSyntax` + 어트리뷰트 있음)가 가벼워 비용은 작다고 본다 (추정, 측정 안 함).
- **1번 — 재현.** `BadEnums.cs(4,6): error CS0246: The type or namespace name 'E2String' could not be found`. 어트리뷰트를 볼 수 없는 asmdef에서는 쓸 수 없다. 기본 위치(`Assets/Scripts/E2String`, asmdef 없음)에 만들면 어트리뷰트가 Assembly-CSharp에 들어가고, asmdef는 Assembly-CSharp를 참조할 수 없으므로 이 상태가 된다.
- **3번 — 재현.** `Assembly for Assembly Definition File 'Packages/com.hb.e2string/Runtime/HB.E2String.Runtime.asmdef' will not be compiled, because it has no scripts associated with it.`
- **5번 — 재현.** `AmbigEnums.cs(9,43): error CS0121: The call is ambiguous between … 'Outer1_StateExtensions.ToStateFast(string?, Outer1.State)' and 'Outer2_StateExtensions.ToStateFast(string?, Outer2.State)'`
- **6번 — 재현 (추정이 맞았음).** `warning CS8785: Generator 'E2StringGenerator' failed to generate source … ArgumentException … The hintName 'E2Check.Case.ui.KindExtensions.g.cs' of the added source file must be unique within a generator.` CS8785는 "It will not contribute to the output"이므로 그 어셈블리의 생성이 **전부** 사라진다.
  - 정정: 같이 나온 `CaseEnums.cs(17,49/76): error CS1061`은 근거가 아니다. 테스트 코드가 확장 클래스의 namespace(`E2Check.Case.UI`)를 `using`하지 않은 채 확장 호출을 해서, 생성이 되어도 나는 에러였다 (2차에서 발견, 정적 호출로 고쳐 재확인).

### 테스트 프로젝트 상태
- 수정 후 재확인을 위해 테스트 폴더와 manifest 변경을 **아직 되돌리지 않았다.** 작업이 끝나면 되돌리고 기준선과 비교한다.

## 2. 항목별 처리

결정은 계획서의 `결정`·`결정 2`·`결정 3` 절에 원문 인용.

| 항목 | 처리 | 파일 |
|---|---|---|
| 1 | 매니저가 어트리뷰트 옆에 `HB.E2String.Attributes.asmdef`(autoReferenced) 생성. 폴더가 이미 asmdef/asmref에 속하면 만들지 않음, 다른 스크립트가 옮겨지면 개수 알리고 물음, 같은 이름(`"name"` 기준) asmdef가 있으면 만들지 않음. 창에 어트리뷰트의 어셈블리 표시, Assembly-CSharp면 경고 | `E2StringManagerWindow.cs` |
| 2 | 설치된 상태에서도 **다시 생성 (덮어쓰기)** Foldout으로 같은 자리에 재생성 | `E2StringManagerWindow.cs` |
| 3 | 빈 `Runtime/`(asmdef) 삭제, Editor asmdef의 참조 제거 | `Runtime/` 삭제, `HB.E2String.Editor.asmdef` |
| 4 | 고칠 것 없음 (실제 확인으로 문제 아님) | — |
| 5 | 경고 `E2S005` 추가 (같은 namespace에서 `To{Enum}Fast` 이름이 겹치는 enum마다, 정적 호출 안내). API 이름 그대로 | 생성기 |
| 6 | 대소문자 없이 겹치는 hintName 그룹 전원에 ClassKey의 FNV-1a 해시를 붙여 모두 생성 | 생성기 |
| 마이너: 창 Flags 인식 | `[Flags]`가 다른 대괄호여도 인식 (`HasFlagsAttribute`) | 창 |
| 마이너: 네임스페이스 키워드 | 예약 키워드(+`__arglist` 등) 거절 | 창 |
| 마이너: 생성 코드 `global::` | `using` 제거, `MethodImpl`·`StringComparison`·`Dictionary`·`StringBuilder`를 `global::`로 | 생성기 |
| 마이너: 창 목록 범위 | **코드 유지**, README에 "Assets만 스캔, 거절된 enum도 보일 수 있음" 명시. 이유: 텍스트 스캔 구조를 TypeCache 기반으로 바꾸는 건 이 점검 범위를 넘음 | README |
| 마이너: 첫 스캔 동기 I/O | **유지.** 창을 처음 열 때·Refresh 때만 돌고, `E2String` 문자열 사전 필터가 있음 | — |
| 마이너: 캐시 키의 Location | **유지.** 진단 위치에 필요하고, 비용은 해당 파일 enum 재생성뿐 | — |
| 마이너: CHANGELOG·LICENSE | **넣지 않음** (사용자 결정 4) | — |

- README: 시작하기(asmdef 자동 생성·서버 공유 영향 없음), 다시 생성 안내, `E2S003`·`E2S005` 표, 다른 namespace 동명 enum 주의, 매니저 목록 범위.
- 생성기: Release 빌드(경고 0) → `Plugins/EString.dll` 복사. SHA-256 `cf2220a5…f933` (원본 bin과 일치). RS1032(진단 메시지 마침표) 경고 1건은 빌드 중 고침.
- 버전: 1.0.0 유지 (배포 이력 없음).

## 3. 테스트 (ToolTest6.3, 로컬 `file:` 패키지로 교체해 재확인)
- run2b/run3 (`-executeMethod E2CheckRunner.Run`, 창의 private 정적 메서드를 리플렉션 호출) — exit 0
  - `FindOwningAssemblyDefinition`: Game 폴더 → `E2Check.Game.asmdef`, CSharp 폴더 → null
  - `CountScriptsMovedByNewAsmdef`: asmdef 밖 스크립트만 셈 (3), 새 폴더 0
  - `AssemblyDefinitionExists`: 파일 `Odd.asmdef`의 `"name": "E2Check.OddName"` → `E2Check.OddName` True, `Odd` False (리뷰 반영 확인)
  - `GetAssemblyNameFromScriptPath` 실제 반환: `Assembly-CSharp.dll`, `E2Check.Attr.dll` → `.dll` 떼는 처리가 필요했음 (확정)
- run2c/run4 (일반 컴파일) — exit 0, 에러 0, CS8785 없음, 빈 asmdef 경고 없음
  - 창이 만드는 파일 그대로(`BuildAttributeSource(false, "E2Check.Gen")` + `BuildAttributeAsmdef()`) → asmdef `E2Check.GenUse`가 references로 참조 / Assembly-CSharp `CsGenState`가 자동 참조, 둘 다 통과
  - 6번: `E2Check.Case.UI.KindExtensions`·`E2Check.Case.ui.KindExtensions` 둘 다 생성 (정적 호출 컴파일 통과)
  - 5번: `warning E2S005` 두 enum 각각 출력, 안내대로 정적 호출하면 통과
  - `global::`: 사용자 namespace에 `StringComparison`·`MethodImplAttribute`·`MethodImplOptions`·`Dictionary<,>`가 있는 `[Flags]` enum 생성 코드 통과 (수정 전 DLL로는 돌려보지 않음 — 수정 전 실패는 추정)

## 4. 외부 리뷰 (review, runId 36474e5456, gpt_main_sol)
- [major] asmdef 존재 검사가 파일명 기준 → **채택.** 모든 AssemblyDefinitionAsset의 `"name"`을 `JsonUtility`로 읽어 비교. 재확인 완료.
- [major] `noEngineReferences: true`라 함께 옮겨진 UnityEngine 사용 스크립트가 깨짐 → **채택.** 자체 리뷰에서 놓침. `false`로.
- [minor] `__arglist` 등 키워드 누락 → **채택.**
- [minor] 두 파일 중 하나만 써지는 부분 실패 → **부분 채택.** asmdef를 먼저 쓰고, 어트리뷰트 쓰기가 실패하면 이번에 만든 asmdef를 지운다. 덮어쓰기 백업·복구는 하지 않음 (쓰기 도중 실패는 드물고, 덮어쓰기는 사용자가 확인한 동작).
- 자체 리뷰 관찰(고아 asmdef, 다른 어셈블리의 동명 enum 미감지, 이동 스크립트 셈 비용)은 수용, 변경 없음.

## 5. 남은 것
- 창 UI 흐름(생성/다시 생성 대화상자, 경고 HelpBox, Foldout)은 배치로 확인하지 못함 → 에디터에서 사용자 확인 필요.
- ~~ToolTest6.3 정리~~ → 6절에서 완료.
- 커밋 안 함: ToolCreate(창·asmdef·README·DLL·Runtime 삭제·docs), ToolWithCS(생성기).

## 6. 마지막 점검 (결정 4)
- 사용자 에디터 확인: UI 정상, 다시 생성 후 컴파일되면 Assembly-CSharp 경고가 사라짐 (점검 우려 3 해소, 사용자 확인).
- 확인 중 ToolCreate 쪽 `.meta` 변경 없음 (`file:` 로컬 패키지 우려 해소, `git status` 확인).
- 수정 (`E2StringManagerWindow.cs`):
  - 다시 생성은 첫 줄이 `// <auto-generated by HB E2String Manager />`인 파일만 덮어씀. 아니면 Foldout에 안내만, `CreateAttributeScript`에서도 한 번 더 막음. 이유: 파일명이 달라도 `class E2StringAttribute`가 있으면 설치로 인식하므로, 다른 코드가 섞인 파일이 통째로 덮어써질 수 있었음. 헤더는 git 이력의 모든 버전에 있었음 (확인).
  - 폴더 아래 다른 스크립트가 있으면 asmdef를 만들지 않음 (확인 대화상자 제거). 이유: asmdef 아래에서는 Editor 폴더 규칙이 사라져 에디터 스크립트가 런타임 어셈블리로 들어가고 플레이어 빌드가 실패함. 외부 리뷰 [major]의 제안과 같은 방향.
  - 다시 생성의 네임스페이스 옵션을 기존 생성 파일에서 읽어 채움 (생성 직후·도메인 리로드 뒤 모두).
- README: asmdef 미생성 조건, 다시 생성 대상·옵션 채움 안내.
- 테스트 (ToolTest6.3 run5, exit 0): `IsGeneratedAttributeFile` 생성 파일 True / 다른 코드 섞인 `SharedTypes.cs` False, `LoadNamespaceOptionsFrom` 글로벌+호환 → `global=True`, 커스텀 → `global=False custom='E2Check.Gen'`.
- 작성자 표시 "HeartBeat": ToolTest6.3 lock이 InGameConsole(`bfd6415a…`)·ScreenRecorder를 작성자 변경(`1b1e2ee`) 이전 커밋에 고정해서 생긴 것. 저장소 package.json 6개의 `author`는 모두 `HoBeenH / whdmstlsk@gmail.com`. 남아 있던 `HB_TestFrameworkHelper/package.json` description의 "HeartBeat " 제거 (JSON 검사 통과).
- ToolTest6.3 정리: `Assets/E2StringCheck/` 삭제, manifest·lock 백업으로 복원 (SHA-256 `48b5d225…b613` / `4931fbfa…8a04` 일치), 다시 열어 컴파일 exit 0, ScriptAssemblies에 E2String·E2Check 없음, PackageCache com.hb는 ingameconsole·screenrecorder만.
- 남은 것: 커밋 (ToolCreate, ToolWithCS 두 저장소).
