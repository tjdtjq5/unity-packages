# Changelog

## [0.1.0] — 2026-05-05

Phase 1 MVP 첫 배포. `com.tjdtjq5.cicd`(GameCI/GitHub Actions) 대체 — Codemagic 전용.

### 핵심 가치

- Unity Editor 안에서 Codemagic 운영 완결 (yaml 작성 X)
- 첫 빌드까지 셋업 ~5분
- Linux X2 + GameCI Docker 흐름 (Personal license 호환)

### 포함

**Setup Wizard (7단계)**
- Welcome splash → Preflight → Token → App match → License → Keystore → Complete
- `.ulf` 자동 탐색 + 만료일 D-N 표시 + serial 마스킹
- Codemagic 환경 변수 등록은 **GUI walk-through** (REST API 미공개라 클립보드 복사 + 셀프 체크)
- 등록 변수 7개: UNITY_LICENSE / UNITY_EMAIL / UNITY_PASSWORD / KEYSTORE_BASE64 / KEYSTORE_PASSWORD / KEY_ALIAS / KEY_PASSWORD (UNITY_SERIAL은 yaml inline 추출)

**BuildDialog**
- 인스턴스 / max_build_duration / 캐시 토글 / 빌드 옵션 한 화면
- `manifest.json` file: ↔ git URL 자동 swap (try/finally 보장)
- yaml 자동 생성 + commit + push + 빌드 트리거
- 태그 기반 트리거 또는 Codemagic API 직접 트리거 선택

**3-레이어 영속**
- `SecretStore` (EditorPrefs, per-user) — 토큰 / 비밀번호 / .ulf 내용
- `LocalUserState` (Library JSON, gitignored) — 만료일 / serial mask / 셀프 체크 상태
- `CodemagicProjectSettings` (ScriptableObject, git 추적) — 앱 ID / 알림 수신자

**서비스**
- `CodemagicApiClient` — REST(`/apps`, `/builds`, `/builds/{id}`). UniTask 기반
- `CodemagicYamlGenerator` — Linux X2 + GameCI Docker yaml + 캐시 step + 진단 grep + publishing.email
- `CodemagicBuildScript` — Unity `-batchmode -executeMethod` 진입점 (패키지 asmdef로 cp 단계 회피)
- **토스트 알림 시스템** — `EditorUI.DrawNotificationBar`로 [Copy] / [X] 버튼 제공 (suparun/cicd 일관)

**Util 포팅 (cicd → codemagic)**
- `ManifestModeSwapper` (file:↔git URL swap), `GitHelpers` (RunGit/GetDirtyFiles), `KeystoreHelper` (base64), `KeystoreCreator` (keytool 호출), `UlfReader`, `UlfSerialExtractor`, `PlatformPaths`

### 알려진 제약

- **Personal license + Codemagic 무료 macOS M2 (500분/월) 조합 사용 불가** — Unity 공식 정책. macOS native에서 Personal command line 활성화 차단.
- Linux X2는 분당 $0.045 (무료 분 없음). 비용 관리는 빌드 시간 최적화로.
- Codemagic 환경 변수 그룹 관리 REST API 미공개 → GUI walk-through 사용 (`UpsertEnvVarAsync` `[Obsolete]` 표시).
- 첫 통합 빌드 검증은 SurvivorsDuo 환경에서 후속 작업으로 진행.

### 제외 (Phase 2+ 로드맵)

- `Diagnostics/*` — Phase 3 (자동 로그 다운로드 + 카테고리화 + 추천 액션)
- `License/UlfRenewer` — Phase 4 (Hub 연동 자동 갱신)
- `Cache/CacheDecider`, `ProjectStateCollector`, `SnapshotStore` — Phase 2 (자동 캐시 결정 룰)
- macOS 인스턴스 yaml — Unity Plus/Pro 라이선스 사용자만 가능, 별도 phase
