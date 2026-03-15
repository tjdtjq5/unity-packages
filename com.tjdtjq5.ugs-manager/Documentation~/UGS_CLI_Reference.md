# UGS CLI 레퍼런스

> `ugs --version`으로 설치 확인. `npm install -g ugs`로 설치.

## 글로벌 옵션

| 옵션 | 설명 |
|------|------|
| `-j, --json` | JSON 출력 |
| `-q, --quiet` | 최소 로그 |
| `-e, --environment-name` | 환경 지정 |
| `-p, --project-id` | 프로젝트 지정 |

## 인증

```bash
ugs login --service-key-id <KEY_ID> --secret-key-stdin  # 로그인
ugs logout                                               # 로그아웃
ugs status                                               # 상태 확인
```

자격증명 저장 위치: `{LOCALAPPDATA}/UnityServices/credentials` (Base64 `keyId:secretKey`)
환경변수: `UGS_CLI_SERVICE_KEY_ID`, `UGS_CLI_SERVICE_SECRET_KEY`

## 설정

```bash
ugs config get project-id
ugs config get environment-name
ugs config set environment-name dev
```

저장 위치: `{LOCALAPPDATA}/UnityServices/Config.json`

---

## 서비스별 명령

### Environment (`env`)

```bash
ugs env list -j -q              # 환경 목록 (JSON: [{id, name, isDefault}])
ugs env add <name>              # 환경 생성
# 삭제: CLI 미지원 (Dashboard에서만 가능)
```

### Remote Config (`rc`)

```bash
ugs rc new-file <name>          # .rc 파일 생성
ugs rc export <dir> <file>      # 서버 → 파일 내보내기
ugs rc import <dir> <file>      # 파일 → 서버 가져오기
ugs deploy <dir> -s remote-config  # .rc 파일 배포
```

설정 파일: `.rc`
```json
{
  "$schema": "https://ugs-config-schemas.unity3d.com/v1/remote-config.schema.json",
  "entries": { "key": "value" },
  "types": { "key": "FLOAT" }
}
```

### Cloud Code (`cc`)

```bash
ugs cc scripts list -j -q      # 스크립트 목록 (JSON: [{Name, DatePublished}])
ugs cc scripts get <name>      # 스크립트 상세
ugs cc scripts create <name> <file>  # 생성
ugs cc scripts update <name> <file>  # 업데이트
ugs cc scripts delete <name>        # 삭제
ugs cc scripts publish <name>       # 게시
ugs cc scripts export <dir> <file>  # 내보내기
ugs cc scripts import <dir> <file>  # 가져오기
ugs cc scripts new-file <name>      # .js 설정 파일 생성
ugs deploy <dir> -s cloud-code-scripts  # .js 파일 배포
```

**주의**: `run` 명령 없음. 스크립트 실행은 Unity SDK 또는 REST API로만 가능.

### Economy (`ec`)

```bash
ugs ec get-resources -j -q     # 전체 리소스 (draft)
ugs ec get-published -j -q     # 배포된 리소스
ugs ec delete <resource-id>    # 리소스 삭제
ugs ec publish                 # draft → published
ugs ec currency new-file <name>           # .ecc 통화 파일
ugs ec inventory new-file <name>          # .eci 아이템 파일
ugs ec virtual-purchase new-file <name>   # .ecv 가상구매 파일
ugs ec real-money-purchase new-file <name> # .ecr 현금구매 파일
ugs deploy <dir> -s economy              # Economy 파일 배포
```

설정 파일 형식:

**Currency `.ecc`** (파일명 = 리소스 ID)
```json
{
  "$schema": "https://ugs-config-schemas.unity3d.com/v1/economy/economy-currency.schema.json",
  "name": "Gold",
  "initial": 0,
  "max": 0
}
```

**Inventory Item `.eci`**
```json
{
  "$schema": "https://ugs-config-schemas.unity3d.com/v1/economy/economy-inventory.schema.json",
  "name": "Health Potion"
}
```

**Virtual Purchase `.ecv`**
```json
{
  "$schema": "https://ugs-config-schemas.unity3d.com/v1/economy/economy-virtual-purchase.schema.json",
  "name": "Buy Health Potion",
  "costs": [{"resourceId": "GOLD", "amount": 100}],
  "rewards": [{"resourceId": "HEALTH_POTION", "amount": 1}]
}
```

**Real Money Purchase `.ecr`**
```json
{
  "$schema": "https://ugs-config-schemas.unity3d.com/v1/economy/economy-real-purchase.schema.json",
  "name": "Starter Pack",
  "storeIdentifiers": {"googlePlayStore": "com.example.starterpack"},
  "rewards": [{"resourceId": "GEM", "amount": 100}]
}
```

### Cloud Save (`cs`)

```bash
ugs cs data player list -j -q --player-id <id>    # 플레이어 데이터 키 목록
ugs cs data player get -j -q --player-id <id> --keys <key1,key2>  # 데이터 조회
ugs cs data player set --player-id <id> --body '{"key":"val"}'    # 데이터 설정
ugs cs data player query -j -q --fields <field>    # 쿼리
ugs cs data index list -j -q                        # 인덱스 목록
```

### Leaderboards (`lb`)

```bash
ugs lb list -j -q              # 리더보드 목록
ugs lb get <id> -j -q          # 상세
ugs lb delete <id>             # 삭제
ugs lb reset <id>              # 초기화
ugs lb export <dir> <file>     # 내보내기
ugs lb import <dir> <file>     # 가져오기
ugs lb new-file <name>         # .lb 설정 파일 생성
ugs deploy <dir> -s leaderboards  # 배포
```

설정 파일: `.lb`
```json
{
  "$schema": "https://ugs-config-schemas.unity3d.com/v1/leaderboards.schema.json",
  "Name": "Weekly Score",
  "SortOrder": "desc",
  "UpdateType": "keepBest",
  "ResetConfig": {
    "Start": "2026-01-01T00:00:00Z",
    "Schedule": "0 0 * * 1"
  }
}
```

### Player

```bash
ugs player list -j -q          # 플레이어 목록
ugs player get <id> -j -q      # 상세
ugs player create -j -q        # 생성
ugs player delete <id>         # 삭제
ugs player disable <id>        # 비활성화
ugs player enable <id>         # 활성화
```

### Deploy (통합)

```bash
ugs deploy <dir>                        # 전체 서비스 배포
ugs deploy <dir> -s <service>           # 특정 서비스만
ugs deploy <dir> --dry-run              # 시뮬레이션
ugs deploy <dir> --reconcile            # 서버에만 있는 항목 삭제
ugs fetch <dir>                         # 서버 → 로컬
```

지원 서비스 (-s 옵션):
- `remote-config`
- `cloud-code-scripts`
- `economy`
- `leaderboards`

---

## REST API (CLI에서 지원하지 않는 기능)

### Cloud Code 파라미터 스키마 등록

CLI `update` 명령에 `--parameters` 옵션 없음. REST API 필요.

```
PATCH /cloud-code/v1/projects/{pid}/environments/{eid}/scripts/{name}
Authorization: Basic {credentials}
Content-Type: application/json

{"params":[{"name":"amount","type":"NUMERIC","required":false}]}

→ 204 (draft에 저장)
```

```
POST /cloud-code/v1/projects/{pid}/environments/{eid}/scripts/{name}/publish
Authorization: Basic {credentials}
Content-Type: application/json

{}

→ 200 {"version":N,"datePublished":"..."}
```

### Cloud Code 스크립트 실행

CLI `run` 명령 없음. Unity SDK 또는 REST API 필요.

```
POST /cloud-code/v1/projects/{pid}/environments/{eid}/scripts/{name}/run
Authorization: Bearer {player-token}
Content-Type: application/json

{"params":{"key":"value"}}
```

**주의**: 플레이어 인증 토큰 필요 (Service Account로는 불가)

---

## 설정 파일 확장자 요약

| 서비스 | 확장자 | 설명 |
|--------|--------|------|
| Remote Config | `.rc` | 키-값 설정 |
| Cloud Code | `.js` | 스크립트 |
| Economy Currency | `.ecc` | 통화 |
| Economy Inventory | `.eci` | 인벤토리 아이템 |
| Economy Virtual Purchase | `.ecv` | 가상 구매 |
| Economy Real Purchase | `.ecr` | 현금 구매 |
| Leaderboards | `.lb` | 리더보드 |
| Triggers | `.tr` | 트리거 |
| Scheduler | `.sched` | 스케줄러 |
