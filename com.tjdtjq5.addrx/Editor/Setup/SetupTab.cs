#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using Tjdtjq5.EditorToolkit.Editor;

namespace Tjdtjq5.AddrX.Editor
{
    /// <summary>Setup 탭. 스텝 위자드(초기 설정) + 대시보드(일상 사용).</summary>
    public class SetupTab : EditorTabBase
    {
        static readonly string[] StepLabels = { "Package", "Addressables", "Folders", "AddrX" };
        static readonly Color COL_LOCAL = new(0.4f, 0.7f, 0.95f);
        static readonly Color COL_REMOTE = new(0.95f, 0.6f, 0.3f);

        static GUIStyle _boldStyle;
        static GUIStyle BoldStyle => _boldStyle ??= new GUIStyle(EditorStyles.label) { fontStyle = FontStyle.Bold };

        readonly Action _repaint;
        Vector2 _scroll;
        new string _notification;
        new EditorUI.NotificationType _notificationType;

        // Step 3 — 그룹 편집
        int _editGroupIdx = -1;
        string _editBuffer = "";

        // Dashboard
        int _registeredCount;
        int _unregisteredCount;
        List<(string address, List<string> paths)> _conflicts = new();
        bool _showConflicts;
        bool _showGroups;
        bool _showLabels;

        public SetupTab(Action repaint) => _repaint = repaint;

        public override string TabName => "Setup";
        public override Color TabColor => new(0.4f, 0.8f, 0.6f);

        public override void OnEnable()
        {
            if (IsAllComplete())
                UpdateDashboardCounts();
        }

        // ═══════════════════════════════════════════════════════
        // ─── 스텝 판별
        // ═══════════════════════════════════════════════════════

        int GetCurrentStep()
        {
            if (AddressableAssetSettingsDefaultObject.Settings == null) return 1;
            var rules = AddrXSetupRules.Instance;
            if (rules == null || !AssetDatabase.IsValidFolder(rules.RootPath)) return 2;
            if (AssetDatabase.LoadAssetAtPath<AddrXSettings>(
                    "Assets/AddrX/Resources/AddrXSettings.asset") == null) return 3;
            return -1;
        }

        bool IsAllComplete() => GetCurrentStep() == -1;

        int[] GetStepStates()
        {
            int current = GetCurrentStep();
            var states = new int[4];
            states[0] = 1;
            for (int i = 1; i < 4; i++)
            {
                if (current == -1 || i < current) states[i] = 1;
                else if (i == current) states[i] = 2;
                else states[i] = 0;
            }
            return states;
        }

        // ═══════════════════════════════════════════════════════
        // ─── 메인 Draw
        // ═══════════════════════════════════════════════════════

        public override void OnDraw()
        {
            EditorUI.DrawNotificationBar(ref _notification, _notificationType);

            EditorUI.DrawStepIndicator(StepLabels, GetStepStates());
            EditorGUILayout.Space(8);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            if (IsAllComplete())
                DrawDashboard();
            else
                DrawWizardStep(GetCurrentStep());

            EditorGUILayout.EndScrollView();
        }

        void DrawWizardStep(int step)
        {
            switch (step)
            {
                case 1: DrawStep_Addressables(); break;
                case 2: DrawStep_Folders(); break;
                case 3: DrawStep_AddrX(); break;
            }
        }

        // ═══════════════════════════════════════════════════════
        // ─── Step 1: Addressables 설정
        // ═══════════════════════════════════════════════════════

        void DrawStep_Addressables()
        {
            EditorUI.DrawSectionHeader("Step 2: Addressables Settings", TabColor);
            EditorGUILayout.Space(8);

            var pkgInfo = UnityEditor.PackageManager.PackageInfo
                .FindForAssembly(typeof(AddressableAssetSettings).Assembly);
            if (pkgInfo != null)
                EditorUI.DrawDescription($"\u2713 Addressables {pkgInfo.version} installed");

            EditorGUILayout.Space(8);
            EditorGUILayout.HelpBox(
                "Addressables Settings를 생성합니다.\nLocal 기본 설정이 적용됩니다.",
                MessageType.Info);
            EditorGUILayout.Space(8);

            if (EditorUI.DrawColorButton("Addressables Settings 생성", TabColor, 32))
            {
                AddressableAssetSettingsDefaultObject.Settings =
                    AddressableAssetSettings.Create(
                        AddressableAssetSettingsDefaultObject.kDefaultConfigFolder,
                        AddressableAssetSettingsDefaultObject.kDefaultConfigAssetName,
                        true, true);
                _notification = "Addressables Settings 생성 완료";
                _notificationType = EditorUI.NotificationType.Success;
                _repaint?.Invoke();
            }
        }

        // ═══════════════════════════════════════════════════════
        // ─── Step 2: 폴더 구조 (그룹 + 로컬/원격)
        // ═══════════════════════════════════════════════════════

        void DrawStep_Folders()
        {
            EditorUI.DrawSectionHeader("Step 3: 폴더 구조", TabColor);
            EditorGUILayout.Space(8);

            EditorGUILayout.HelpBox(
                "기본 폴더 구조를 생성합니다.\n" +
                "생성 후 대시보드에서 그룹 추가/삭제, 로컬/원격 전환이 가능합니다.",
                MessageType.Info);
            EditorGUILayout.Space(8);

            if (EditorUI.DrawColorButton("생성", TabColor, 32))
            {
                if (FolderTemplateGenerator.Generate())
                {
                    _notification = "폴더 구조 생성 완료";
                    _notificationType = EditorUI.NotificationType.Success;
                }
                else
                {
                    _notification = "생성 실패 — 콘솔 확인";
                    _notificationType = EditorUI.NotificationType.Error;
                }
                _repaint?.Invoke();
            }
        }

        void DrawGroupEditor()
        {
            var rules = AddrXSetupRules.GetOrCreate();
            var folders = rules.GetGroupFolders();

            if (folders.Length == 0)
            {
                EditorUI.DrawDescription("  Assets/Addressables/ 에 폴더가 없습니다.");
                return;
            }

            foreach (var folderName in folders)
            {
                bool isRemote = rules.IsGroupRemote(folderName);
                Color groupColor = isRemote ? COL_REMOTE : COL_LOCAL;

                EditorGUILayout.BeginHorizontal();

                EditorUI.DrawCellLabel("\u25CF", 14, groupColor);
                GUILayout.Label(folderName, BoldStyle, GUILayout.ExpandWidth(true));
                GUILayout.FlexibleSpace();

                // 로컬/원격 토글
                var toggleLabel = isRemote ? "Remote" : "Local";
                if (GUILayout.Button(toggleLabel, GUILayout.Width(60), GUILayout.Height(20)))
                {
                    rules.SetGroupRemote(folderName, !isRemote);

                    // Addressables 그룹 스키마도 업데이트
                    var settings = AddressableAssetSettingsDefaultObject.Settings;
                    if (settings != null)
                    {
                        var addrGroup = settings.FindGroup(folderName);
                        if (addrGroup != null)
                            AddrXAutoRegister.ApplyGroupSchema(addrGroup, !isRemote);
                    }
                }

                EditorGUILayout.EndHorizontal();
            }
        }

        // ═══════════════════════════════════════════════════════
        // ─── Step 3: AddrX 설정 + Label Category
        // ═══════════════════════════════════════════════════════

        SerializedObject _addrxSo;

        void DrawStep_AddrX()
        {
            EditorUI.DrawSectionHeader("Step 4: AddrX Settings", TabColor);
            EditorGUILayout.Space(8);

            EditorGUILayout.HelpBox(
                "AddrX 기본 설정을 확인하고 완료하세요.\n" +
                "나중에 톱니바퀴(\u2699)에서 변경 가능합니다.\n" +
                "그룹/라벨 관리는 대시보드에서 할 수 있습니다.",
                MessageType.Info);
            EditorGUILayout.Space(8);

            var settings = AddrXSettings.GetOrCreate();
            if (_addrxSo == null || _addrxSo.targetObject != settings)
                _addrxSo = new SerializedObject(settings);

            _addrxSo.Update();

            EditorUI.DrawProperty(_addrxSo, "_logLevel", "Log Level");
            EditorGUILayout.Space(4);
            EditorUI.DrawProperty(_addrxSo, "_enableTracking", "Enable Tracking");
            EditorUI.DrawProperty(_addrxSo, "_enableLeakDetection", "Enable Leak Detection");
            EditorGUILayout.Space(4);
            EditorUI.DrawProperty(_addrxSo, "_autoInitialize", "Auto Initialize");

            if (_addrxSo.ApplyModifiedProperties())
                settings.Apply();

            EditorGUILayout.Space(12);
            if (EditorUI.DrawColorButton("완료", TabColor, 32))
            {
                _notification = "AddrX 초기 설정 완료!";
                _notificationType = EditorUI.NotificationType.Success;
                UpdateDashboardCounts();
                _repaint?.Invoke();
            }
        }

        int _editCatIdx = -1;
        int _editOptIdx = -1;  // -1 = 옵션 편집, -2 = 카테고리명 편집
        int _editGraceFrames;  // 편집 진입 후 포커스 체크 유예 프레임
        bool _editWasFocused;  // 텍스트 필드가 포커스를 받은 적 있는지

        static GUIStyle _chipStyle;
        static GUIStyle _chipDefaultStyle;
        static GUIStyle ChipStyle => _chipStyle ??= new GUIStyle(EditorStyles.miniButton)
        {
            padding = new RectOffset(6, 6, 2, 2),
            margin = new RectOffset(2, 2, 1, 1),
            fixedHeight = 20
        };
        static GUIStyle ChipDefaultStyle => _chipDefaultStyle ??= new GUIStyle(ChipStyle)
        {
            fontStyle = FontStyle.Bold
        };

        const string EditTextFieldName = "AddrXLabelEdit";

        void DrawLabelCategoryEditor()
        {
            var rules = AddrXSetupRules.GetOrCreate();

            // 포커스 해제 시 편집 자동 완료
            if (_editCatIdx >= 0 && Event.current.type == EventType.Repaint)
            {
                bool isFocused = GUI.GetNameOfFocusedControl() == EditTextFieldName;

                if (isFocused)
                    _editWasFocused = true;

                if (_editGraceFrames > 0)
                    _editGraceFrames--;
                else if (_editWasFocused && !isFocused)
                    ApplyEdit(rules);
            }

            // Enter키 = 즉시 적용
            if (_editCatIdx >= 0 && Event.current.type == EventType.KeyDown
                && Event.current.keyCode == KeyCode.Return)
            {
                ApplyEdit(rules);
                Event.current.Use();
            }

            for (int ci = 0; ci < rules.LabelCategories.Count; ci++)
            {
                var cat = rules.LabelCategories[ci];

                // ── 카테고리 타이틀 행 ──
                if (_editCatIdx == ci && _editOptIdx == -2)
                {
                    EditorGUILayout.BeginHorizontal();
                    GUI.SetNextControlName(EditTextFieldName);
                    _editBuffer = EditorGUILayout.TextField(_editBuffer, GUILayout.Width(120));
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.EndHorizontal();
                }
                else
                {
                    EditorUI.DrawSectionHeader(cat.categoryName, EditorUI.COL_INFO);

                    var headerRect = GUILayoutUtility.GetLastRect();
                    var e = Event.current;
                    if (e.type == EventType.MouseDown && e.button == 1
                        && headerRect.Contains(e.mousePosition))
                    {
                        int capturedCi = ci;
                        var menu = new GenericMenu();
                        menu.AddItem(new GUIContent("Rename"), false, () =>
                        {
                            _editCatIdx = capturedCi;
                            _editOptIdx = -2;
                            _editBuffer = rules.LabelCategories[capturedCi].categoryName;
                            _editGraceFrames = 10; _editWasFocused = false;
                        });
                        menu.AddItem(new GUIContent("Delete"), false, () =>
                        {
                            if (EditorUtility.DisplayDialog("Label Category 삭제",
                                    $"'{rules.LabelCategories[capturedCi].categoryName}' 카테고리를 삭제하시겠습니까?",
                                    "삭제", "취소"))
                            {
                                rules.LabelCategories.RemoveAt(capturedCi);
                                EditorUtility.SetDirty(rules);
                            }
                        });
                        menu.ShowAsContext();
                        e.Use();
                    }
                }

                EditorGUILayout.Space(4);

                // ── 옵션 칩 행 ──
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(8);

                // 옵션 칩들 (가로 나열)
                for (int oi = 0; oi < cat.options.Count; oi++)
                {
                    bool isDefault = cat.options[oi] == cat.defaultValue;

                    if (_editCatIdx == ci && _editOptIdx == oi)
                    {
                        GUI.SetNextControlName(EditTextFieldName);
                        _editBuffer = EditorGUILayout.TextField(_editBuffer, GUILayout.Width(60));
                    }
                    else
                    {
                        var chipLabel = isDefault ? $"\u2605 {cat.options[oi]}" : cat.options[oi];
                        var chipStyle = isDefault ? ChipDefaultStyle : ChipStyle;

                        // 칩 Rect 확보
                        var chipContent = new GUIContent(chipLabel);
                        var chipRect = GUILayoutUtility.GetRect(chipContent, chipStyle);

                        // 좌클릭 = 디폴트 설정
                        var ev = Event.current;
                        if (ev.type == EventType.MouseDown && ev.button == 0
                            && chipRect.Contains(ev.mousePosition))
                        {
                            cat.defaultValue = cat.options[oi];
                            EditorUtility.SetDirty(rules);
                            ev.Use();
                        }

                        // 우클릭 = Rename/Set as Default/Delete
                        if (ev.type == EventType.MouseDown && ev.button == 1
                            && chipRect.Contains(ev.mousePosition))
                        {
                            int capturedCi = ci, capturedOi = oi;
                            var capturedCat = cat;
                            var menu = new GenericMenu();

                            menu.AddItem(new GUIContent("Rename"), false, () =>
                            {
                                _editCatIdx = capturedCi;
                                _editOptIdx = capturedOi;
                                _editBuffer = capturedCat.options[capturedOi];
                                _editGraceFrames = 10; _editWasFocused = false;
                            });

                            if (!isDefault)
                            {
                                menu.AddItem(new GUIContent("Set as Default"), false, () =>
                                {
                                    capturedCat.defaultValue = capturedCat.options[capturedOi];
                                    EditorUtility.SetDirty(rules);
                                });
                            }

                            if (capturedCat.options.Count > 1)
                            {
                                menu.AddItem(new GUIContent("Delete"), false, () =>
                                {
                                    var optName = capturedCat.options[capturedOi];
                                    if (EditorUtility.DisplayDialog("옵션 삭제",
                                            $"'{optName}'을(를) 삭제하시겠습니까?", "삭제", "취소"))
                                    {
                                        bool wasDefault = capturedCat.defaultValue == optName;
                                        capturedCat.options.RemoveAt(capturedOi);
                                        if (wasDefault && capturedCat.options.Count > 0)
                                            capturedCat.defaultValue = capturedCat.options[0];
                                        EditorUtility.SetDirty(rules);
                                    }
                                });
                            }

                            menu.ShowAsContext();
                            ev.Use();
                        }

                        // 칩 그리기
                        GUI.Button(chipRect, chipContent, chipStyle);
                    }
                }

                // + 버튼
                if (GUILayout.Button("+", ChipStyle, GUILayout.Width(22)))
                {
                    cat.options.Add("New");
                    EditorUtility.SetDirty(rules);
                    _editCatIdx = ci;
                    _editOptIdx = cat.options.Count - 1;
                    _editBuffer = "New";
                    _editGraceFrames = 10; _editWasFocused = false;
                }

                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(2);
            }

            // + Category 버튼
            if (EditorUI.DrawColorButton("+ Category", EditorUI.COL_INFO))
            {
                rules.LabelCategories.Add(new LabelCategory
                {
                    categoryName = "NewCategory",
                    defaultValue = "Default",
                    options = new List<string> { "Default" }
                });
                EditorUtility.SetDirty(rules);
                _editCatIdx = rules.LabelCategories.Count - 1;
                _editOptIdx = -2;
                _editBuffer = "NewCategory";
                _editGraceFrames = 10; _editWasFocused = false;
            }
        }

        /// <summary>편집 중인 텍스트를 적용하고 편집 모드를 종료한다.</summary>
        void ApplyEdit(AddrXSetupRules rules)
        {
            if (_editCatIdx < 0) return;
            if (string.IsNullOrWhiteSpace(_editBuffer))
            {
                _editCatIdx = -1;
                _editOptIdx = -1;
                return;
            }

            if (_editCatIdx < rules.LabelCategories.Count)
            {
                var cat = rules.LabelCategories[_editCatIdx];

                if (_editOptIdx == -2)
                {
                    // 카테고리명 변경
                    cat.categoryName = _editBuffer;
                }
                else if (_editOptIdx >= 0 && _editOptIdx < cat.options.Count)
                {
                    // 옵션명 변경
                    bool wasDefault = cat.options[_editOptIdx] == cat.defaultValue;
                    cat.options[_editOptIdx] = _editBuffer;
                    if (wasDefault) cat.defaultValue = _editBuffer;
                }

                EditorUtility.SetDirty(rules);
            }

            _editCatIdx = -1;
            _editOptIdx = -1;
        }

        // ═══════════════════════════════════════════════════════
        // ─── Dashboard
        // ═══════════════════════════════════════════════════════

        void DrawDashboard()
        {
            // ── 상태 요약 ──
            EditorUI.DrawSectionHeader("Status", EditorUI.COL_SUCCESS);
            EditorGUILayout.Space(4);

            var pkgInfo = UnityEditor.PackageManager.PackageInfo
                .FindForAssembly(typeof(AddressableAssetSettings).Assembly);
            var rules = AddrXSetupRules.Instance;
            var folders = rules != null ? rules.GetGroupFolders() : System.Array.Empty<string>();
            int localCount = 0, remoteCount = 0;
            foreach (var f in folders)
            {
                if (rules.IsGroupRemote(f)) remoteCount++;
                else localCount++;
            }

            EditorUI.BeginRow();
            EditorUI.DrawStatCard("Addressables", pkgInfo?.version ?? "?", EditorUI.COL_SUCCESS);
            EditorUI.DrawStatCard("Local", localCount.ToString(), COL_LOCAL);
            EditorUI.DrawStatCard("Remote", remoteCount.ToString(), COL_REMOTE);
            EditorUI.DrawStatCard("Labels", rules?.LabelCategories.Count.ToString() ?? "0", EditorUI.COL_INFO);
            EditorUI.EndRow();

            EditorGUILayout.Space(8);

            // ── 그룹 관리 ──
            if (EditorUI.DrawSectionFoldout(ref _showGroups,
                    $"Groups ({folders.Length})", COL_LOCAL))
            {
                EditorUI.BeginSubBox();
                DrawGroupEditor();
                EditorUI.EndSubBox();
            }
            EditorGUILayout.Space(8);

            // ── 라벨 관리 ──
            if (EditorUI.DrawSectionFoldout(ref _showLabels,
                    $"Label Categories ({rules.LabelCategories.Count})", EditorUI.COL_INFO))
            {
                EditorUI.BeginSubBox();
                DrawLabelCategoryEditor();
                EditorUI.EndSubBox();
            }
            EditorGUILayout.Space(8);

            // ── 에셋 상태 ──
            EditorUI.DrawSectionHeader("Assets", EditorUI.COL_INFO);
            EditorGUILayout.Space(4);

            EditorUI.BeginRow();
            EditorUI.DrawStatCard("Registered", _registeredCount.ToString(), EditorUI.COL_SUCCESS);
            EditorUI.DrawStatCard("Unregistered", _unregisteredCount.ToString(),
                _unregisteredCount > 0 ? EditorUI.COL_WARN : EditorUI.COL_MUTED);
            EditorUI.DrawStatCard("Conflicts", _conflicts.Count.ToString(),
                _conflicts.Count > 0 ? EditorUI.COL_ERROR : EditorUI.COL_MUTED);
            EditorUI.EndRow();

            EditorGUILayout.Space(8);

            // ── 액션 ──
            EditorUI.DrawActionBar(new (string, Color, Action)[]
            {
                ("전체 동기화", EditorUI.COL_SUCCESS, () =>
                {
                    SyncAll();
                    UpdateDashboardCounts();
                    _repaint?.Invoke();
                }),
                ("상태 갱신", EditorUI.COL_INFO, () =>
                {
                    UpdateDashboardCounts();
                    _notification = "상태 갱신 완료";
                    _notificationType = EditorUI.NotificationType.Success;
                    _repaint?.Invoke();
                })
            }, $"{_registeredCount} registered");

            // ── 충돌 목록 ──
            if (_conflicts.Count > 0)
            {
                EditorGUILayout.Space(8);
                _showConflicts = EditorUI.DrawToggleRow(
                    $"충돌 목록 ({_conflicts.Count}건)", _showConflicts, EditorUI.COL_ERROR);

                if (_showConflicts)
                {
                    EditorUI.BeginSubBox();
                    foreach (var (address, paths) in _conflicts)
                    {
                        EditorUI.DrawCellLabel($"  {address}", color: EditorUI.COL_ERROR);
                        foreach (var p in paths)
                            EditorUI.DrawDescription($"    \u2192 {p}");
                    }
                    EditorUI.EndSubBox();
                }
            }
        }

        // ═══════════════════════════════════════════════════════
        // ─── Dashboard 데이터
        // ═══════════════════════════════════════════════════════

        void UpdateDashboardCounts()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            var rules = AddrXSetupRules.Instance;

            _registeredCount = 0;
            _unregisteredCount = 0;
            _conflicts.Clear();

            if (settings == null || rules == null || !AssetDatabase.IsValidFolder(rules.RootPath)) return;

            var registeredGuids = new HashSet<string>();
            foreach (var group in settings.groups)
            {
                if (group == null) continue;
                foreach (var entry in group.entries)
                    registeredGuids.Add(entry.guid);
            }

            var guids = AssetDatabase.FindAssets("", new[] { rules.RootPath });
            var addressMap = new Dictionary<string, List<string>>();

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.IsValidFolder(path)) continue;

                var address = rules.GetAddress(path);
                if (address == null) continue;

                if (!addressMap.ContainsKey(address))
                    addressMap[address] = new List<string>();
                addressMap[address].Add(path);

                if (registeredGuids.Contains(guid)) _registeredCount++;
                else _unregisteredCount++;
            }

            foreach (var kv in addressMap.Where(kv => kv.Value.Count > 1))
                _conflicts.Add((kv.Key, kv.Value));
        }

        void SyncAll()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            var rules = AddrXSetupRules.Instance;
            if (settings == null || rules == null || !AssetDatabase.IsValidFolder(rules.RootPath)) return;

            var guids = AssetDatabase.FindAssets("", new[] { rules.RootPath });
            var paths = new List<string>();
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!AssetDatabase.IsValidFolder(path))
                    paths.Add(path);
            }

            var duplicates = AddrXAutoRegister.DetectDuplicates(paths, rules, settings);
            int registered = 0, skipped = 0;

            foreach (var path in paths)
            {
                if (duplicates.Contains(rules.GetAddress(path) ?? ""))
                { skipped++; continue; }
                if (AddrXAutoRegister.RegisterAsset(settings, rules, path, duplicates))
                    registered++;
            }

            settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, null, true);

            _notification = $"동기화 완료: {registered}개 등록"
                + (skipped > 0 ? $", {skipped}개 충돌 스킵" : "");
            _notificationType = skipped > 0
                ? EditorUI.NotificationType.Error
                : EditorUI.NotificationType.Success;
        }
    }
}
#endif
