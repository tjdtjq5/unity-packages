# CI/CD 패키지 v0.6 개선 설계

## 문제 정의

### P1: 아티팩트 용량 문제 (40MB APK → 155MB zip)

**원인 분석**:
`game-ci/unity-builder`는 `build/Android/` 폴더에 APK 외 부산물을 생성한다:

```
build/Android/
├── ServerTest.apk                                    (~40MB) ← 필요
├── ServerTest_BurstDebugInformation_DotsPlayer/      (~50MB) ← 불필요
├── ServerTest_BackUpThisFolder_ButDontShipItWithYourGame/ (~60MB) ← 불필요
└── 기타 메타데이터
```

현재 워크플로우는 폴더 전체를 업로드한 후, release job에서 다시 zip으로 묶어 GitHub Release에 올린다.

**영향받는 코드**:
- `WorkflowGenerator.cs:244-252` — Upload build artifact step
- `WorkflowGenerator.cs:255-283` — AppendReleaseJob

---

### P2: 캐싱 비효율 (매번 ~45분)

**원인 분석**:

| 문제 | 위치 | 영향 |
|------|------|------|
| Library 캐시 키 과민 | `WorkflowGenerator.cs:159` | `Assets/**` 전체 해시 → 매번 캐시 미스 |
| IL2CPP 캐시 키 과민 | `WorkflowGenerator.cs:187` | `Packages/**` 전체 해시 → 패키지 변경 시 전체 재빌드 |
| npm 캐시 무효 | `WorkflowGenerator.cs:193-202` | Unity 빌드에 npm 미사용 |
| Docker 캐시 무효 (buildx) | `WorkflowGenerator.cs:204-213` | game-ci는 buildx 아닌 docker pull 사용 |
| Docker 이미지 매번 pull | 없음 | Unity 이미지 수 GB, 매번 pull (~5-10분) |
| Checkout 전체 히스토리 | `WorkflowGenerator.cs:135` | `fetch-depth: 0` 불필요 (태그에서 버전 추출) |

---

## 변경 설계

### 변경 1: 아티팩트 필터링 (WorkflowGenerator.cs)

**Upload step** — 플랫폼별 glob 패턴으로 필요한 파일만 업로드:

```yaml
# Android (APK)
path: build/Android/*.apk

# Android (AAB)
path: build/Android/*.aab

# iOS — Xcode 프로젝트 전체 필요 (App Store job에서 빌드)
path: build/iOS

# Windows — 실행파일 폴더 전체 필요
path: build/StandaloneWindows64

# WebGL — 빌드 폴더 전체 필요
path: build/WebGL
```

**구현**: `AppendBuildJob`에서 플랫폼별 분기 + `androidBuildFormat` 설정 참조

```
AS-IS:
  sb.AppendLine("          path: build/${{ matrix.targetPlatform }}");

TO-BE:
  플랫폼이 Android만 있으면: path 고정 (*.apk 또는 *.aab)
  멀티 플랫폼이면: if 조건으로 분기
```

**Release job** — Android는 zip 불필요, 파일 직접 첨부:

```
AS-IS:
  모든 아티팩트를 zip으로 묶어서 첨부

TO-BE:
  Android APK/AAB는 그대로 첨부 (zip X)
  Windows/WebGL은 zip으로 묶어서 첨부
  iOS는 release에 올리지 않음 (App Store로 직접 배포)
```

**수정 파일**: `WorkflowGenerator.cs` — `AppendBuildJob`, `AppendReleaseJob`

---

### 변경 2: Library 캐시 키 최적화 (WorkflowGenerator.cs)

```
AS-IS:
  key: Library-${{ matrix.targetPlatform }}-${{ hashFiles('Assets/**', 'Packages/**', 'ProjectSettings/**') }}

TO-BE:
  key: Library-${{ matrix.targetPlatform }}-${{ hashFiles('Packages/manifest.json', 'ProjectSettings/ProjectVersion.txt', 'ProjectSettings/ProjectSettings.asset') }}
```

**근거**:
- Library는 Unity의 임포트 캐시로, 에셋 내용이 아닌 **구조적 변경**에만 무효화가 필요
- `manifest.json` — 패키지 추가/삭제 시
- `ProjectVersion.txt` — Unity 버전 변경 시
- `ProjectSettings.asset` — 빌드 설정 변경 시
- `restore-keys` fallback이 있으므로 정확한 키 미스 시에도 이전 캐시 재사용 가능

---

### 변경 3: IL2CPP 캐시 키 최적화 (WorkflowGenerator.cs)

```
AS-IS:
  key: il2cpp-${{ matrix.targetPlatform }}-${{ hashFiles('Assets/Scripts/**', 'Packages/**') }}

TO-BE:
  key: il2cpp-${{ matrix.targetPlatform }}-${{ hashFiles('Assets/Scripts/**', 'Packages/manifest.json') }}
```

**근거**:
- IL2CPP 캐시는 C# 코드 변환 결과물
- `Packages/**` 전체를 해시하면 패키지 내 에셋/문서 변경에도 캐시 미스 발생
- `manifest.json`만 감시하면 패키지 추가/삭제/버전 변경만 감지

---

### 변경 4: npm/Docker 레거시 캐시 제거 (CacheTypes.cs, WorkflowGenerator.cs)

**npm 캐시**: Unity 빌드 파이프라인에서 npm을 사용하지 않음. 캐시 자체가 무효.
**Docker 레이어 캐시**: `game-ci/unity-builder`는 buildx가 아닌 `docker pull`로 이미지를 가져옴. `/tmp/.buildx-cache` 경로는 사용되지 않음.

```
AS-IS (CacheTypes.cs):
  5개: Library, Gradle, IL2CPP, Npm, Docker

TO-BE:
  4개: Library, Gradle, IL2CPP, DockerImage (신규)
```

**수정 파일**:
- `CacheTypes.cs` — Npm, Docker 제거 + DockerImage 추가
- `WorkflowGenerator.cs` — npm, Docker 레이어 캐시 코드 제거 + DockerImage 캐시 추가
- `CacheHealthChecker.cs` — Docker → DockerImage 참조 변경

**기존 사용자 호환**: `enabledCaches`에 "npm"/"docker"가 남아있을 수 있으나, `WorkflowGenerator`가 해당 ID를 무시하므로 문제 없음. `CacheTypes.All`에서 제거되면 UI에서도 자연스럽게 사라짐.

---

### 변경 5: Unity Docker 이미지 캐싱 — 신규 (WorkflowGenerator.cs)

매번 수 GB Unity Docker 이미지를 pull하는 것이 가장 큰 병목 (~5-10분).
`docker save/load` + `actions/cache`로 이미지를 캐싱한다.

**생성될 YAML**:

```yaml
      - name: Restore Unity Docker image
        id: docker-cache
        uses: actions/cache@v4
        with:
          path: /tmp/unity-image.tar
          key: unity-docker-${{ matrix.targetPlatform }}-${{ hashFiles('ProjectSettings/ProjectVersion.txt') }}

      - name: Load cached Docker image
        if: steps.docker-cache.outputs.cache-hit == 'true'
        run: docker load < /tmp/unity-image.tar

      # ... unity-builder 실행 (기존) ...

      - name: Save Unity Docker image
        if: steps.docker-cache.outputs.cache-hit != 'true'
        run: |
          IMAGE=$(docker images --format '{{.Repository}}:{{.Tag}}' | grep unityci | head -1)
          if [ -n "$IMAGE" ]; then
            docker save "$IMAGE" > /tmp/unity-image.tar
          fi
```

**캐시 키 설계**:
- `unity-docker-{platform}-{ProjectVersion.txt 해시}` — Unity 버전이 바뀔 때만 새로 pull
- Unity 이미지 크기: ~3-5GB (압축). GitHub Actions 캐시 제한 10GB/repo.
- Android 단일 플랫폼이면 여유 충분. 멀티 플랫폼 시 Library + IL2CPP + Docker 합산 주의.

**CacheTypes에 추가**:
```csharp
public const string DockerImage = "docker-image";
// CacheInfo: "Docker Image", "Unity Docker 이미지 캐시 (pull 시간 절약)"
```

**위치**: unity-builder step **직전**에 restore/load, **직후**에 save.

**수정 파일**: `WorkflowGenerator.cs`, `CacheTypes.cs`

---

### 변경 6: Shallow Checkout (WorkflowGenerator.cs)

현재 `fetch-depth: 0`(전체 Git 히스토리)을 가져오지만, 버전은 `GITHUB_REF_NAME`(태그명)에서 추출하므로 전체 히스토리가 불필요하다.

```
AS-IS:
  fetch-depth: 0

TO-BE:
  fetch-depth: 1
```

LFS는 유지한다 (`lfs: true`).

**절약**: ~30초-1분 (리포 크기에 비례)

**수정 파일**: `WorkflowGenerator.cs` — Checkout step

---

### 변경 7: 빌드 타임아웃 조정 (BuildTracker.cs)

현재 10분 타임아웃은 실제 빌드 시간에 비해 너무 짧다.

```
AS-IS: 타임아웃 10분
TO-BE: 타임아웃 60분
```

**수정 파일**: `BuildTracker.cs` — 타임아웃 상수

---

### 변경 8: CacheHealthChecker 검사 보강 (CacheHealthChecker.cs)

기존 12개 검사 중 수정이 필요한 항목과 누락된 검사를 추가한다.

#### 8-A. Android 빌드 포맷 변경 검사 (신규)

APK ↔ AAB 전환 시 Gradle 빌드 파이프라인이 달라지지만 현재 감지하지 않는다.

```csharp
const string PREF_ANDROID_FORMAT = PREF + "AndroidFormat";

static void CheckAndroidFormatChange(List<Alert> alerts, string currentFormat)
{
    var saved = EditorPrefs.GetString(PREF_ANDROID_FORMAT, "");
    if (string.IsNullOrEmpty(saved)) return;
    if (saved != currentFormat)
        alerts.Add(new Alert(Severity.Warning,
            $"Android 빌드 포맷 변경 ({saved} → {currentFormat})",
            CacheTypes.Gradle));
}
```

**연동 필요**:
- `MainThreadSnapshot`에 `androidFormat` 필드 추가
- `RunAllChecks`에서 `CheckAndroidFormatChange` 호출
- `SaveBuildSnapshot`에서 `PREF_ANDROID_FORMAT` 저장
- `EnsureBaselineSnapshot`에서 누락 키 보충

#### 8-B. CacheExpiry에 DockerImage 추가

7일 미사용 시 DockerImage 캐시도 GitHub에서 삭제되므로 경고 대상에 포함.

```
AS-IS (CheckCacheExpiry):
  affected: CacheTypes.Library, CacheTypes.Gradle, CacheTypes.IL2CPP

TO-BE:
  affected: CacheTypes.Library, CacheTypes.Gradle, CacheTypes.IL2CPP, CacheTypes.DockerImage
```

#### 8-C. CheckUnityVersionChange의 Docker → DockerImage

```
AS-IS:
  affected: CacheTypes.Library, CacheTypes.IL2CPP, CacheTypes.Docker

TO-BE:
  affected: CacheTypes.Library, CacheTypes.IL2CPP, CacheTypes.DockerImage
```

**수정 파일**: `CacheHealthChecker.cs`

---

## 수정 파일 목록

| 파일 | 변경 내용 | 난이도 |
|------|----------|--------|
| `WorkflowGenerator.cs` | 아티팩트 필터링 + 캐시 키 최적화 + npm/Docker 레이어 제거 + Docker 이미지 캐싱 + Release job 개선 + shallow checkout | 중 |
| `CacheTypes.cs` | Npm, Docker 제거 + DockerImage 추가 | 하 |
| `CacheHealthChecker.cs` | Docker → DockerImage 참조 변경 + CacheExpiry DockerImage 추가 + Android 포맷 변경 검사 신규 | 중 |
| `BuildTracker.cs` | 타임아웃 60분으로 변경 | 하 |

---

## 예상 효과

| 지표 | Before | After (예상) |
|------|--------|-------------|
| 릴리즈 다운로드 용량 | ~155MB zip | ~40MB apk |
| Library 캐시 히트율 | 낮음 (매번 미스) | 높음 (구조 변경 시만 미스) |
| IL2CPP 캐시 히트율 | 낮음 | 높음 (스크립트 변경 시만 미스) |
| Docker 이미지 pull | 매번 5-10분 | 캐시 히트 시 ~1분 (load) |
| 불필요한 캐시 step | 2개 (npm, Docker layer) | 0개 |
| **캐시 히트 시 빌드 시간** | **~45분** | **~8-12분** |

### 빌드 시간 breakdown (캐시 전체 히트 시)

| 단계 | Before | After |
|------|--------|-------|
| Free disk space | ~1-2분 | ~1-2분 |
| Checkout | ~1-2분 (full history) | ~30초 (shallow) |
| Docker image | ~5-10분 (pull) | ~1분 (cache load) |
| Cache restore | ~1-2분 | ~1-2분 |
| Unity 빌드 | ~20-25분 | ~3-5분 (캐시 히트) |
| Gradle/APK | ~2-3분 | ~2-3분 |
| Upload | ~2분 | ~30초 (APK만) |
| **합계** | **~35-45분** | **~8-12분** |

---

## GitHub Actions 캐시 용량 계획

GitHub Actions 캐시 제한: **10GB/repo**

| 캐시 | 예상 크기 |
|------|----------|
| Library (Android) | ~1-2GB |
| IL2CPP (Android) | ~500MB-1GB |
| Gradle | ~200-500MB |
| Docker Image (Unity) | ~3-5GB |
| **합계** | **~5-8.5GB** |

Android 단일 플랫폼에서는 10GB 이내. 멀티 플랫폼 추가 시 캐시 eviction 가능성 있으므로 모니터링 필요.

---

## 향후 개선 (이번 범위 밖)

- **Self-hosted runner**: Docker 이미지 + Library가 로컬에 상주하므로 가장 극적인 속도 개선 (~5분 이내 가능). 단, 인프라 관리 비용 발생.
- **Free disk space 조건부 스킵**: Docker 이미지 캐시 히트 시 디스크 여유가 충분하면 스킵 가능. 리스크 있어 우선 유지.
