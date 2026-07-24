#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using Tjdtjq5.AddrX.Editor.Analysis;

namespace Tjdtjq5.AddrX.Editor
{
    /// <summary>분석 탭. 원클릭 전체 분석 + 섹션별 그룹핑 리포트 + Impact Analyzer.</summary>
    internal class AnalysisTab : AddrXTabBase
    {
        readonly Action _repaint;
        Vector2 _scroll;
        bool _analyzed;

        // 전체 분석 결과
        DuplicateReport? _dupReport;
        List<GroupScore> _healthScores;
        List<BudgetViolation> _budgetViolations;
        List<DiffWarning> _diffWarnings;
        List<ImpactReport> _impactAll;
        List<NondeterminismWarning> _nondetWarnings;

        // 캐싱된 집계값
        int _lowHealthCount;
        int _heavyImpactCount;

        // 섹션 접기/펼치기
        bool _foldDup = true;
        bool _foldHealth = true;
        bool _foldBudget = true;
        bool _foldDiff = true;
        bool _foldImpact = true;
        bool _foldNondet = true;


        readonly Dictionary<string, bool> _itemFoldouts = new();
        readonly Dictionary<string, UnityEngine.Object> _assetCache = new();

        public AnalysisTab(Action repaint) => _repaint = repaint;

        public override string TabName => "Analysis";
        public override Color TabColor => new(0.9f, 0.4f, 0.4f);

        public override void OnDraw()
        {
            AddrXGui.DrawNotificationBar(ref _notification, _notificationType);

            // Addressables Settings 미생성 검증
            if (AddressableAssetSettingsDefaultObject.Settings == null)
            {
                EditorGUILayout.Space(20);
                EditorGUILayout.LabelField(
                    "Addressables Settings가 아직 생성되지 않았습니다",
                    EditorStyles.centeredGreyMiniLabel);
                EditorGUILayout.Space(8);
                EditorGUILayout.HelpBox(
                    "Analysis 기능을 사용하려면 Addressables Settings를 먼저 생성해야 합니다.\n" +
                    "Setup 탭에서 생성하거나 Window > Asset Management > Addressables > Groups에서 생성할 수 있습니다.",
                    MessageType.Warning);
                return;
            }

            // 전체 분석 버튼
            EditorGUILayout.Space(4);
            if (GUILayout.Button("Analyze All", GUILayout.Height(36)))
                RunAllAnalysis();

            EditorGUILayout.Space(8);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            if (!_analyzed)
            {
                EditorGUILayout.LabelField(
                    "Analyze All 버튼을 눌러 전체 분석을 실행합니다",
                    EditorStyles.centeredGreyMiniLabel);
            }
            else
            {
                DrawSummaryCards();
                EditorGUILayout.Space(8);
                DrawDuplicatesSection();
                DrawHealthSection();
                DrawBudgetSection();
                DrawDiffSection();
                DrawImpactAllSection();
                DrawNondetSection();
            }

            EditorGUILayout.EndScrollView();
        }

        // ─── 전체 분석 ───

        void RunAllAnalysis()
        {
            _dupReport = DuplicateScanner.Scan();
            _healthScores = GroupHealthScore.Evaluate();
            _budgetViolations = BundleSizeBudget.Check();
            _diffWarnings = BehaviorDiffChecker.Check();
            _impactAll = ImpactAnalyzer.ScanAll();
            _nondetWarnings = NondeterminismScanner.Scan();
            _assetCache.Clear();
            _analyzed = true;

            _heavyImpactCount = _impactAll?.Count(r => r.BundleCount > 1) ?? 0;
            _lowHealthCount = _healthScores?.Count(s => s.Score < 50) ?? 0;
            int issues = (_dupReport?.Count ?? 0)
                       + (_budgetViolations?.Count ?? 0)
                       + (_diffWarnings?.Count ?? 0)
                       + _lowHealthCount
                       + _heavyImpactCount
                       + (_nondetWarnings?.Count ?? 0);

            _notification = issues > 0
                ? $"분석 완료 — {issues}개 이슈 발견"
                : "분석 완료 — 이슈 없음";
            _notificationType = issues > 0
                ? NotificationType.Error
                : NotificationType.Success;
        }

        // ─── 요약 카드 ───

        void DrawSummaryCards()
        {
            int dupCount = _dupReport?.Count ?? 0;
            int budgetCount = _budgetViolations?.Count ?? 0;
            int diffCount = _diffWarnings?.Count ?? 0;
            int nondetCount = _nondetWarnings?.Count ?? 0;

            EditorGUILayout.BeginHorizontal();
            AddrXGui.DrawStatCard("Duplicates", $"{dupCount}");
            AddrXGui.DrawStatCard("Low Health", $"{_lowHealthCount}");
            AddrXGui.DrawStatCard("Over Budget", $"{budgetCount}");
            AddrXGui.DrawStatCard("Diff Warnings", $"{diffCount}");
            AddrXGui.DrawStatCard("Heavy Impact", $"{_heavyImpactCount}");
            AddrXGui.DrawStatCard("Non-det", $"{nondetCount}");
            EditorGUILayout.EndHorizontal();
        }

        // ─── Duplicates 섹션 ───

        void DrawDuplicatesSection()
        {
            var count = _dupReport?.Count ?? 0;
            _foldDup = EditorGUILayout.Foldout(_foldDup, $"Duplicates ({count})", true);
            if (!_foldDup) return;

            if (count == 0)
            {
                EditorGUILayout.LabelField("  중복 에셋 없음", EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.Space(4);
                return;
            }

            foreach (var entry in _dupReport.Value.Entries)
            {
                var key = $"dup_{entry.AssetPath}";
                if (!_itemFoldouts.ContainsKey(key)) _itemFoldouts[key] = false;
                bool ex = _itemFoldouts[key];

                if (AddrXGui.BeginServiceCard(
                        System.IO.Path.GetFileName(entry.AssetPath),
                        $"{entry.Groups.Count} groups",
                        string.Join(", ", entry.Groups), ref ex))
                {
                    var obj = GetAsset(entry.AssetPath);
                    EditorGUILayout.ObjectField("Asset", obj,
                        typeof(UnityEngine.Object), false);
                    EditorGUILayout.LabelField($"경로: {entry.AssetPath}", EditorStyles.wordWrappedMiniLabel);
                    EditorGUILayout.LabelField("포함된 그룹", EditorStyles.miniLabel);
                    foreach (var g in entry.Groups)
                        EditorGUILayout.LabelField($"  • {g}");
                }
                AddrXGui.EndServiceCard();
                _itemFoldouts[key] = ex;
            }

            EditorGUILayout.Space(4);
        }

        // ─── Health 섹션 ───

        void DrawHealthSection()
        {
            var count = _healthScores?.Count ?? 0;
            _foldHealth = EditorGUILayout.Foldout(_foldHealth, $"Health Score ({count} groups)", true);
            if (!_foldHealth) return;

            if (count == 0)
            {
                EditorGUILayout.LabelField("  평가할 그룹 없음", EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.Space(4);
                return;
            }

            foreach (var gs in _healthScores)
            {
                var key = $"hp_{gs.GroupName}";
                if (!_itemFoldouts.ContainsKey(key)) _itemFoldouts[key] = false;
                bool ex = _itemFoldouts[key];

                if (AddrXGui.BeginServiceCard(
                        gs.GroupName,
                        $"{gs.Score:F0}/100",
                        $"{gs.EntryCount} entries, {gs.SizeText}", ref ex))
                {
                    if (gs.Issues.Count > 0)
                        EditorGUILayout.HelpBox(string.Join("\n", gs.Issues), MessageType.Info);
                    else
                        EditorGUILayout.LabelField("문제 없음", EditorStyles.wordWrappedMiniLabel);
                }
                AddrXGui.EndServiceCard();
                _itemFoldouts[key] = ex;
            }

            EditorGUILayout.Space(4);
        }

        // ─── Budget 섹션 ───

        void DrawBudgetSection()
        {
            var count = _budgetViolations?.Count ?? 0;
            _foldBudget = EditorGUILayout.Foldout(_foldBudget, $"Size Budget ({count} violations)", true);
            if (!_foldBudget) return;

            if (count == 0)
            {
                EditorGUILayout.LabelField("  모든 그룹이 예산 이내", EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.Space(4);
                return;
            }

            foreach (var v in _budgetViolations)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField(v.GroupName, EditorStyles.boldLabel);
                EditorGUILayout.Space(2);
                EditorGUILayout.BeginHorizontal();
                AddrXGui.DrawStatCard("Actual", v.ActualText);
                AddrXGui.DrawStatCard("Budget", v.BudgetText);
                AddrXGui.DrawStatCard("Over", $"+{v.OverPercent:F0}%");
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.LabelField($"에셋 수: {v.EntryCount}", EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(4);
            }
        }

        // ─── Diff 섹션 ───

        void DrawDiffSection()
        {
            var count = _diffWarnings?.Count ?? 0;
            _foldDiff = EditorGUILayout.Foldout(_foldDiff, $"Behavior Diff ({count} warnings)", true);
            if (!_foldDiff) return;

            if (count == 0)
            {
                EditorGUILayout.LabelField("  알려진 동작 차이 없음", EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.Space(4);
                return;
            }

            for (int i = 0; i < _diffWarnings.Count; i++)
            {
                var w = _diffWarnings[i];

                var key = $"diff_{i}_{w.AssetPath}";
                if (!_itemFoldouts.ContainsKey(key)) _itemFoldouts[key] = false;
                bool ex = _itemFoldouts[key];

                if (AddrXGui.BeginServiceCard(
                        w.RuleName, "Warning",
                        w.AssetPath ?? "", ref ex))
                {
                    EditorGUILayout.LabelField(w.Message, EditorStyles.wordWrappedMiniLabel);
                    if (!string.IsNullOrEmpty(w.AssetPath))
                    {
                        EditorGUILayout.Space(4);
                        var obj = GetAsset(w.AssetPath);
                        EditorGUILayout.ObjectField("Asset", obj,
                            typeof(UnityEngine.Object), false);
                    }
                }
                AddrXGui.EndServiceCard();
                _itemFoldouts[key] = ex;
            }

            EditorGUILayout.Space(4);
        }

        // ─── Impact 전체 스캔 (Analyze All 포함) ───

        void DrawImpactAllSection()
        {
            var heavy = _impactAll?.Where(r => r.BundleCount > 1).ToList();
            var count = heavy?.Count ?? 0;

            _foldImpact = EditorGUILayout.Foldout(_foldImpact, $"Impact ({count} heavy)", true);
            if (!_foldImpact) return;

            if (_impactAll == null || _impactAll.Count == 0)
            {
                EditorGUILayout.LabelField("  분석할 에셋 없음", EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.Space(4);
                return;
            }

            if (count == 0)
            {
                EditorGUILayout.LabelField("  모든 에셋이 단일 번들 로드 (연쇄 없음)", EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.Space(4);
                return;
            }

            foreach (var report in heavy)
            {
                var key = $"impall_{report.Address}";
                if (!_itemFoldouts.ContainsKey(key)) _itemFoldouts[key] = false;
                bool ex = _itemFoldouts[key];

                if (AddrXGui.BeginServiceCard(
                        report.Address ?? System.IO.Path.GetFileName(report.AssetPath),
                        $"{report.BundleCount} bundles, {report.TotalSizeText}",
                        $"Source: {report.SourceGroup}", ref ex))
                {
                    foreach (var impact in report.Impacts)
                    {
                        bool isSrc = impact.GroupName == report.SourceGroup;
                        var prefix = isSrc ? "(source)" : "(chain)";
                        EditorGUILayout.LabelField(
                            $"  {prefix} {impact.GroupName} — {impact.SizeText} ({impact.Assets.Count} assets)");
                    }
                }
                AddrXGui.EndServiceCard();
                _itemFoldouts[key] = ex;
            }

            EditorGUILayout.Space(4);
        }

        // ─── Non-determinism 섹션 ───

        void DrawNondetSection()
        {
            var count = _nondetWarnings?.Count ?? 0;
            _foldNondet = EditorGUILayout.Foldout(_foldNondet, $"Non-determinism ({count})", true);
            if (!_foldNondet) return;

            if (count == 0)
            {
                EditorGUILayout.LabelField("  비결정성 패턴 없음", EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.Space(4);
                return;
            }

            foreach (var w in _nondetWarnings)
            {
                var key = $"nondet_{w.FilePath}_{w.Line}";
                if (!_itemFoldouts.ContainsKey(key)) _itemFoldouts[key] = false;
                bool ex = _itemFoldouts[key];

                var fileName = System.IO.Path.GetFileName(w.FilePath);

                if (AddrXGui.BeginServiceCard(
                        $"{fileName}:{w.Line}", "Warning",
                        w.Message, ref ex))
                {
                    EditorGUILayout.LabelField($"경로: {w.FilePath}", EditorStyles.wordWrappedMiniLabel);
                    EditorGUILayout.LabelField($"패턴: {w.Pattern}", EditorStyles.wordWrappedMiniLabel);
                    EditorGUILayout.Space(4);
                    var obj = AssetDatabase.LoadMainAssetAtPath(w.FilePath);
                    if (obj != null)
                        EditorGUILayout.ObjectField("Script", obj,
                            typeof(UnityEngine.Object), false);
                }
                AddrXGui.EndServiceCard();
                _itemFoldouts[key] = ex;
            }

            EditorGUILayout.Space(4);
        }

        UnityEngine.Object GetAsset(string path)
        {
            if (!_assetCache.TryGetValue(path, out var obj))
            {
                obj = AssetDatabase.LoadMainAssetAtPath(path);
                _assetCache[path] = obj;
            }
            return obj;
        }
    }
}
#endif
