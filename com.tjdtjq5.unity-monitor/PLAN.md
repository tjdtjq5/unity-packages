# com.tjdtjq5.unity-monitor 패키지 계획

> 작성일: 2026-03-22
> 상태: 계획 단계 (미구현)

## 개요

Unity 에디터 이벤트를 자동 캡처하고 event-log.md로 기록하는 독립 모니터링 패키지.
Claude 패키지와 무관하게 단독으로 사용 가능.

## claude 패키지에서 분리할 것

현재 `com.tjdtjq5.claude`의 `UnityMonitor.cs`에 있는 기능을 이 패키지로 이전.

## 이벤트 소스

| 이벤트 | Unity API | 등급 |
|--------|-----------|------|
| 콘솔 에러/예외 | `Application.logMessageReceived` | ACTION |
| 콘솔 경고 | `Application.logMessageReceived` | REVIEW |
| 콘솔 일반 로그 | `Application.logMessageReceived` | FYI |
| 컴파일 에러 | `CompilationPipeline.assemblyCompilationFinished` | ACTION |
| 컴파일 성공 | `CompilationPipeline.compilationFinished` | FYI |
| 빌드 실패 | `IPostprocessBuildWithReport` | ACTION |
| 빌드 성공 | `IPostprocessBuildWithReport` | FYI |
| 플레이모드 시작 | `EditorApplication.playModeStateChanged` | FYI |
| 플레이모드 종료 | `EditorApplication.playModeStateChanged` | REVIEW (요약 트리거) |
| 프레임 스파이크 | `Time.deltaTime` 감시 | REVIEW |
| GC 스파이크 | `ProfilerRecorder` | REVIEW |
| 드로우콜 과다 | `UnityStats.drawCalls` | REVIEW |

## 이벤트 등급 시스템

- **ACTION**: 즉시 대응 필요 (컴파일 에러, 런타임 예외, 빌드 실패)
- **REVIEW**: 확인 필요하지만 급하지 않음 (성능 이슈, 경고)
- **FYI**: 기록만, 행동 불필요 (로그, 상태 변경)

## event-log.md 자동 관리

### 파일 구조

```markdown
# Unity Event Log

> 세션: 2026-03-22 10:00 ~

## ACTION (미처리)
- [ ] [10:30] COMPILE ERROR: CS0103 in Foo.cs:42

## REVIEW (미확인)
- [ ] [10:28] PERF: 스파이크 48ms, DC 523

## Timeline
- [10:30] 컴파일 에러 1건
- [10:20] 플레이 종료 — 에러 0건, fps 42
- [10:10] 플레이 시작

## 최근 세션 성능
| 지표 | 평균 | 최저 | 최대 |
|------|------|------|------|
| FPS | 42 | 18 | 60 |
| DC | 312 | 180 | 523 |
| GC/frame | 0.3KB | 0KB | 2.1KB |

<details><summary>아카이브 (처리 완료)</summary>
- [x] [10:15] COMPILE ERROR → 수정됨
</details>
```

### 자동 초기화/정리 규칙

| 시점 | 동작 |
|------|------|
| 에디터 시작 | 완료항목([x]) → 아카이브. 미처리 유지 |
| 플레이 시작 | 성능 섹션 초기화 |
| 플레이 종료 | 성능 요약 생성 |
| 아카이브 50건 초과 | 오래된 것부터 삭제 |

## 성능 수집 (플레이모드 전용)

```csharp
// EditorWindow 또는 MonoBehaviour에서 Update마다 수집
// 임계치 초과 시 REVIEW 이벤트 생성
float fps = 1f / Time.deltaTime;
int drawCalls = UnityStats.drawCalls;
int batches = UnityStats.batches;
long gcAlloc = ProfilerRecorder("GC Allocated In Frame").LastValue;
```

수집 데이터는 **개별 전달하지 않고** 플레이 종료 시 요약 테이블로 기록.
임계치 초과(fps<30, DC>500, GC>1KB)만 REVIEW 이벤트로 즉시 기록.

## 외부 연동 API

```csharp
// 다른 패키지(claude 등)가 이벤트를 구독할 수 있는 API
public static class MonitorEvents
{
    public static event Action<MonitorEvent> OnEvent;
    // MonitorEvent { Priority, Category, Message, SourceFile, ... }
}
```

claude 패키지는 이 API를 구독해서 Channel로 전달.

## 패키지 구조 (예상)

```
com.tjdtjq5.unity-monitor/
├── Editor/
│   ├── Core/
│   │   ├── MonitorEvents.cs        ← 이벤트 API (외부 구독용)
│   │   ├── EventPriority.cs        ← ACTION/REVIEW/FYI enum
│   │   └── MonitorEvent.cs         ← 이벤트 데이터 구조
│   ├── Collectors/
│   │   ├── ConsoleCollector.cs     ← 콘솔 로그 캡처
│   │   ├── CompileCollector.cs     ← 컴파일 에러 캡처
│   │   ├── BuildCollector.cs       ← 빌드 결과 캡처
│   │   ├── PlayModeCollector.cs    ← 플레이모드 상태
│   │   └── PerformanceCollector.cs ← fps/GC/DC 수집
│   ├── Logger/
│   │   ├── EventLogWriter.cs       ← event-log.md 기록
│   │   └── EventLogManager.cs      ← 초기화/정리/아카이브
│   ├── UI/
│   │   └── MonitorDashboard.cs     ← 에디터 대시보드 (선택)
│   └── Settings/
│       └── MonitorSettings.cs      ← EditorPrefs 기반 설정
├── package.json
└── README.md
```

## 의존성

```
editor-toolkit (독립)
  └── unity-monitor (독립 — Claude 없이 사용 가능)
       └── claude (monitor API 구독 → Channel 전달)
```

## claude 패키지 변경 사항 (분리 시)

1. `UnityMonitor.cs` 삭제 (monitor 패키지로 이전)
2. `MonitorEvents.OnEvent` 구독 → Channel Bridge로 전달
3. Settings에서 모니터 관련 항목 제거 (monitor 패키지가 관리)
4. `package.json`에 `com.tjdtjq5.unity-monitor` 의존성 추가
