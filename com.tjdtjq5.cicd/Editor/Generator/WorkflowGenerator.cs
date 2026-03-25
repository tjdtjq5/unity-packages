#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Tjdtjq5.CICD.Editor
{
    /// <summary>GitHub Actions 워크플로우 yml 생성 엔진</summary>
    public static class WorkflowGenerator
    {
        /// <summary>설정 기반으로 완성된 yml 문자열 반환</summary>
        public static string Generate(BuildAutomationSettings settings)
        {
            var sb = new StringBuilder();

            AppendHeader(sb, settings);
            AppendBuildJob(sb, settings);

            if (settings.deployGitHubReleases)
                AppendReleaseJob(sb, settings);

            if (settings.deployGooglePlay)
                AppendGooglePlayJob(sb);

            if (settings.deployAppStore)
                AppendAppStoreJob(sb);

            if (settings.deploySteam)
                AppendSteamJob(sb, settings);

            if (settings.notifyChannel != NotifyChannel.None)
                AppendNotifyJob(sb, settings);

            return sb.ToString();
        }

        /// <summary>생성된 yml을 프로젝트에 저장. 파일 경로 반환.</summary>
        public static string SaveToProject(string yml)
        {
            var repoRoot = GitHelper.GetRepoRoot();
            if (string.IsNullOrEmpty(repoRoot))
                repoRoot = Directory.GetParent(UnityEngine.Application.dataPath)!.FullName;

            var dir = Path.Combine(repoRoot, ".github", "workflows");
            Directory.CreateDirectory(dir);

            var path = Path.Combine(dir, "build-and-deploy.yml");
            File.WriteAllText(path, yml);
            _cachedExists = true;
            return path;
        }

        // ── 캐시 ──
        static bool? _cachedExists;

        /// <summary>캐시 초기화</summary>
        public static void InvalidateCache() => _cachedExists = null;

        /// <summary>yml 파일이 이미 존재하는지 확인 (캐싱)</summary>
        public static bool WorkflowExists()
        {
            if (_cachedExists.HasValue) return _cachedExists.Value;
            var repoRoot = GitHelper.GetRepoRoot();
            _cachedExists = !string.IsNullOrEmpty(repoRoot) &&
                File.Exists(Path.Combine(repoRoot, ".github", "workflows", "build-and-deploy.yml"));
            return _cachedExists.Value;
        }

        // ─────────────────────────────────────────────
        // yml 조각 생성
        // ─────────────────────────────────────────────

        static void AppendHeader(StringBuilder sb, BuildAutomationSettings settings)
        {
            sb.AppendLine("# 이 파일은 Build Automation 패키지가 자동 생성했습니다.");
            sb.AppendLine("# 수동 편집하지 마세요. 설정 변경 후 재생성하세요.");
            sb.AppendLine();
            sb.AppendLine("name: Build and Deploy");
            sb.AppendLine();
            sb.AppendLine("on:");
            sb.AppendLine("  push:");
            sb.AppendLine("    tags:");
            sb.AppendLine("      - 'v*'");
            // branches 트리거 제거: save-always로 태그 빌드에서도 캐시 저장됨
            // branches를 넣으면 태그+브랜치 동시 push 시 중복 빌드 발생

            sb.AppendLine();
        }

        static void AppendBuildJob(StringBuilder sb, BuildAutomationSettings settings)
        {
            var platforms = GetPlatformList(settings);

            sb.AppendLine("jobs:");
            sb.AppendLine("  build:");
            sb.AppendLine("    name: Build (${{ matrix.targetPlatform }})");

            // iOS는 macOS 러너 필요
            if (settings.enableIOS && platforms.Count > 1)
                sb.AppendLine("    runs-on: ${{ matrix.targetPlatform == 'iOS' && 'macos-latest' || 'ubuntu-latest' }}");
            else if (settings.enableIOS && platforms.Count == 1)
                sb.AppendLine("    runs-on: macos-latest");
            else
                sb.AppendLine("    runs-on: ubuntu-latest");

            // 매트릭스 (include 방식: 플랫폼별 artifactPath 포함)
            sb.AppendLine("    strategy:");
            sb.AppendLine("      fail-fast: false");
            sb.AppendLine("      matrix:");
            sb.AppendLine("        include:");
            foreach (var p in platforms)
            {
                sb.AppendLine($"          - targetPlatform: {p}");
                sb.AppendLine($"            artifactPath: {GetArtifactPath(p, settings)}");
            }

            // outputs (release job에서 사용)
            sb.AppendLine("    outputs:");
            sb.AppendLine("      version: ${{ steps.extract_version.outputs.version }}");

            // steps
            sb.AppendLine("    steps:");

            // 디스크 정리 (Unity Docker 이미지가 크기 때문에 필수)
            sb.AppendLine("      - name: Free disk space");
            sb.AppendLine("        uses: jlumbroso/free-disk-space@main");
            sb.AppendLine("        with:");
            sb.AppendLine("          tool-cache: false");
            sb.AppendLine("          large-packages: false");
            sb.AppendLine("          docker-images: true");
            sb.AppendLine("          swap-storage: true");
            sb.AppendLine();

            // checkout (shallow — 버전은 GITHUB_REF_NAME에서 추출하므로 히스토리 불필요)
            sb.AppendLine("      - name: Checkout");
            sb.AppendLine("        uses: actions/checkout@v4");
            sb.AppendLine("        with:");
            sb.AppendLine("          fetch-depth: 1");
            sb.AppendLine("          lfs: true");
            sb.AppendLine();

            // 버전 추출 (태그 빌드: 태그에서 추출, 브랜치 빌드: dev 버전)
            sb.AppendLine("      - name: Extract version");
            sb.AppendLine("        id: extract_version");
            sb.AppendLine("        run: |");
            sb.AppendLine("          if [[ \"$GITHUB_REF\" == refs/tags/v* ]]; then");
            sb.AppendLine("            echo \"version=${GITHUB_REF_NAME#v}\" >> $GITHUB_OUTPUT");
            sb.AppendLine("          else");
            sb.AppendLine("            echo \"version=0.0.0-dev\" >> $GITHUB_OUTPUT");
            sb.AppendLine("          fi");
            sb.AppendLine();

            // ── 캐시 (활성화된 캐시만 step 생성) ──
            var caches = settings.enabledCaches;

            if (caches.Contains(CacheTypes.Library))
            {
                sb.AppendLine("      - name: Cache Library");
                sb.AppendLine("        uses: actions/cache@v4");
                sb.AppendLine("        with:");
                sb.AppendLine("          path: Library");
                sb.AppendLine("          key: Library-${{ matrix.targetPlatform }}-${{ hashFiles('Packages/manifest.json', 'ProjectSettings/ProjectVersion.txt', 'ProjectSettings/ProjectSettings.asset') }}");
                sb.AppendLine("          restore-keys: |");
                sb.AppendLine("            Library-${{ matrix.targetPlatform }}-");
                sb.AppendLine("          save-always: true");
                sb.AppendLine();
            }

            if (caches.Contains(CacheTypes.Gradle))
            {
                sb.AppendLine("      - name: Cache Gradle");
                sb.AppendLine("        if: matrix.targetPlatform == 'Android'");
                sb.AppendLine("        uses: actions/cache@v4");
                sb.AppendLine("        with:");
                sb.AppendLine("          path: |");
                sb.AppendLine("            ~/.gradle/caches");
                sb.AppendLine("            ~/.gradle/wrapper");
                sb.AppendLine("          key: gradle-${{ hashFiles('Assets/Plugins/Android/*.gradle', 'Assets/Plugins/Android/*.properties') }}");
                sb.AppendLine("          restore-keys: gradle-");
                sb.AppendLine("          save-always: true");
                sb.AppendLine();
            }

            if (caches.Contains(CacheTypes.IL2CPP))
            {
                sb.AppendLine("      - name: Cache IL2CPP");
                sb.AppendLine("        uses: actions/cache@v4");
                sb.AppendLine("        with:");
                sb.AppendLine("          path: Library/Il2cppBuildCache");
                sb.AppendLine("          key: il2cpp-${{ matrix.targetPlatform }}-${{ hashFiles('Assets/**/*.cs', 'Packages/manifest.json') }}");
                sb.AppendLine("          restore-keys: il2cpp-${{ matrix.targetPlatform }}-");
                sb.AppendLine("          save-always: true");
                sb.AppendLine();
            }

            // Docker 이미지 캐싱 (restore + load)
            if (caches.Contains(CacheTypes.DockerImage))
            {
                sb.AppendLine("      - name: Restore Unity Docker image");
                sb.AppendLine("        id: docker-cache");
                sb.AppendLine("        uses: actions/cache@v4");
                sb.AppendLine("        with:");
                sb.AppendLine("          path: /tmp/unity-image.tar");
                sb.AppendLine("          key: unity-docker-${{ matrix.targetPlatform }}-${{ hashFiles('ProjectSettings/ProjectVersion.txt') }}");
                sb.AppendLine();
                sb.AppendLine("      - name: Load cached Docker image");
                sb.AppendLine("        if: steps.docker-cache.outputs.cache-hit == 'true'");
                sb.AppendLine("        run: docker load < /tmp/unity-image.tar");
                sb.AppendLine();
            }

            // Unity 빌드
            sb.AppendLine("      - name: Build Unity project");
            sb.AppendLine("        uses: game-ci/unity-builder@v4");
            sb.AppendLine("        env:");

            sb.AppendLine("          UNITY_LICENSE: ${{ secrets.UNITY_LICENSE }}");
            sb.AppendLine("          UNITY_EMAIL: ${{ secrets.UNITY_EMAIL }}");
            sb.AppendLine("          UNITY_PASSWORD: ${{ secrets.UNITY_PASSWORD }}");

            sb.AppendLine("        with:");
            sb.AppendLine("          targetPlatform: ${{ matrix.targetPlatform }}");
            sb.AppendLine("          versioning: Custom");
            sb.AppendLine("          version: ${{ steps.extract_version.outputs.version }}");

            // Android 서명
            if (settings.enableAndroid)
            {
                if (settings.androidBuildFormat == AndroidBuildFormat.AAB)
                    sb.AppendLine("          androidAppBundle: ${{ matrix.targetPlatform == 'Android' }}");

                sb.AppendLine("          androidKeystoreName: user.keystore");
                sb.AppendLine("          androidKeystoreBase64: ${{ secrets.ANDROID_KEYSTORE_BASE64 }}");
                sb.AppendLine("          androidKeystorePass: ${{ secrets.ANDROID_KEYSTORE_PASS }}");
                sb.AppendLine("          androidKeyaliasName: ${{ secrets.ANDROID_KEY_ALIAS }}");
                sb.AppendLine("          androidKeyaliasPass: ${{ secrets.ANDROID_KEY_PASS }}");
            }

            sb.AppendLine();

            // Docker 이미지 캐싱 (save — 캐시 미스 시만)
            if (caches.Contains(CacheTypes.DockerImage))
            {
                sb.AppendLine("      - name: Save Unity Docker image");
                sb.AppendLine("        if: always() && steps.docker-cache.outputs.cache-hit != 'true'");
                sb.AppendLine("        run: |");
                sb.AppendLine("          IMAGE=$(docker images --format '{{.Repository}}:{{.Tag}}' | grep unityci | head -1)");
                sb.AppendLine("          if [ -n \"$IMAGE\" ]; then");
                sb.AppendLine("            docker save \"$IMAGE\" > /tmp/unity-image.tar");
                sb.AppendLine("          fi");
                sb.AppendLine();
            }

            // 아티팩트 업로드 (태그 빌드에서만 — 브랜치 빌드는 캐시 워밍만)
            sb.AppendLine("      - name: Upload build artifact");
            sb.AppendLine("        if: startsWith(github.ref, 'refs/tags/v')");
            sb.AppendLine("        uses: actions/upload-artifact@v4");
            sb.AppendLine("        with:");
            sb.AppendLine("          name: build-${{ matrix.targetPlatform }}");
            sb.AppendLine("          path: ${{ matrix.artifactPath }}");
            sb.AppendLine("          retention-days: 14");
            sb.AppendLine();
        }

        static void AppendReleaseJob(StringBuilder sb, BuildAutomationSettings settings)
        {
            sb.AppendLine("  release:");
            sb.AppendLine("    name: Create GitHub Release");
            sb.AppendLine("    needs: build");
            sb.AppendLine("    if: startsWith(github.ref, 'refs/tags/v')");
            sb.AppendLine("    runs-on: ubuntu-latest");
            sb.AppendLine("    permissions:");
            sb.AppendLine("      contents: write");
            sb.AppendLine("    steps:");
            sb.AppendLine("      - name: Download all artifacts");
            sb.AppendLine("        uses: actions/download-artifact@v4");
            sb.AppendLine("        with:");
            sb.AppendLine("          path: artifacts");
            sb.AppendLine();
            sb.AppendLine("      - name: Prepare release assets");
            sb.AppendLine("        run: |");
            sb.AppendLine("          mkdir -p release");
            sb.AppendLine("          # APK/AAB 단일 파일은 그대로 복사 (zip 불필요)");
            sb.AppendLine("          find artifacts -name '*.apk' -o -name '*.aab' | while read f; do cp \"$f\" release/; done");
            sb.AppendLine("          # 폴더형 아티팩트(Windows, WebGL 등)는 zip으로 묶기");
            sb.AppendLine("          cd artifacts");
            sb.AppendLine("          for dir in */; do");
            sb.AppendLine("            if ls \"${dir}\"*.apk \"${dir}\"*.aab 2>/dev/null | grep -q .; then continue; fi");
            sb.AppendLine("            zip -r \"../release/${dir%/}.zip\" \"$dir\"");
            sb.AppendLine("          done");
            sb.AppendLine();
            sb.AppendLine("      - name: Create Release");
            sb.AppendLine("        uses: softprops/action-gh-release@v2");
            sb.AppendLine("        with:");
            sb.AppendLine("          files: release/*");
            sb.AppendLine("          generate_release_notes: true");
            sb.AppendLine();
        }

        static void AppendGooglePlayJob(StringBuilder sb)
        {
            sb.AppendLine("  deploy-google-play:");
            sb.AppendLine("    name: Deploy to Google Play");
            sb.AppendLine("    needs: build");
            sb.AppendLine("    runs-on: ubuntu-latest");
            sb.AppendLine("    if: contains(github.ref, 'v')");
            sb.AppendLine("    steps:");
            sb.AppendLine("      - name: Download Android artifact");
            sb.AppendLine("        uses: actions/download-artifact@v4");
            sb.AppendLine("        with:");
            sb.AppendLine("          name: build-Android");
            sb.AppendLine("          path: build/Android");
            sb.AppendLine();
            sb.AppendLine("      - name: Upload to Google Play");
            sb.AppendLine("        uses: r0adkll/upload-google-play@v1");
            sb.AppendLine("        with:");
            sb.AppendLine("          serviceAccountJsonPlainText: ${{ secrets.GOOGLE_PLAY_SERVICE_ACCOUNT_JSON }}");
            sb.AppendLine("          packageName: ${{ secrets.ANDROID_PACKAGE_NAME }}");
            sb.AppendLine("          releaseFiles: build/Android/*.aab");
            sb.AppendLine("          track: internal");
            sb.AppendLine();
        }

        static void AppendAppStoreJob(StringBuilder sb)
        {
            sb.AppendLine("  deploy-app-store:");
            sb.AppendLine("    name: Deploy to App Store (TestFlight)");
            sb.AppendLine("    needs: build");
            sb.AppendLine("    runs-on: macos-latest");
            sb.AppendLine("    if: contains(github.ref, 'v')");
            sb.AppendLine("    steps:");
            sb.AppendLine("      - name: Download iOS artifact");
            sb.AppendLine("        uses: actions/download-artifact@v4");
            sb.AppendLine("        with:");
            sb.AppendLine("          name: build-iOS");
            sb.AppendLine("          path: build/iOS");
            sb.AppendLine();
            sb.AppendLine("      - name: Install Fastlane");
            sb.AppendLine("        run: gem install fastlane");
            sb.AppendLine();
            sb.AppendLine("      - name: Build and upload to TestFlight");
            sb.AppendLine("        env:");
            sb.AppendLine("          APP_STORE_CONNECT_API_KEY: ${{ secrets.APP_STORE_CONNECT_API_KEY }}");
            sb.AppendLine("          APP_STORE_CONNECT_KEY_ID: ${{ secrets.APP_STORE_CONNECT_KEY_ID }}");
            sb.AppendLine("          APP_STORE_CONNECT_ISSUER_ID: ${{ secrets.APP_STORE_CONNECT_ISSUER_ID }}");
            sb.AppendLine("        run: |");
            sb.AppendLine("          cd build/iOS");
            sb.AppendLine("          xcodebuild -project Unity-iPhone.xcodeproj -scheme Unity-iPhone -archivePath Unity-iPhone.xcarchive archive");
            sb.AppendLine("          xcodebuild -exportArchive -archivePath Unity-iPhone.xcarchive -exportPath export -exportOptionsPlist ExportOptions.plist");
            sb.AppendLine("          xcrun altool --upload-app -f export/*.ipa --apiKey $APP_STORE_CONNECT_KEY_ID --apiIssuer $APP_STORE_CONNECT_ISSUER_ID");
            sb.AppendLine();
        }

        static void AppendSteamJob(StringBuilder sb, BuildAutomationSettings settings)
        {
            sb.AppendLine("  deploy-steam:");
            sb.AppendLine("    name: Deploy to Steam");
            sb.AppendLine("    needs: build");
            sb.AppendLine("    runs-on: ubuntu-latest");
            sb.AppendLine("    if: contains(github.ref, 'v')");
            sb.AppendLine("    steps:");
            sb.AppendLine("      - name: Download Windows artifact");
            sb.AppendLine("        uses: actions/download-artifact@v4");
            sb.AppendLine("        with:");
            sb.AppendLine("          name: build-StandaloneWindows64");
            sb.AppendLine("          path: build/StandaloneWindows64");
            sb.AppendLine();
            sb.AppendLine("      - name: Deploy to Steam");
            sb.AppendLine("        uses: game-ci/steam-deploy@v3");
            sb.AppendLine("        with:");
            sb.AppendLine("          username: ${{ secrets.STEAM_USERNAME }}");
            sb.AppendLine("          configVdf: ${{ secrets.STEAM_CONFIG_VDF }}");

            string appId = !string.IsNullOrEmpty(settings.steamAppId)
                ? settings.steamAppId : "${{ secrets.STEAM_APP_ID }}";
            sb.AppendLine($"          appId: {appId}");
            sb.AppendLine("          buildDescription: v${{ needs.build.outputs.version }}");
            sb.AppendLine("          rootPath: build");
            sb.AppendLine("          depot1Path: StandaloneWindows64");
            sb.AppendLine("          releaseBranch: prerelease");
            sb.AppendLine();
        }

        static void AppendNotifyJob(StringBuilder sb, BuildAutomationSettings settings)
        {
            // 알림이 의존하는 job 목록 (모든 배포 job 포함)
            var needs = new List<string> { "build" };
            if (settings.deployGitHubReleases) needs.Add("release");
            if (settings.deployGooglePlay) needs.Add("deploy-google-play");
            if (settings.deployAppStore) needs.Add("deploy-app-store");
            if (settings.deploySteam) needs.Add("deploy-steam");

            sb.AppendLine("  notify:");
            sb.AppendLine("    name: Send Notification");
            sb.AppendLine($"    needs: [{string.Join(", ", needs)}]");
            sb.AppendLine("    runs-on: ubuntu-latest");
            sb.AppendLine("    if: startsWith(github.ref, 'refs/tags/v') && always()");
            sb.AppendLine("    steps:");

            string secretName = settings.notifyChannel switch
            {
                NotifyChannel.Discord => "DISCORD_WEBHOOK",
                NotifyChannel.Slack => "SLACK_WEBHOOK",
                _ => "CUSTOM_WEBHOOK"
            };

            // env로 시크릿을 전달하여 if 조건에서 안전하게 확인
            if (settings.notifyChannel == NotifyChannel.Discord)
            {
                sb.AppendLine("      - name: Notify Discord");
                sb.AppendLine("        env:");
                sb.AppendLine("          WEBHOOK_URL: ${{ secrets.DISCORD_WEBHOOK }}");
                sb.AppendLine("        run: |");
                sb.AppendLine("          if [ -z \"${WEBHOOK_URL}\" ]; then echo \"No webhook configured\"; exit 0; fi");
                sb.AppendLine("          STATUS=\"${{ needs.build.result == 'success' && 'SUCCESS' || 'FAILED' }}\"");
                sb.AppendLine("          COLOR=\"${{ needs.build.result == 'success' && '3066993' || '15158332' }}\"");
                sb.AppendLine("          VERSION=\"${{ needs.build.outputs.version }}\"");
                sb.AppendLine("          REPO=\"${{ github.repository }}\"");
                sb.AppendLine("          RELEASE_URL=\"https://github.com/${REPO}/releases/tag/${GITHUB_REF_NAME}\"");
                sb.AppendLine("          curl -s -X POST \"${WEBHOOK_URL}\" \\");
                sb.AppendLine("            -H \"Content-Type: application/json\" \\");
                sb.AppendLine("            -d \"{");
                sb.AppendLine("              \\\"embeds\\\": [{");
                sb.AppendLine("                \\\"title\\\": \\\"${STATUS}: Build v${VERSION}\\\",");
                sb.AppendLine("                \\\"color\\\": ${COLOR},");
                sb.AppendLine("                \\\"fields\\\": [");
                sb.AppendLine("                  {\\\"name\\\": \\\"Version\\\", \\\"value\\\": \\\"v${VERSION}\\\", \\\"inline\\\": true},");
                sb.AppendLine("                  {\\\"name\\\": \\\"Download\\\", \\\"value\\\": \\\"[GitHub Release](${RELEASE_URL})\\\", \\\"inline\\\": true}");
                sb.AppendLine("                ]");
                sb.AppendLine("              }]");
                sb.AppendLine("            }\"");
            }
            else if (settings.notifyChannel == NotifyChannel.Slack)
            {
                sb.AppendLine("      - name: Notify Slack");
                sb.AppendLine("        env:");
                sb.AppendLine("          WEBHOOK_URL: ${{ secrets.SLACK_WEBHOOK }}");
                sb.AppendLine("        run: |");
                sb.AppendLine("          if [ -z \"${WEBHOOK_URL}\" ]; then echo \"No webhook configured\"; exit 0; fi");
                sb.AppendLine("          STATUS=\"${{ needs.build.result == 'success' && 'SUCCESS' || 'FAILED' }}\"");
                sb.AppendLine("          VERSION=\"${{ needs.build.outputs.version }}\"");
                sb.AppendLine("          REPO=\"${{ github.repository }}\"");
                sb.AppendLine("          RELEASE_URL=\"https://github.com/${REPO}/releases/tag/${GITHUB_REF_NAME}\"");
                sb.AppendLine("          curl -s -X POST \"${WEBHOOK_URL}\" \\");
                sb.AppendLine("            -H \"Content-Type: application/json\" \\");
                sb.AppendLine("            -d \"{\\\"text\\\": \\\"${STATUS}: Build v${VERSION}\\\\nDownload: ${RELEASE_URL}\\\"}\"");
            }
            else
            {
                sb.AppendLine("      - name: Custom Webhook");
                sb.AppendLine("        env:");
                sb.AppendLine($"          WEBHOOK_URL: ${{{{ secrets.{secretName} }}}}");
                sb.AppendLine("        run: |");
                sb.AppendLine("          if [ -z \"${WEBHOOK_URL}\" ]; then echo \"No webhook configured\"; exit 0; fi");
                sb.AppendLine("          VERSION=\"${{ needs.build.outputs.version }}\"");
                sb.AppendLine("          REPO=\"${{ github.repository }}\"");
                sb.AppendLine("          RELEASE_URL=\"https://github.com/${REPO}/releases/tag/${GITHUB_REF_NAME}\"");
                sb.AppendLine("          curl -s -X POST \"${WEBHOOK_URL}\" \\");
                sb.AppendLine("            -H \"Content-Type: application/json\" \\");
                sb.AppendLine("            -d \"{");
                sb.AppendLine("              \\\"version\\\": \\\"v${VERSION}\\\",");
                sb.AppendLine("              \\\"status\\\": \\\"${{ needs.build.result }}\\\",");
                sb.AppendLine("              \\\"download_url\\\": \\\"${RELEASE_URL}\\\"");
                sb.AppendLine("            }\"");
            }
            sb.AppendLine();
        }

        // ── 헬퍼 ──

        static List<string> GetPlatformList(BuildAutomationSettings settings)
        {
            var list = new List<string>();
            if (settings.enableAndroid) list.Add("Android");
            if (settings.enableIOS) list.Add("iOS");
            if (settings.enableWindows) list.Add("StandaloneWindows64");
            if (settings.enableWebGL) list.Add("WebGL");
            return list;
        }

        /// <summary>플랫폼별 아티팩트 업로드 경로 (Android는 APK/AAB만, 나머지는 폴더 전체)</summary>
        static string GetArtifactPath(string platform, BuildAutomationSettings settings)
        {
            if (platform == "Android")
            {
                string ext = settings.androidBuildFormat == AndroidBuildFormat.AAB ? "*.aab" : "*.apk";
                return $"build/Android/{ext}";
            }
            return $"build/{platform}";
        }
    }
}
#endif
