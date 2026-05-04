# Codemagic — Unity CI/CD 자동화

> Unity 프로젝트를 Codemagic에서 "그냥 돌아가게" 만든다.

Codemagic 자체는 강력하지만 Unity 특수성(라이선스 만료, 시리얼 추출, IL2CPP 메모리, yaml 노가다)을 사용자가 매번 수동으로 처리해야 한다. 이 패키지는 그 사이의 보일러플레이트를 흡수해서 Codemagic 위에 Unity 친화 레이어를 얹는다.

## 핵심 가치

- **Editor 안에서 Codemagic 운영 완결** — yaml/Codemagic UI/Unity Hub 사이를 오가지 않음
- **첫 빌드까지 5분** — 패키지 설치 → API 토큰 + 라이선스 등록 → 빌드 옵션 선택 → 트리거. yaml 작성 X
- **라이선스 30일 갱신 자동화** (Phase 2+)
- **빌드 실패 진단 보조** (Phase 3+)

## 설치

`Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.tjdtjq5.codemagic": "https://github.com/tjdtjq5/unity-packages.git?path=com.tjdtjq5.codemagic#codemagic/v0.1.0"
  }
}
```

## 시작하기

1. **Build → Codemagic → Setup Wizard** 메뉴 실행
2. 6단계 위저드 따라 진행 (Welcome / Preflight / Token / App match / License / Keystore / Complete)
3. **Build → Codemagic → Dashboard** → [Build] 버튼 → 옵션 선택 → 빌드 시작

## 폴더 구조 (Editor 전용)

```
Editor/
├── Setup/        ← 6단계 SetupWizard + ISetupStep + Steps/
├── Dashboard/    ← CodemagicWindow + BuildDialog
├── Codemagic/    ← REST API + yaml generator
├── License/      ← .ulf reader + serial extractor
├── Build/        ← executeMethod 진입점
├── Manifest/     ← file: ↔ git URL swapper
├── Git/          ← git wrapper
├── Settings/     ← 3-레이어 영속 (EditorPrefs / Library JSON / ScriptableObject)
└── Util/         ← keystore helpers, platform paths
```

## 영속 레이어

| 레이어 | 위치 | 데이터 | git 추적 |
|-------|------|------|---------|
| Layer 1 | EditorPrefs | 시크릿 (token, password, .ulf) | X |
| Layer 2 | `Library/codemagic-setup.json` | per-user 메타 (만료일, 시리얼 mask, keystore 경로) | X |
| Layer 3 | `Assets/Editor/CodemagicProjectSettings.asset` | 팀 공유 (앱 ID, 빌드 옵션 default, 알림 수신자) | O |

## 의존성

- `com.tjdtjq5.editor-toolkit` 1.3.3 (IMGUI 헬퍼 `EditorUI`)
- Codemagic 계정 + Personal Access Token

## 라이선스

Internal — 모노레포 (tjdtjq5/unity-packages).

## 관련 문서

- 설계: `<프로젝트>/docs/CODEMAGIC_PACKAGE.md` (개발 중인 프로젝트의 docs/)
- 폐기 대상: `com.tjdtjq5.cicd` — GameCI/GitHub Actions 흐름이라 Unity 6+ OOM 한계
