# EOS Editor / Build

## 상태
done

## 용도
PlayEveryWare EOS 플러그인의 `eos_dependencies.androidlib` 모듈을 Unity 6 (AGP 8.x) 호환 상태로 빌드 시점에 자동 패치한다. 원본 파일은 건드리지 않고, Gradle 프로젝트 생성 직후 복사본만 수정한다.

## 배경
- PlayEveryWare EOS v6.0.2의 `eos_dependencies.androidlib/build.gradle`은 AGP 3.6 시절 형식이라 AGP 8.x에서 "Namespace not specified" 에러 발생
- 공식 입장: Unity 6 미지원 + 지원 계획 없음 (Issue #863, 2024-08)
- 업스트림 업그레이드로 해결 불가 → 프로젝트 측 빌드 후처리로 우회

## 의존성
- `UnityEditor.Android.IPostGenerateGradleAndroidProject` (Unity Android Build Support)

## 포함 기능
- `EOSAndroidGradlePatcher` — Gradle 프로젝트 생성 직후 `eos_dependencies.androidlib` 모듈 자동 패치

## 구조
| 파일 | 설명 |
|------|------|
| EOSAndroidGradlePatcher.cs | IPostGenerateGradleAndroidProject 구현체 |
| ../Tjdtjq5.EOS.Editor.asmdef | Editor 전용 어셈블리 |

## 패치 내용
`Library/Bee/Android/Prj/IL2CPP/Gradle/unityLibrary/eos_dependencies.androidlib/` 하위의 복사본에 대해:

**build.gradle**:
1. `buildscript { ... }` 블록 전체 제거 — Unity 루트가 AGP 8.10을 주입하므로 모듈 수준 buildscript는 불필요. 남겨두면 `classpath "com.android.tools.build:gradle:3.6.0"`이 AGP 3.6 시절의 고대 의존성(kotlin-stdlib-jdk8:1.3.61, asm:7.0, protobuf:3.4.0, bouncycastle:1.56 등)을 끌어오다 resolve 실패
2. `android { ... }` 블록 최상단에 `namespace "com.pew.eos_dependencies"` 삽입 (없을 때만)
3. `buildToolsVersion`을 Unity `unityLibrary/build.gradle`의 값과 동기화 — 제거만 하면 AGP 기본값(예: 35.0.0)으로 fallback되어 Unity SDK에 없는 버전을 Gradle이 Program Files 경로에 자동 설치 시도 → 권한 실패
4. `jcenter()` 제거 (2022년 shutdown)

**AndroidManifest.xml (eos_dependencies)**:
- `<manifest ... package="com.pew.eos_dependencies">` 의 `package` 속성 제거
- namespace 값을 build.gradle에 동일하게 선언했으므로 R 클래스 경로(`com.pew.eos_dependencies.R`) 유지 → EOS Java 코드 참조 안 깨짐
- `<application android:theme="@style/Theme.AppCompat..."/>` 의 `android:theme` 속성 제거 — Unity 6 `UnityPlayerGameActivity`는 Material3 테마를 쓰는데 application 전역에 AppCompat을 강제하면 일부 기기에서 크래시/앱 숨김 유발

**unityLibrary/build.gradle + launcher/build.gradle** (core library desugaring 활성화):
- `eos-sdk.aar`이 Java 8+ API(`java.time` 등)를 사용해서 이 AAR를 참조하는 모듈에 desugaring 필수
- `android.compileOptions`에 `coreLibraryDesugaringEnabled true` 추가
- `dependencies`에 `coreLibraryDesugaring 'com.android.tools:desugar_jdk_libs:2.0.4'` 추가

**launcher/src/main/AndroidManifest.xml** (installLocation 교체):
- `android:installLocation="preferExternal"` → `"auto"` 강제 교체
- Unity 6000.3에서 Player Settings와 무관하게 `preferExternal`이 박히는 회귀 버그 대응 (UUM-25965 유사 증상)
- `preferExternal`은 제조사 런쳐(삼성/샤오미 등)에서 앱을 드로어에서 숨기는 원인

## 멱등성
- namespace가 이미 있으면 skip
- package가 이미 없으면 skip
- 빌드 재실행해도 안전

## 주의사항
- 패치 대상은 Unity가 `Library/Bee/` 아래로 복사한 **빌드 전용 사본**. `Assets/Plugins/Android/EOS/`와 PackageCache 원본은 변경되지 않음
- EOS 플러그인이 Unity 6 자체 지원하는 버전으로 업그레이드되면 이 파일 전체 삭제 가능
- **런타임 호환성은 별도 검증 필요** — 빌드 통과가 런타임 정상 동작을 보장하지 않음. 실기기에서 EOS 로그인 / 로비 / P2P 전송 테스트 필수
