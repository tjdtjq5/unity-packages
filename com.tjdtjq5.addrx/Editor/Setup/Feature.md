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
| Windows/AddrXTabBase | `../Windows/AddrXTabBase.cs` | AddrXTabBase 상속, AddrXGui 유틸 |

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
| `GetAddress()` | `string GetAddress(string assetPath)` | 에셋 경로 → 주소 (`1뎁스폴더/파일명`, 확장자 제외). **깊이와 무관** |
| `GetRootFolder()` | `string GetRootFolder(string assetPath)` | 에셋 경로 → 1뎁스 폴더명 (콘텐츠 영역 식별자) |
| `GetGroupName()` | `string GetGroupName(string assetPath)` | 에셋 경로 → 그룹명 (`GroupDepth` 만큼 `-`로 연결) |
| `GroupDepth` | `int GroupDepth` | 그룹 입도 (기본 1, 최소 1) |
| `SetGroupDepth()` | `void SetGroupDepth(int depth)` | 그룹 입도 설정 (저장만, 반영은 전체 동기화) |
| `IsRemoteFolder()` | `bool IsRemoteFolder(string folderName)` | 원격 여부. **인자는 1뎁스 폴더명** |
| `SetRemoteFolder()` | `void SetRemoteFolder(string folderName, bool isRemote)` | 로컬/원격 전환 |
| `GetGroupFolders()` | `string[] GetGroupFolders()` | 루트 하위 1뎁스 폴더 목록 |
| `GetLabelsForAsset()` | `List<string> GetLabelsForAsset(string assetGuid)` | 에셋의 전체 라벨 목록 (디폴트 + 오버라이드) |
| `GetLabelForCategory()` | `string GetLabelForCategory(string guid, string cat)` | 특정 카테고리 라벨 |
| `SetLabelOverride()` | `void SetLabelOverride(string guid, string cat, string val)` | 라벨 오버라이드 설정 |
| `LabelCategories` | `List<LabelCategory>` | 라벨 카테고리 목록 |
| `RemoteFolders` | `List<RemoteFolderEntry>` | 원격 폴더 목록 |

#### 주소와 그룹은 별개다

`GetAddress()`(공개 조회 키)는 **항상 1뎁스 기준**이고, `GetGroupName()`(번들 경계)만
`GroupDepth`의 영향을 받는다. 그래서 그룹을 잘게 나눠도 **주소는 바뀌지 않는다.**

```
GroupDepth = 1 → Common/Prefabs/UI/Foo.prefab → 그룹 "Common"           주소 "Common/Foo"
GroupDepth = 2 →                              → 그룹 "Common-Prefabs"   주소 "Common/Foo"
GroupDepth = 3 →                              → 그룹 "Common-Prefabs-UI" 주소 "Common/Foo"
```

폴더 깊이가 설정값보다 얕으면 있는 만큼만 쓴다.

⚠구분자가 `-`인 것은 제약이다. 그룹명에 `/`가 들어가면 `FindUniqueGroupName`이 `-`로 치환해
그룹을 만드는데 조회는 치환 전 이름으로 하므로, `FindGroup`이 매번 실패해 그룹이 무한 증식한다.

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

### SetupTab (AddrXTabBase)

스텝 위자드(초기 설정 4단계) + 대시보드(그룹 관리, 라벨 관리, 에셋 상태, 충돌 감지). `AddrXManagerWindow`에서 탭으로 사용.

### SettingsPanel

톱니바퀴 버튼 클릭 시 표시되는 설정 패널. AddrX 설정(LogLevel, Tracking, LeakDetection, AutoInit) + Addressables 설정(Profile, Build/Load Path) 표시.

## 주의사항

- `AddrXSetupRules`는 `Resources` 폴더에 위치해야 한다 (`Assets/AddrX/Resources/AddrXSetupRules.asset`).
- `AddrXAutoRegister`는 `AssetPostprocessor`이므로 에셋 Import 시 자동 실행된다. 대량 에셋 이동 시 퍼포먼스에 주의.
- 주소 규칙은 **파일명 기반** (`1뎁스폴더/파일명`)이므로 같은 1뎁스 폴더 안의 파일명 중복은 차단된다. `GroupDepth`를 올려도 주소 규칙은 그대로이므로 이 제약은 변하지 않는다.
- `AddrXFolderColorizer`와 `AddrXLabelDrawer`는 `[InitializeOnLoad]`로 항상 활성화된다. 비활성화하려면 스크립트 자체를 제거해야 한다.
- `SetRemoteFolder()` 호출 시 `AddrXSetupRules`만 변경되고, 실제 Addressables 그룹 스키마는 별도로 `ApplyGroupSchema()`를 호출해야 반영된다. (SetupTab 대시보드에서는 자동 처리)
- Label Category의 옵션 변경/삭제 시 기존 Addressables 라벨과의 동기화는 수동으로 `전체 동기화` 버튼을 실행해야 한다.
- `GroupDepth` 변경도 같은 규약이다 — 저장만 되고 기존 엔트리는 옛 그룹에 남는다. Setup 탭의 `Mismatched` 카운트가 재배치 대상 수를 알려주며, `전체 동기화`가 이동 + 빈 그룹 정리를 함께 수행한다.
- `전체 동기화`는 **현재 규칙으로 만들어질 수 없는 빈 그룹**을 제거한다. 조건은 ①엔트리 0개 ②규칙상 생성 불가한 이름 ③`DefaultGroup`이 아님 — 셋 모두 만족할 때만이며 제거 시 로그를 남긴다. 에셋을 넣기 전 미리 만들어 둔 빈 그룹은 대상이 될 수 있다.
- ⚠원격 판정(`IsRemoteFolder`)의 인자는 **1뎁스 폴더명**이다. `GetGroupName()` 결과를 넘기면 `GroupDepth > 1`일 때 매칭이 조용히 실패해, 원격이어야 할 콘텐츠가 로컬 스키마로 생성된다.
