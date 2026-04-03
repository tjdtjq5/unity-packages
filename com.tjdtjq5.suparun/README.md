# SupaRun

Unity Editor에서 게임 서버 인프라를 관리하는 올인원 패키지.
ASP.NET + Supabase + Cloud Run 자동 배포.

## 설치

manifest.json에 추가:

```json
"com.tjdtjq5.suparun": "https://github.com/tjdtjq5/unity-packages.git?path=com.tjdtjq5.suparun#suparun/v0.3.6"
```

### 의존성

- `com.tjdtjq5.editor-toolkit` >= 1.1.0
- `com.unity.nuget.newtonsoft-json` >= 3.2.1

## 주요 기능

- **Supabase 연동**: Auth, DB, Realtime, Storage
- **Cloud Run 배포**: ASP.NET 서버 자동 빌드 + 배포
- **Auth**: Google/Apple/GameCenter/GPGS 플랫폼 로그인
- **SecureStorage**: 플랫폼별 보안 저장소 (Android KeyStore, iOS Keychain)
- **Editor Window**: 통합 설정 + 배포 관리 UI
- **프로젝트별 설정 격리**: 멀티 프로젝트 환경에서 EditorPrefs 충돌 방지

## 라이선스

MIT
