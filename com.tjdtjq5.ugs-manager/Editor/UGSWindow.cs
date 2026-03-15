#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.UGSManager
{
    /// <summary>
    /// UGS Manager EditorWindow. CLI 래핑으로 Dashboard 없이 UGS 서비스 관리.
    /// </summary>
    public class UGSWindow : EditorWindow
    {
        static readonly Color BG_WINDOW = new(0.15f, 0.15f, 0.19f);
        static readonly Color BG_HEADER = new(0.11f, 0.11f, 0.14f);
        static readonly Color BG_STATUS = new(0.10f, 0.10f, 0.13f);

        static readonly Color COL_OK    = new(0.30f, 0.80f, 0.40f);
        static readonly Color COL_WARN  = new(0.95f, 0.75f, 0.20f);
        static readonly Color COL_MUTED = new(0.45f, 0.45f, 0.50f);

        UGSTabBase[] _tabs;
        int _currentTabIndex;
        Vector2 _scroll;

        // 상태바 캐싱
        string _environment;
        string _cliVersion;
        string _projectId;
        bool _isLoggedIn;
        bool _cliInstalled;
        bool _statusLoaded;

        // 설정
        bool _showSettings;
        string _editOrgId;
        string _editCodePath;
        string _editDeployPath;

        [MenuItem("Tools/UGS Manager %#u")]
        static void Open()
        {
            var wnd = GetWindow<UGSWindow>();
            wnd.titleContent = new GUIContent("UGS Manager");
            wnd.minSize = new Vector2(500, 400);
        }

        void OnEnable()
        {
            _tabs = new UGSTabBase[]
            {
                new EnvironmentTab(),
                new RemoteConfigTab(),
                new CloudCodeTab(),
                new EconomyTab(),
                new LeaderboardsTab(),
                new PlayerDataTab(),
                new CustomDataTab(),
                new DeployTab(),
            };

            RefreshStatus();

            // 초기 설정 안내
            if (!UGSConfig.IsConfigured)
                _showSettings = true;

            foreach (var tab in _tabs) tab.OnEnable();
        }

        void OnDisable()
        {
            if (_tabs != null)
                foreach (var tab in _tabs) tab.OnDisable();
        }

        void OnGUI()
        {
            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), BG_WINDOW);

            // CLI 미설치 경고
            if (_statusLoaded && !_cliInstalled)
            {
                DrawCliWarning();
                return;
            }

            // 설정 화면
            if (_showSettings)
            {
                DrawSettings();
                return;
            }

            // 탭 바
            DrawTabBar();
            GUILayout.Space(4);

            // 콘텐츠
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            if (_tabs != null && _currentTabIndex >= 0 && _currentTabIndex < _tabs.Length)
                _tabs[_currentTabIndex].OnDraw();

            GUILayout.Space(10);
            EditorGUILayout.EndScrollView();

            // 상태바
            DrawStatusBar();
        }

        // ─── 탭 바 ──────────────────────────────────────

        void DrawTabBar()
        {
            if (_tabs == null || _tabs.Length == 0) return;

            var rect = GUILayoutUtility.GetRect(0, 36, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, BG_HEADER);

            // 탭 영역 (설정 버튼 제외)
            float settingsBtnW = 26;
            float tabAreaW = rect.width - settingsBtnW;
            float tabWidth = tabAreaW / _tabs.Length;

            for (int i = 0; i < _tabs.Length; i++)
            {
                var tabRect = new Rect(rect.x + tabWidth * i, rect.y, tabWidth, rect.height);
                bool isActive = _currentTabIndex == i;
                Color color = _tabs[i].TabColor;

                if (isActive)
                {
                    EditorGUI.DrawRect(tabRect, new Color(color.r, color.g, color.b, 0.12f));
                    EditorGUI.DrawRect(new Rect(tabRect.x, tabRect.yMax - 3, tabRect.width, 3), color);
                }

                var style = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 13,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = isActive ? color : COL_MUTED }
                };
                EditorGUI.LabelField(tabRect, _tabs[i].TabName, style);

                if (Event.current.type == EventType.MouseDown && tabRect.Contains(Event.current.mousePosition))
                {
                    _currentTabIndex = i;
                    _scroll = Vector2.zero;
                    Event.current.Use();
                }
            }

            // 설정 버튼
            var settingsRect = new Rect(rect.x + tabAreaW, rect.y + 6, settingsBtnW, rect.height - 12);
            if (GUI.Button(settingsRect, "⚙", new GUIStyle(EditorStyles.miniButton)
                { fontSize = 14, alignment = TextAnchor.MiddleCenter }))
                _showSettings = true;
        }

        // ─── 설정 화면 ─────────────────────────────────

        void DrawSettings()
        {
            GUILayout.Space(20);

            var titleStyle = new GUIStyle(EditorStyles.boldLabel)
                { fontSize = 16, alignment = TextAnchor.MiddleCenter };
            EditorGUILayout.LabelField("UGS Manager 설정", titleStyle);

            GUILayout.Space(16);

            // 초기값 로드
            _editOrgId ??= UGSConfig.OrganizationId;
            _editCodePath ??= UGSConfig.CloudCodePath;
            _editDeployPath ??= UGSConfig.DeployPath;

            var hintStyle = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = COL_MUTED }, wordWrap = true };

            // ─── 조직 ID ────────────────────────────
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.LabelField("1. 조직 ID", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Dashboard 링크에 사용됩니다. 아래 버튼으로 Dashboard를 열고 URL에서 숫자를 복사하세요.", hintStyle);

            GUILayout.Space(4);

            // 안내 이미지 (텍스트)
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("cloud.unity.com/home/organizations/[숫자ID]/projects/...",
                new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = COL_WARN }, alignment = TextAnchor.MiddleCenter });
            EditorGUILayout.LabelField("↑ 이 숫자를 아래에 붙여넣기",
                new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = COL_MUTED }, alignment = TextAnchor.MiddleCenter });
            EditorGUILayout.EndVertical();

            GUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Dashboard 열기", GUILayout.Height(22)))
                Application.OpenURL("https://cloud.unity.com");
            GUILayout.Space(8);
            _editOrgId = EditorGUILayout.TextField(_editOrgId);
            EditorGUILayout.EndHorizontal();

            if (string.IsNullOrEmpty(_editOrgId))
                EditorGUILayout.LabelField("⚠ 조직 ID가 비어있으면 Dashboard 링크가 작동하지 않습니다. (나머지 기능은 정상)", hintStyle);

            EditorGUILayout.EndVertical();

            GUILayout.Space(8);

            // ─── 경로 설정 ──────────────────────────
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.LabelField("2. 경로 설정", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("UGS 설정 파일(.rc, .js 등)이 있는 폴더를 지정합니다.", hintStyle);

            GUILayout.Space(4);

            // Deploy 경로
            EditorGUILayout.LabelField("Deploy 경로 (RemoteConfig.rc, CloudCode/ 등이 있는 폴더)", hintStyle);
            EditorGUILayout.BeginHorizontal();
            _editDeployPath = EditorGUILayout.TextField(_editDeployPath);
            if (GUILayout.Button("...", GUILayout.Width(28)))
            {
                string selected = EditorUtility.OpenFolderPanel("Deploy 폴더", "Assets", "");
                if (!string.IsNullOrEmpty(selected))
                {
                    int ai = selected.IndexOf("Assets", System.StringComparison.Ordinal);
                    _editDeployPath = ai >= 0 ? selected[ai..] : selected;
                }
            }
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(4);

            // Cloud Code 경로
            EditorGUILayout.LabelField("Cloud Code 경로 (.js 스크립트가 있는 폴더)", hintStyle);
            EditorGUILayout.BeginHorizontal();
            _editCodePath = EditorGUILayout.TextField(_editCodePath);
            if (GUILayout.Button("...", GUILayout.Width(28)))
            {
                string selected = EditorUtility.OpenFolderPanel("Cloud Code 폴더", "Assets", "");
                if (!string.IsNullOrEmpty(selected))
                {
                    int ai = selected.IndexOf("Assets", System.StringComparison.Ordinal);
                    _editCodePath = ai >= 0 ? selected[ai..] : selected;
                }
            }
            EditorGUILayout.EndHorizontal();

            // 자동 감지 버튼
            GUILayout.Space(4);
            if (GUILayout.Button("경로 자동 감지 (.rc 파일 위치 기반)", GUILayout.Height(20)))
                AutoDetectPaths();

            EditorGUILayout.EndVertical();

            GUILayout.Space(8);

            // ─── CLI 상태 (캐싱된 값 사용) ────────────
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("3. CLI 상태", EditorStyles.boldLabel);

            EditorGUILayout.LabelField($"CLI 버전: {_cliVersion ?? "N/A"}", hintStyle);
            EditorGUILayout.LabelField($"Project ID: {(_projectId ?? "(미설정)")}", hintStyle);
            EditorGUILayout.LabelField($"Environment: {_environment ?? "(미설정)"}", hintStyle);
            EditorGUILayout.EndVertical();

            GUILayout.Space(16);

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("저장", GUILayout.Width(80), GUILayout.Height(28)))
            {
                UGSConfig.OrganizationId = _editOrgId?.Trim() ?? "";
                UGSConfig.CloudCodePath = _editCodePath?.Trim() ?? "";
                UGSConfig.DeployPath = _editDeployPath?.Trim() ?? "";
                _showSettings = false;
            }

            if (GUILayout.Button("취소", GUILayout.Width(80), GUILayout.Height(28)))
            {
                _editOrgId = null;
                _editCodePath = null;
                _editDeployPath = null;
                _showSettings = false;
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(8);

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("설정 초기화", GUILayout.Width(100)))
            {
                if (EditorUtility.DisplayDialog("설정 초기화", "모든 UGS Manager 설정을 초기화하시겠습니까?", "초기화", "취소"))
                {
                    UGSConfig.Reset();
                    _editOrgId = null;
                    _editCodePath = null;
                    _editDeployPath = null;
                }
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        // ─── 상태바 ─────────────────────────────────────

        void DrawStatusBar()
        {
            var rect = GUILayoutUtility.GetRect(0, 24, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, BG_STATUS);

            if (!_statusLoaded) return;

            float x = rect.x + 8;
            float y = rect.y;
            float h = rect.height;

            var style = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = COL_MUTED },
                alignment = TextAnchor.MiddleLeft
            };

            // 환경
            var envStyle = new GUIStyle(style) { normal = { textColor = COL_OK } };
            EditorGUI.LabelField(new Rect(x, y, 12, h), "●", envStyle);
            x += 12;
            EditorGUI.LabelField(new Rect(x, y, 100, h), _environment ?? "unknown", envStyle);
            x += 100;

            EditorGUI.LabelField(new Rect(x, y, 10, h), "|", style);
            x += 14;

            EditorGUI.LabelField(new Rect(x, y, 80, h), $"CLI: {_cliVersion ?? "N/A"}", style);
            x += 84;

            EditorGUI.LabelField(new Rect(x, y, 10, h), "|", style);
            x += 14;

            var loginColor = _isLoggedIn ? COL_OK : COL_WARN;
            var loginStyle = new GUIStyle(style) { normal = { textColor = loginColor } };
            EditorGUI.LabelField(new Rect(x, y, 100, h), _isLoggedIn ? "Logged in" : "Not logged in", loginStyle);
        }

        // ─── CLI 미설치 경고 ─────────────────────────────

        void DrawCliWarning()
        {
            GUILayout.Space(40);

            EditorGUILayout.LabelField("UGS CLI가 설치되지 않았습니다", new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 16, alignment = TextAnchor.MiddleCenter,
                normal = { textColor = COL_WARN }
            });

            GUILayout.Space(12);

            EditorGUILayout.LabelField("터미널에서 다음 명령으로 설치하세요:",
                new GUIStyle(EditorStyles.wordWrappedLabel)
                { alignment = TextAnchor.MiddleCenter, normal = { textColor = COL_MUTED } });

            GUILayout.Space(8);

            EditorGUILayout.LabelField("npm install -g ugs", new GUIStyle(EditorStyles.boldLabel)
            { fontSize = 14, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } });

            GUILayout.Space(16);

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("설치 확인 (Retry)", GUILayout.Width(140), GUILayout.Height(28)))
                RefreshStatus();
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        // ─── 상태 갱신 ──────────────────────────────────

        void AutoDetectPaths()
        {
            // .rc 파일 찾기 (Assets 하위만 탐색)
            var rcFiles = System.IO.Directory.GetFiles(Application.dataPath, "*.rc", System.IO.SearchOption.AllDirectories);
            foreach (var f in rcFiles)
            {
                string content = System.IO.File.ReadAllText(f);
                if (!content.Contains("entries")) continue;

                string dir = System.IO.Path.GetDirectoryName(f);
                int ai = dir?.IndexOf("Assets", System.StringComparison.Ordinal) ?? -1;
                if (ai >= 0)
                {
                    _editDeployPath = dir[ai..].Replace('\\', '/');

                    // CloudCode 폴더 탐색 (같은 폴더 또는 하위)
                    string codePath = System.IO.Path.Combine(dir, "CloudCode");
                    if (System.IO.Directory.Exists(codePath))
                        _editCodePath = codePath[ai..].Replace('\\', '/');
                    else
                        _editCodePath = _editDeployPath;
                }
                break;
            }
        }

        void RefreshStatus()
        {
            _cliInstalled = UGSCliRunner.IsInstalled();
            if (_cliInstalled)
            {
                _cliVersion = UGSCliRunner.GetCliVersion();
                _projectId = UGSCliRunner.GetProjectId();
                _environment = UGSCliRunner.GetEnvironment();
                _isLoggedIn = UGSCliRunner.IsLoggedIn();
            }
            _statusLoaded = true;
        }

        /// <summary>환경 변경 시 상태바 갱신 (탭에서 호출)</summary>
        public void OnEnvironmentChanged()
        {
            _environment = UGSCliRunner.GetEnvironment();
            Repaint();
        }
    }
}
#endif
