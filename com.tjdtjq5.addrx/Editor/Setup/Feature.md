# Setup Feature

- **상태**: stable
- **용도**: Addressables 폴더 규칙 기반 자동 등록/해제 + 그룹/라벨 관리 + 초기 설정 위자드

## 의존성

| 대상 | 경로 | 용도 |
|------|------|------|
| AddrXLog | `../../Runtime/` | 로그 출력 |
| AddrXSettings | `../../Runtime/Settings/AddrXSettings.cs` | 위자드 Step 4에서 설정 편집 |
| Windows/AddrXManagerWindow | `../Windows/AddrXManagerWindow.cs` | SetupTab을 탭으로 호스팅 |
| Settings/AddrXSettingsProvider | `../Settings/AddrXSettingsProvider.cs` | Project Settings에 AddrX 탭 등록 (AddrXSettings 편집) |
| EditorToolkit | 외부 패키지 `Tjdtjq5.EditorToolkit.Editor` | EditorTabBase 상속, EditorUI 유틸 |

## 구조

```
Editor/Setup/
  AddrXAutoRegister.cs       -- AssetPostprocessor: 에셋 Import/Move/Delete 시 자동 등록/해제
  AddrXFolderColorizer.cs    -- Project 창 그룹 폴더에 로컬(파랑)/원격(주황) 색상 뱃지 표시
  AddrXLabelDrawer.cs        -- 모든 에셋 Inspector에 Label Category 드롭다운 표시
  AddrXSetupRules.cs         -- 폴더 규칙 매핑 데이터 SO (그룹, 라벨, 주소 규칙)
  FolderTemplateGenerator.cs -- 기본 폴더 + Addressables 그룹 일괄 생성
  SettingsPanel.cs           -- 톱니바퀴(⚙) Settings 패널 UI (AddrX + Addressables 설정)
  SetupTab.cs                -- Setup 탭 UI: 스텝 위자드(초기 설정) + 대시보드(일상 관리)
```

## API

### AddrXSetupRules (ScriptableObject)

핵심 데이터 클래스. `Assets/AddrX/Resources/AddrXSetupRules.asset`에 저장.

| 멤버 | 시그니처 | 설명 |
|------|----------|------|
| `Instance` | `static AddrXSetupRules Instance` | 싱글톤 (Resources.Load) |
| `GetOrCreate()` | `static AddrXSetupRules GetOrCreate()` | 없으면 기본값으로 생성 |
| `RootPath` | `string RootPath` | 루트 경로 (기본 `Assets/Addressables`) |
| `GetAddress()` | `string GetAddress(string assetPath)` | 에셋 경로 → 주소 (`그룹/파일명`, 확장자 제외) |
| `GetGroupName()` | `string GetGroupName(string assetPath)` | 에셋 경로 → 그룹명 (1뎁스 폴더명) |
| `IsGroupRemote()` | `bool IsGroupRemote(string groupName)` | 원격 그룹 여부 |
| `SetGroupRemote()` | `void SetGroupRemote(string groupName, bool isRemote)` | 로컬/원격 전환 |
| `GetGroupFolders()` | `string[] GetGroupFolders()` | 루트 하위 1뎁스 폴더 목록 |
| `GetLabelsForAsset()` | `List<string> GetLabelsForAsset(string assetGuid)` | 에셋의 전체 라벨 목록 (디폴트 + 오버라이드) |
| `GetLabelForCategory()` | `string GetLabelForCategory(string guid, string cat)` | 특정 카테고리 라벨 |
| `SetLabelOverride()` | `void SetLabelOverride(string guid, string cat, string val)` | 라벨 오버라이드 설정 |
| `LabelCategories` | `List<LabelCategory>` | 라벨 카테고리 목록 |
| `RemoteGroups` | `List<RemoteGroupEntry>` | 원격 그룹 목록 |

### LabelCategory (Serializable class)

| 필드 | 타입 | 설명 |
|------|------|------|
| `categoryName` | `string` | 카테고리 이름 (예: Priority, Quality, Region, Platform) |
| `defaultValue` | `string` | 기본 라벨값 |
| `options` | `List<string>` | 선택 가능한 옵션 목록 |

### AddrXAutoRegister (AssetPostprocessor)

| 멤버 | 시그니처 | 설명 |
|------|----------|------|
| `ApplyGroupSchema()` | `internal static void ApplyGroupSchema(group, isRemote)` | 그룹에 로컬/원격 Build/Load Path 적용 |
| `DetectDuplicates()` | `internal static HashSet<string> DetectDuplicates(paths, rules, settings)` | 주소 중복 감지 |
| `RegisterAsset()` | `internal static bool RegisterAsset(settings, rules, path, duplicates)` | 단일 에셋 등록 (Label Category 라벨 자동 부여) |

자동 동작: `Assets/Addressables/` 하위 에셋 Import/Move/Delete 시 Addressables 엔트리 자동 동기화. 1뎁스 폴더 생성/삭제 시 Addressables 그룹 자동 생성/제거.

### FolderTemplateGenerator

| 멤버 | 시그니처 | 설명 |
|------|----------|------|
| `Generate()` | `static bool Generate()` | 기본 폴더 + Addressables 그룹 일괄 생성 |
| `EnsureGroup()` | `static void EnsureGroup(rules, groupName, isRemote)` | 개별 그룹 폴더 + Addressables 그룹 보장 |

기본 폴더: Common, Title, Lobby, Chapter1~3, Audio_BGM, Audio_SFX, Font

### AddrXFolderColorizer

`[InitializeOnLoad]` 자동 활성화. Project 창에서 루트 하위 1뎁스 그룹 폴더에 로컬(파랑 `#66B3F2`) / 원격(주황 `#F2993D`) 색상 뱃지를 표시.

### AddrXLabelDrawer

`[InitializeOnLoad]` 자동 활성화. 루트 하위 에셋의 Inspector 헤더에 Label Category별 드롭다운을 표시. 디폴트와 다른 오버라이드는 Bold 표시. 변경 시 Addressables 라벨도 자동 동기화.

### SetupTab (EditorTabBase)

스텝 위자드(초기 설정 4단계) + 대시보드(그룹 관리, 라벨 관리, 에셋 상태, 충돌 감지). `AddrXManagerWindow`에서 탭으로 사용.

### SettingsPanel

톱니바퀴 버튼 클릭 시 표시되는 설정 패널. AddrX 설정(LogLevel, Tracking, LeakDetection, AutoInit) + Addressables 설정(Profile, Build/Load Path) 표시.

## 주의사항

- `AddrXSetupRules`는 `Resources` 폴더에 위치해야 한다 (`Assets/AddrX/Resources/AddrXSetupRules.asset`).
- `AddrXAutoRegister`는 `AssetPostprocessor`이므로 에셋 Import 시 자동 실행된다. 대량 에셋 이동 시 퍼포먼스에 주의.
- 주소 규칙은 **파일명 기반** (`그룹/파일명`)이므로 같은 그룹 내 파일명 중복은 차단된다.
- `AddrXFolderColorizer`와 `AddrXLabelDrawer`는 `[InitializeOnLoad]`로 항상 활성화된다. 비활성화하려면 스크립트 자체를 제거해야 한다.
- `SetGroupRemote()` 호출 시 `AddrXSetupRules`만 변경되고, 실제 Addressables 그룹 스키마는 별도로 `ApplyGroupSchema()`를 호출해야 반영된다. (SetupTab 대시보드에서는 자동 처리)
- Label Category의 옵션 변경/삭제 시 기존 Addressables 라벨과의 동기화는 수동으로 `전체 동기화` 버튼을 실행해야 한다.
