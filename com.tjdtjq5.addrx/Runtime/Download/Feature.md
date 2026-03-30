# Download

- **상태**: stable
- **용도**: 원격 Addressable 번들 다운로드 관리 -- 큐 기반 순차 다운로드 + 재시도 + 카탈로그 업데이트

## 의존성

| 폴더 | 관계 |
|------|------|
| `../Core/` | AddrX partial class 확장 |
| `../Logging/` | 로그 출력 (AddrXLog) |

## 구조

```
Runtime/Download/
├── AddrX.Download.cs     # AddrX partial class -- Download(), CheckCatalogUpdatesAsync(), UpdateCatalogsAsync()
├── AddrXDownloader.cs    # 다운로드 매니저 -- 체이닝 빌더 + 순차 다운로드 + 재시도
└── CatalogChecker.cs     # 리모트 카탈로그 업데이트 확인/적용
```

## API

### AddrX (partial class 확장)

| 멤버 | 시그니처 | 설명 |
|------|---------|------|
| 메서드 | `AddrXDownloader Download(params string[] keys)` | 다운로드 매니저 생성 (체이닝 빌더) |
| 메서드 | `Task<List<string>> CheckCatalogUpdatesAsync()` | 업데이트 가능한 카탈로그 목록 반환 |
| 메서드 | `Task<bool> UpdateCatalogsAsync(List<string> catalogIds = null)` | 카탈로그 업데이트 적용 (null이면 자동 체크) |

### AddrXDownloader (class)

| 멤버 | 시그니처 | 설명 |
|------|---------|------|
| 메서드 | `AddrXDownloader Add(params string[] keys)` | 다운로드 대상 라벨/키 추가 |
| 메서드 | `AddrXDownloader WithRetry(int maxRetries)` | 최대 재시도 횟수 설정 (기본 3회) |
| 메서드 | `AddrXDownloader OnProgress(Action<DownloadProgress> callback)` | 진행률 콜백 등록 |
| 메서드 | `AddrXDownloader OnComplete(Action callback)` | 전체 완료 콜백 등록 |
| 메서드 | `AddrXDownloader OnError(Action<string> callback)` | 에러 콜백 등록 |
| 메서드 | `Task<long> GetTotalSizeAsync()` | 전체 다운로드 크기(바이트) 조회 |
| 메서드 | `Task StartAsync()` | 다운로드 시작 (키 순서대로 순차 실행) |

### DownloadProgress (readonly struct)

| 필드 | 타입 | 설명 |
|------|------|------|
| `Current` | `int` | 현재까지 처리된 키 수 |
| `Total` | `int` | 전체 키 수 |
| `Key` | `string` | 현재 처리 중인 키 |
| `Success` | `bool` | 현재 키 성공 여부 |
| `Percent` | `float` | 전체 진행률 (0~1, 프로퍼티) |

### CatalogChecker (static class)

| 멤버 | 시그니처 | 설명 |
|------|---------|------|
| 메서드 | `Task<List<string>> CheckForUpdatesAsync()` | 업데이트 가능한 카탈로그 ID 목록 반환 |
| 메서드 | `Task<bool> UpdateCatalogsAsync(List<string> catalogIds = null)` | 카탈로그 업데이트 적용 |

## 사용 예시

```csharp
// 다운로드 크기 확인 + 다운로드
var downloader = AddrX.Download("enemies", "weapons")
    .WithRetry(5)
    .OnProgress(p => Debug.Log($"{p.Percent:P0}"))
    .OnError(e => Debug.LogError(e));

long size = await downloader.GetTotalSizeAsync();
if (size > 0) await downloader.StartAsync();
```

## 주의사항

- 로컬 전용 프로젝트(Remote 번들을 사용하지 않는 경우)에서는 이 모듈을 사용하지 않아도 된다.
- `DownloadWithRetry`는 실패 시 1초, 2초, 3초... 점진적 대기 후 재시도한다 (Exponential Backoff).
- 이미 캐시된 번들은 크기 0으로 반환되어 다운로드를 건너뛴다.
- `StartAsync()`는 키 순서대로 순차 실행. 병렬 다운로드는 지원하지 않는다.
