# Menu

- **상태**: stable
- **용도**: Unity 안에 남은 SupaRun 진입점 — 메인 툴바 드롭다운과 요금 페이지 링크.

> **이 폴더는 예전에 `Dashboard/` 였다.** 그 안의 EditorWindow(대시보드)는 삭제됐다.
> 화면은 어드민 웹 하나뿐이고, Unity 는 로컬에서만 할 수 있는 일을 브리지로 대신한다.
> 근거와 옮겨 간 자리는 `../Bridge/Feature.md` 참조.

## 의존성

| 대상 | 경로 | 용도 |
|------|------|------|
| SupaRunAdmin | `../Bridge/SupaRunAdmin.cs` | 배포 화면 열기 (해시로 특정 화면 지정) |
| SupaRunSettings | `../Settings/SupaRunSettings.cs` | 편집 환경 읽기/쓰기, 환경 목록 |
| PrerequisiteChecker | `../Scaffold/PrerequisiteChecker.cs` | gh 계정 (요금 링크용) |

## 구조

```
Menu/
├── SupaRunToolbar.cs   # 메인 툴바 드롭다운 (환경 표시·전환 + 배포 진입)
└── CostMenu.cs         # 메뉴: Tjdtjq/SupaRun/Cost/{Supabase,Google Cloud,GitHub Actions}
```

## 메인 툴바 드롭다운 (`SupaRunToolbar.cs`)

Unity 6.3 `MainToolbarElement` 로 툴바에 `SupaRun: <환경> ▾` 을 얹는다.
같은 프로젝트의 Photon Quantum 이 씬 선택 드롭다운에 같은 API 를 쓰고 있어 검증된 경로다.

목적이 둘이고, **첫 번째가 더 중요하다**:

1. **현재 편집 환경을 항상 보이게 한다.** 라벨이 곧 표시다 — "dev 인 줄 알고 prod 를 건드리는"
   사고는 지금 어디인지 모르는 상태에서 나온다. 어드민을 열어야만 알 수 있으면 늦다
2. 환경 전환과 배포 진입. **그 밖의 항목은 없다** — 어드민은 Ctrl+Shift+D, 스키마 반영은
   자동(컴파일)이거나 배포에 포함, Id 상수는 어드민이 행 편집 때 자동 트리거(2026-08-01 간소화)

| 메뉴 | 동작 |
|---|---|
| 환경/`<이름>` | 편집 환경 전환. **확인창 없음** — 고르는 행위가 곧 의도. prod 위험은 환경별 자동 반영 OFF 가 구조로 막는다 |
| 서버 배포… | 어드민 `#ops` 로. 설정이 덜 됐으면 `#settings` 로 |

- 배포를 툴바에서 실행하지 않는 이유: 몇 분이 걸리고 진행 로그를 봐야 하는데 툴바에는 그 자리가 없다.
  여기서 할 일은 **버튼까지 데려다주는 것**이다
- 배포 확인 모달도 **여기서 띄우지 않는다.** 어드민이 대상과 진행을 보여주는 자리라,
  같은 것을 두 번 확인시키면 두 번째가 형식이 된다
- 아이콘은 `EditorGUIUtility.FindTexture` 로 찾는다. `IconContent` 는 없는 이름에 경고를 뿌리고,
  내장 아이콘 이름은 Unity 버전마다 사라지기도 한다

> ⚠ **툴바 요소는 등록만으로는 보이지 않는다.** 표시 여부가 사용자 설정에 저장되고
> `MainToolbarElementAttribute` 에는 그것을 제어하는 항목이 없다(`path`/`defaultDockPosition`/
> `menuPriority`/`displayName`/`ussName` 뿐). 같은 프로젝트의 Quantum 도 기본 숨김이다.
> 사용자는 **툴바 우클릭 → Tools > SupaRun > Environment Bar** 로 켠다.
>
> 그래서 `ShowOnceOnFirstLoad()` 가 프로젝트마다 **한 번만** 자동으로 켠다.
> 한 번뿐인 이유는 나중에 사용자가 끄면 그 선택을 존중해야 하기 때문이다 — 매번 켜면 훼방이 된다.
> 쓰는 `MainToolbar.ShowAll` 은 **Unity 내부 API(non-public)** 라 리플렉션으로 부르고 실패는 삼킨다.
> 툴바가 안 보이는 것은 불편이지 고장이 아니고, 우클릭 메뉴라는 길이 남아 있다.

## CostMenu

| 메뉴 항목 | 설명 |
|-----------|------|
| `Tjdtjq/SupaRun/Cost/Supabase` | Supabase 요금 페이지 |
| `Tjdtjq/SupaRun/Cost/Google Cloud` | GCP Billing 페이지 |
| `Tjdtjq/SupaRun/Cost/GitHub Actions` | GitHub Actions Billing 페이지 |

설정이 비어 있으면 링크 대신 안내 모달을 띄운다 — 빈 URL 로 브라우저를 여는 것보다 낫다.
