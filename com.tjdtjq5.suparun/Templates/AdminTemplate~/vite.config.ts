import { defineConfig, loadEnv } from 'vite'
import react from '@vitejs/plugin-react'

// ADR-0003 결정 3 — 산출물은 고정 파일명.
//   해시 파일명을 쓰면 DeployManager가 dist/를 재귀 순회해야 하는데,
//   고정하면 LoadTemplate 호출 2줄 추가로 끝난다.
//
// ADR-0003 결정 10 (2026-07-25 개정) — 개발 중 API는 **배포된 서버**로 프록시한다.
//   당초 "로컬 dotnet 서버" 안이었으나 철회했다:
//     · 로컬 서버도 결국 같은 Supabase 실서버에 붙으므로 실데이터 위험이 줄지 않는다
//     · SupaRun에 로컬 서버 실행 기능이 없다 (PrepareBuildTest는 빌드 검증 전용)
//     · [SpecData] 자동 반영과도 무관하다 — 그 병목은 메타가 C# 소스에 박히는 것이지 서버 위치가 아니다
//   실데이터 보호가 필요해지면 "개발용 Supabase 프로젝트 분리"를 별건으로 다룬다.
export default defineConfig(({ mode, command }) => {
  const env = loadEnv(mode, process.cwd(), '')
  const serverUrl = env.VITE_SERVER_URL

  if (command === 'serve' && !serverUrl) {
    console.warn(
      '[suparun-admin] VITE_SERVER_URL 이 설정되지 않았습니다.\n' +
        '  .env.local 에 배포된 서버 주소를 넣으세요 (.env.example 참고).\n' +
        '  없으면 /admin/api 요청이 전부 404 가 됩니다.',
    )
  }

  return {
    // src/index.html 이 진입점. 바닐라 3,950줄이 아직 여기 있고,
    // 화면을 하나씩 React로 옮기며 점점 빠져나간다 (ADR-0003 결정 1).
    root: 'src',
    base: './',

    plugins: [react()],

    build: {
      outDir: '../dist',
      emptyOutDir: true,
      rollupOptions: {
        output: {
          entryFileNames: 'assets/index.js',
          chunkFileNames: 'assets/[name].js',
          assetFileNames: 'assets/[name][extname]',
        },
      },
    },

    // 주소가 없으면 프록시를 아예 등록하지 않는다 — 죽은 설정이 있는 것보다 낫다.
    server: serverUrl
      ? { proxy: { '/admin/api': { target: serverUrl, changeOrigin: true } } }
      : {},
  }
})
