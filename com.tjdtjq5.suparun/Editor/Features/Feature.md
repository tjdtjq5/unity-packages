# Features

- **상태**: stable
- **용도**: 게임 기능 템플릿([Table]/[Service] 포함)을 설치/제거/관리하는 Feature 시스템 + EditorWindow

## 의존성

| 대상 | 경로 | 용도 |
|------|------|------|
| EditorToolkit | 패키지 `com.tjdtjq5.editor-toolkit` | EditorUI 드로잉 유틸 |
| Newtonsoft.Json | Unity 내장 | feature.json 직렬화/역직렬화 |
| 패키지 Templates | `../../Features~/` | Feature 템플릿 원본 (패키지 내 숨김 폴더) |

## 구조

| 파일 | 타입 | 설명 |
|------|------|------|
| `FeatureInfo.cs` | `class` (Serializable) | Feature 메타데이터 — name, id, description, tier, dependencies + 런타임 상태 (sourcePath, installPath, isInstalled, isCustom) |
| `FeatureRegistry.cs` | `static class` | Feature 스캔 — 패키지 템플릿(`Features~/`) + 설치된 폴더(`Assets/SupaRun/Features/`) + 커스텀 감지 |
| `FeatureInstaller.cs` | `static class` | Feature 설치(의존성 재귀 설치)/제거/커스텀 생성 |
| `FeaturesWindow.cs` | `EditorWindow` | Feature 관리 UI — 설치된 목록, 추가 팝업, 커스텀 생성 |

## API

### FeatureInfo

| 필드 | 타입 | 설명 |
|------|------|------|
| `name` | `string` | 표시 이름 |
| `id` | `string` | 고유 ID (폴더명 기반, 예: `currency`, `daily-mission`) |
| `description` | `string` | 설명 |
| `tier` | `int` | 정렬 우선순위 (낮을수록 먼저) |
| `dependencies` | `string[]` | 의존하는 다른 Feature의 id 목록 |
| `sourcePath` | `string` | 패키지 내 템플릿 원본 경로 (런타임) |
| `installPath` | `string` | 설치된 프로젝트 경로 (런타임) |
| `isInstalled` | `bool` | 설치 여부 (런타임) |
| `isCustom` | `bool` | 패키지 템플릿에 없는 유저 커스텀 여부 (런타임) |

### FeatureRegistry

| 메서드 | 설명 |
|--------|------|
| `GetAll()` | 모든 Feature 목록 (템플릿 + 설치됨 + 커스텀). tier → name 순 정렬 |
| `GetInstalled()` | 설치된 Feature만 반환 |
| `GetAvailable()` | 설치 가능한 템플릿만 반환 (미설치 + 비커스텀) |
| `CheckDependencies(feature)` | 의존성이 모두 설치되어 있는지 확인. `(ok, missing[])` 반환 |
| `InstallRoot` | 설치 루트 경로 (`Assets/SupaRun/Features`) |

### FeatureInstaller

| 메서드 | 설명 |
|--------|------|
| `Install(feature)` | Feature 설치 — 의존성 재귀 설치 + 템플릿 복사 + AssetDatabase.Refresh. 설치된 id 목록 반환 |
| `Uninstall(feature)` | Feature 제거 — 다른 Feature가 의존하면 거부. 폴더 삭제 + Refresh |
| `CreateCustom(id, displayName)` | 빈 폴더 + feature.json 생성. 설치 경로 반환 |

### FeaturesWindow (EditorWindow)

| 메서드 | 설명 |
|--------|------|
| `Open()` | Features 윈도우 열기 (메뉴: `Tools/SupaRun/Features`, 단축키 `Ctrl+Shift+F`) |

## 주의사항

- Feature 템플릿은 패키지의 `Features~/` 폴더(Unity에서 숨김)에 위치. 각 하위 폴더에 `feature.json` 필요
- 설치 시 `*Service.cs` 파일은 자동으로 `#if UNITY_EDITOR` / `#endif` 래핑 — 빌드에서 서버 전용 코드 제외
- `Uninstall`은 다른 Feature가 해당 Feature에 의존하면 거부하고 경고 로그 출력
- 커스텀 Feature는 `tier: 99`로 생성되어 목록 하단에 배치
- `FeatureRegistry.GetAll()`은 매 호출마다 디스크 스캔 수행 — 캐싱 없음. FeaturesWindow에서는 `Refresh()` 호출 시점에만 갱신
- `feature.json`을 `Newtonsoft.Json`으로 파싱하므로 패키지에 Newtonsoft 의존성 필요
