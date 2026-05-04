#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;

namespace Tjdtjq5.Codemagic.Editor.Codemagic
{
    /// <summary>
    /// BuildYamlOptions 입력값을 받아서 Codemagic codemagic.yaml 텍스트를 조립한다.
    /// 출력은 SurvivorsDuo의 검증된 yaml과 구조 동일 — 옵션값으로 변수화된 부분만 다름.
    /// </summary>
    [Serializable]
    public sealed class BuildYamlOptions
    {
        // 빌드 타겟
        public bool BuildAndroid = true;
        public bool BuildIOS = false;

        // 인스턴스
        public string InstanceType = "linux_x2";  // linux_x2 / linux_x4 / mac_mini / mac_pro
        public int MaxBuildDuration = 90;          // 분

        // 캐시 (Phase 1 — 사용자가 BuildDialog에서 토글)
        public bool ClearLibraryCache = false;
        public bool ClearGradleCache = false;
        public string CacheReason = "(no special reason)";  // log only

        // Unity
        public string UnityVersion = "6000.3.10f1";
        public string ImageTag = "ubuntu-6000.3.10f1-android-3";  // unityci/editor 태그
        public string BuilderVersion = "v4";       // GameCI unity-builder 브랜치

        // BUILD_METHOD
        public string BuildMethod = "Tjdtjq5.Codemagic.Editor.Build.CodemagicBuildScript.PerformAndroidBuild";
        public string BuildName = "App";           // 빌드 산출물 이름 (예: SurvivorsDuo)

        // 알림
        public List<string> NotificationRecipients = new();
        public bool NotifyOnSuccess = true;
        public bool NotifyOnFailure = false;

        // 트리거
        public string TagPattern = "v*";           // 어떤 git tag로 빌드 트리거할지
    }

    public sealed class CodemagicYamlGenerator
    {
        const int CacheReasonMaxLength = 200;

        public string Generate(BuildYamlOptions options)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));
            if (!options.BuildAndroid)
                throw new ArgumentException("Android 빌드 비활성화는 v0.1.0에서 지원하지 않습니다.", nameof(options));

            // TODO Phase 2+: BuildIOS 옵션은 받지만 v0.1.0에서는 yaml 생성에 영향 없음.

            var sb = new StringBuilder();
            AppendHeader(sb);
            AppendAndroidWorkflow(sb, options);
            AppendTestLicenseWorkflow(sb, options);

            // 출력 끝에 단일 newline 보장
            EnsureTrailingNewline(sb);
            return sb.ToString();
        }

        // ── android-build workflow ─────────────────────────────────────────

        static void AppendHeader(StringBuilder sb)
        {
            sb.AppendLine("# 자동 생성됨 — Editor에서 수정 X (BuildDialog에서 옵션 변경 후 재생성)");
            sb.AppendLine("workflows:");
        }

        void AppendAndroidWorkflow(StringBuilder sb, BuildYamlOptions o)
        {
            sb.AppendLine("  android-build:");
            sb.AppendLine("    name: Android Build");
            sb.AppendLine($"    instance_type: {o.InstanceType}");
            sb.AppendLine($"    max_build_duration: {o.MaxBuildDuration}");
            sb.AppendLine();

            // triggering
            sb.AppendLine("    triggering:");
            sb.AppendLine("      events:");
            sb.AppendLine("        - tag");
            sb.AppendLine("      tag_patterns:");
            sb.AppendLine($"        - pattern: '{o.TagPattern}'");
            sb.AppendLine("          include: true");
            sb.AppendLine();

            // environment
            sb.AppendLine("    environment:");
            sb.AppendLine("      groups:");
            sb.AppendLine("        - unity_credentials");
            sb.AppendLine("        - android_keystore");
            sb.AppendLine("      vars:");
            sb.AppendLine($"        UNITY_VERSION: {o.UnityVersion}");
            sb.AppendLine($"        IMAGE: unityci/editor:{o.ImageTag}");
            sb.AppendLine($"        BUILDER_VERSION: {o.BuilderVersion}");
            sb.AppendLine();

            // cache
            sb.AppendLine("    cache:");
            sb.AppendLine("      cache_paths:");
            sb.AppendLine("        - Library");
            sb.AppendLine("        - ~/.gradle");
            sb.AppendLine();

            // scripts
            sb.AppendLine("    scripts:");
            AppendCacheStep(sb, o);
            AppendVerifyEnvStep(sb, includeKeystore: true);
            AppendCloneBuilderStep(sb, listDist: true);
            AppendBuildAndroidStep(sb, o);

            // artifacts
            sb.AppendLine("    artifacts:");
            sb.AppendLine("      - build/Android/*.apk");
            sb.AppendLine("      - build/Android/*.aab");
            sb.AppendLine("      - build/unity-build-full.log");

            // publishing — 유효 수신자 0명이면 통째로 제외 (Codemagic이 빈 recipients를 거부할 수 있음)
            var validRecipients = new List<string>();
            if (o.NotificationRecipients != null)
            {
                foreach (var r in o.NotificationRecipients)
                {
                    if (!string.IsNullOrWhiteSpace(r))
                        validRecipients.Add(r.Trim());
                }
            }
            if (validRecipients.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("    publishing:");
                sb.AppendLine("      email:");
                sb.AppendLine("        recipients:");
                foreach (var recipient in validRecipients)
                    sb.AppendLine($"          - {recipient}");
                sb.AppendLine("        notify:");
                sb.AppendLine($"          success: {ToYamlBool(o.NotifyOnSuccess)}");
                sb.AppendLine($"          failure: {ToYamlBool(o.NotifyOnFailure)}");
            }

            sb.AppendLine();
        }

        // ── 캐시 자동 관리 step ────────────────────────────────────────────
        void AppendCacheStep(StringBuilder sb, BuildYamlOptions o)
        {
            var safeReason = EscapeShellString(o.CacheReason ?? string.Empty);
            sb.AppendLine("      - name: Auto cache management");
            sb.AppendLine("        script: |");
            sb.AppendLine("          echo \"===== 캐시 자동 관리 =====\"");
            sb.AppendLine($"          echo \"사유: {safeReason}\"");

            if (o.ClearLibraryCache)
                sb.AppendLine("          rm -rf Library && echo \"▶ Library 클린\"");
            if (o.ClearGradleCache)
                sb.AppendLine("          rm -rf ~/.gradle && echo \"▶ Gradle 클린\"");
            if (!o.ClearLibraryCache && !o.ClearGradleCache)
                sb.AppendLine("          echo \"변경 없음\"");

            sb.AppendLine("          echo \"===========================\"");
            sb.AppendLine();
        }

        // ── ENV CHECK step (android-build / test-license 공용) ────────────
        static void AppendVerifyEnvStep(StringBuilder sb, bool includeKeystore)
        {
            sb.AppendLine("      - name: Verify environment variables");
            sb.AppendLine("        script: |");
            sb.AppendLine("          echo \"===== ENV CHECK =====\"");
            sb.AppendLine("          echo \"UNITY_LICENSE length: ${#UNITY_LICENSE}\"");
            sb.AppendLine("          echo \"UNITY_EMAIL length: ${#UNITY_EMAIL}\"");
            sb.AppendLine("          echo \"UNITY_PASSWORD length: ${#UNITY_PASSWORD}\"");
            if (includeKeystore)
            {
                sb.AppendLine("          echo \"KEYSTORE_BASE64 length: ${#KEYSTORE_BASE64}\"");
                sb.AppendLine("          echo \"KEYSTORE_PASSWORD length: ${#KEYSTORE_PASSWORD}\"");
                sb.AppendLine("          echo \"KEY_ALIAS length: ${#KEY_ALIAS}\"");
                sb.AppendLine("          echo \"KEY_PASSWORD length: ${#KEY_PASSWORD}\"");
            }
            sb.AppendLine("          echo \"====================\"");
            sb.AppendLine();
        }

        // ── unity-builder clone step ──────────────────────────────────────
        static void AppendCloneBuilderStep(StringBuilder sb, bool listDist)
        {
            sb.AppendLine("      - name: Clone GameCI unity-builder");
            sb.AppendLine("        script: |");
            sb.AppendLine("          git clone --depth 1 --branch $BUILDER_VERSION \\");
            sb.AppendLine("            https://github.com/game-ci/unity-builder.git /tmp/unity-builder");
            if (listDist)
            {
                sb.AppendLine("          ls -la /tmp/unity-builder/dist/");
            }
            else
            {
                sb.AppendLine("          echo \"===== Steps directory =====\"");
                sb.AppendLine("          ls -la /tmp/unity-builder/dist/platforms/ubuntu/steps/");
                sb.AppendLine("          echo \"===========================\"");
            }
            sb.AppendLine();
        }

        // ── Build Android step ────────────────────────────────────────────
        static void AppendBuildAndroidStep(StringBuilder sb, BuildYamlOptions o)
        {
            sb.AppendLine("      - name: Build Android");
            sb.AppendLine("        script: |");
            sb.AppendLine("          docker pull $IMAGE");
            sb.AppendLine("          BUILDER=/tmp/unity-builder/dist");
            sb.AppendLine();
            sb.AppendLine("          # .ulf에서 시리얼 추출 (GameCI v4 알고리즘)");
            sb.AppendLine("          echo \"$UNITY_LICENSE\" > /tmp/unity_license.ulf");
            sb.AppendLine("          DEVDATA=$(grep -o '<DeveloperData Value=\"[^\"]*\"' /tmp/unity_license.ulf | sed 's/<DeveloperData Value=\"//; s/\"$//')");
            sb.AppendLine("          UNITY_SERIAL=$(echo \"$DEVDATA\" | base64 -d 2>/dev/null | tail -c +5 | tr -d '\\0')");
            sb.AppendLine("          export UNITY_SERIAL");
            sb.AppendLine("          echo \"Extracted UNITY_SERIAL length: ${#UNITY_SERIAL}\"");
            sb.AppendLine("          echo \"Serial prefix: ${UNITY_SERIAL:0:3}-...\"");
            sb.AppendLine();
            sb.AppendLine("          if [ -z \"$UNITY_SERIAL\" ]; then");
            sb.AppendLine("            echo \"::error::Failed to extract UNITY_SERIAL from .ulf file\"");
            sb.AppendLine("            exit 1");
            sb.AppendLine("          fi");
            sb.AppendLine();
            sb.AppendLine("          mkdir -p build");
            sb.AppendLine("          set -o pipefail");
            sb.AppendLine("          docker run --rm \\");
            sb.AppendLine("            -v \"$PWD:/github/workspace\" \\");
            sb.AppendLine("            -v $BUILDER/default-build-script:/UnityBuilderAction \\");
            sb.AppendLine("            -v $BUILDER/platforms/ubuntu/steps:/steps \\");
            sb.AppendLine("            -v $BUILDER/platforms/ubuntu/entrypoint.sh:/entrypoint.sh \\");
            sb.AppendLine("            -v $BUILDER/unity-config:/usr/share/unity3d/config/ \\");
            sb.AppendLine("            -v $BUILDER/BlankProject:/BlankProject \\");
            sb.AppendLine("            -w /github/workspace \\");
            sb.AppendLine("            -e UNITY_EMAIL \\");
            sb.AppendLine("            -e UNITY_PASSWORD \\");
            sb.AppendLine("            -e UNITY_SERIAL \\");
            sb.AppendLine("            -e UNITY_VERSION \\");
            sb.AppendLine("            -e PROJECT_PATH=. \\");
            sb.AppendLine($"            -e BUILD_METHOD={o.BuildMethod} \\");
            sb.AppendLine("            -e BUILD_TARGET=Android \\");
            sb.AppendLine($"            -e BUILD_NAME={o.BuildName} \\");
            sb.AppendLine("            -e BUILD_PATH=build/Android \\");
            sb.AppendLine($"            -e BUILD_FILE={o.BuildName}.apk \\");
            sb.AppendLine("            -e VERSION=\"${CM_TAG#v}\" \\");
            sb.AppendLine("            -e ANDROID_VERSION_CODE=\"${CM_BUILD_NUMBER:-1}\" \\");
            sb.AppendLine("            -e ANDROID_KEYSTORE_BASE64=\"$KEYSTORE_BASE64\" \\");
            sb.AppendLine("            -e ANDROID_KEYSTORE_PASS=\"$KEYSTORE_PASSWORD\" \\");
            sb.AppendLine("            -e ANDROID_KEYALIAS_NAME=\"$KEY_ALIAS\" \\");
            sb.AppendLine("            -e ANDROID_KEYALIAS_PASS=\"$KEY_PASSWORD\" \\");
            sb.AppendLine("            -e ANDROID_KEYSTORE_NAME=user.keystore \\");
            sb.AppendLine("            -e ANDROID_EXPORT_TYPE=androidPackage \\");
            sb.AppendLine("            -e ANDROID_SYMBOL_TYPE=none \\");
            sb.AppendLine("            -e SKIP_ACTIVATION=false \\");
            sb.AppendLine("            -e MANUAL_EXIT=false \\");
            sb.AppendLine("            -e ENABLE_GPU=false \\");
            sb.AppendLine("            -e RUN_AS_HOST_USER=false \\");
            sb.AppendLine("            $IMAGE \\");
            sb.AppendLine("            /entrypoint.sh 2>&1 | tee build/unity-build-full.log | { grep -v -E \"^chmod:.*proc|chown:.*proc\" || true; }");
            sb.AppendLine("          DOCKER_EXIT=${PIPESTATUS[0]}");
            sb.AppendLine();
            sb.AppendLine("          if [ \"$DOCKER_EXIT\" != \"0\" ]; then");
            sb.AppendLine("            echo \"\"");
            sb.AppendLine("            echo \"###########################\"");
            sb.AppendLine("            echo \"# 핵심 에러 추출 (실패 케이스)  #\"");
            sb.AppendLine("            echo \"###########################\"");
            sb.AppendLine("            grep -nE \"error CS|error:|Build [Ff]ailed|Killed|Aborted|Exception|BUILD FAILED|Cannot find|Could not load|Burst error|IL2CPP error\" build/unity-build-full.log | head -30 || echo \"키워드 매칭 없음 — 전체 로그를 artifact에서 확인하세요.\"");
            sb.AppendLine("            exit $DOCKER_EXIT");
            sb.AppendLine("          fi");
            sb.AppendLine();
        }

        // ── test-license workflow ──────────────────────────────────────────
        void AppendTestLicenseWorkflow(StringBuilder sb, BuildYamlOptions o)
        {
            sb.AppendLine("  test-license:");
            sb.AppendLine("    name: License Activation Test");
            sb.AppendLine("    instance_type: linux_x2");
            sb.AppendLine("    max_build_duration: 15");
            sb.AppendLine();

            sb.AppendLine("    environment:");
            sb.AppendLine("      groups:");
            sb.AppendLine("        - unity_credentials");
            sb.AppendLine("      vars:");
            sb.AppendLine($"        UNITY_VERSION: {o.UnityVersion}");
            sb.AppendLine($"        IMAGE: unityci/editor:{o.ImageTag}");
            sb.AppendLine($"        BUILDER_VERSION: {o.BuilderVersion}");
            sb.AppendLine();

            sb.AppendLine("    scripts:");
            AppendVerifyEnvStep(sb, includeKeystore: false);
            AppendCloneBuilderStep(sb, listDist: false);
            AppendTestLicenseStep(sb);
        }

        static void AppendTestLicenseStep(StringBuilder sb)
        {
            sb.AppendLine("      - name: Test license activation only");
            sb.AppendLine("        script: |");
            sb.AppendLine("          docker pull $IMAGE");
            sb.AppendLine("          BUILDER=/tmp/unity-builder/dist");
            sb.AppendLine();
            sb.AppendLine("          echo \"$UNITY_LICENSE\" > /tmp/unity_license.ulf");
            sb.AppendLine("          DEVDATA=$(grep -o '<DeveloperData Value=\"[^\"]*\"' /tmp/unity_license.ulf | sed 's/<DeveloperData Value=\"//; s/\"$//')");
            sb.AppendLine("          UNITY_SERIAL=$(echo \"$DEVDATA\" | base64 -d 2>/dev/null | tail -c +5 | tr -d '\\0')");
            sb.AppendLine("          export UNITY_SERIAL");
            sb.AppendLine("          echo \"Extracted UNITY_SERIAL length: ${#UNITY_SERIAL}\"");
            sb.AppendLine("          echo \"Serial prefix: ${UNITY_SERIAL:0:3}-...\"");
            sb.AppendLine();
            sb.AppendLine("          if [ -z \"$UNITY_SERIAL\" ]; then");
            sb.AppendLine("            echo \"::error::Failed to extract UNITY_SERIAL from .ulf\"");
            sb.AppendLine("            exit 1");
            sb.AppendLine("          fi");
            sb.AppendLine();
            sb.AppendLine("          docker run --rm \\");
            sb.AppendLine("            -v $BUILDER/platforms/ubuntu/steps:/steps \\");
            sb.AppendLine("            -v $BUILDER/unity-config:/usr/share/unity3d/config/ \\");
            sb.AppendLine("            -e UNITY_EMAIL \\");
            sb.AppendLine("            -e UNITY_PASSWORD \\");
            sb.AppendLine("            -e UNITY_SERIAL \\");
            sb.AppendLine("            -e UNITY_VERSION \\");
            sb.AppendLine("            -e SKIP_ACTIVATION=false \\");
            sb.AppendLine("            $IMAGE \\");
            sb.AppendLine("            bash /steps/activate.sh");
            sb.AppendLine();
            sb.AppendLine("          echo \"===== Activation completed =====\"");
        }

        // ── helpers ────────────────────────────────────────────────────────

        /// <summary>
        /// CacheReason 같은 사용자 입력 문자열을 yaml double-quote 안에 넣어도 안전하게.
        /// 큰따옴표/백슬래시/달러/백틱/줄바꿈 처리, 길이 cap.
        /// </summary>
        static string EscapeShellString(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            // 줄바꿈은 공백으로 단순화
            var collapsed = input.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');

            var sb = new StringBuilder(collapsed.Length);
            foreach (var c in collapsed)
            {
                switch (c)
                {
                    case '\\':
                        sb.Append("\\\\");
                        break;
                    case '"':
                        sb.Append("\\\"");
                        break;
                    case '$':
                        sb.Append("\\$");
                        break;
                    case '`':
                        sb.Append("\\`");
                        break;
                    default:
                        sb.Append(c);
                        break;
                }
            }

            // 길이 cap
            if (sb.Length > CacheReasonMaxLength)
                sb.Length = CacheReasonMaxLength;

            return sb.ToString();
        }

        static string ToYamlBool(bool value) => value ? "true" : "false";

        static void EnsureTrailingNewline(StringBuilder sb)
        {
            if (sb.Length == 0 || sb[sb.Length - 1] != '\n')
                sb.Append('\n');
        }
    }
}
#endif
