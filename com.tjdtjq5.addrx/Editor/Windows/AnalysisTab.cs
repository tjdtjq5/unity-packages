#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using Tjdtjq5.EditorToolkit.Editor;
using Tjdtjq5.AddrX.Editor.Analysis;

namespace Tjdtjq5.AddrX.Editor
{
    /// <summary>분석 탭. 원클릭 전체 분석 + 섹션별 그룹핑 리포트 + Impact Analyzer.</summary>
    public class AnalysisTab : EditorTabBase
    {
        static readonly Color COL_DUP = new(0.95f, 0.5f, 0.3f);
        static readonly Color COL_HP = new(0.3f, 0.8f, 0.4f);
        static readonly Color COL_BUD = new(0.4f, 0.7f, 0.95f);
        static readonly Color COL_DIFF = new(0.95f, 0.75f, 0.2f);
        static readonly Color COL_IMPACT = new(0.7f, 0.5f, 0.9f);

        readonly Action _repaint;
        Vector2 _scroll;
        new string _notification;
        new EditorUI.NotificationType _notificationType;
        bool _analyzed;

        // 전체 분석 결과
        DuplicateReport? _dupReport;
        List<GroupScore> _healthScores;
        List<BudgetViolation> _budgetViolations;
        List<DiffWarning> _diffWarnings;
        List<ImpactReport> _impactAll;
        List<NondeterminismWarning> _nondetWarnings;

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
            EditorUI.DrawNotificationBar(ref _notification, _notificationType);

            // Addressables Settings 미생성 검증
            if (AddressableAssetSettingsDefaultObject.Settings == null)
            {
                EditorGUILayout.Space(20);
                EditorUI.DrawPlaceholder(
                    "Addressables Settings가 아직 생성되지 않았습니다");
                EditorGUILayout.Space(8);
                EditorGUILayout.HelpBox(
                    "Analysis 기능을 사용하려면 Addressables Settings를 먼저 생성해야 합니다.\n" +
                    "Setup 탭에서 생성하거나 Window > Asset Management > Addressables > Groups에서 생성할 수 있습니다.",
                    MessageType.Warning);
                return;
            }

            // 전체 분석 버튼
            EditorGUILayout.Space(4);
            if (EditorUI.DrawColorButton("Analyze All", TabColor, 36))
                RunAllAnalysis();

            EditorGUILayout.Space(8);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            if (!_analyzed)
            {
                EditorUI.DrawPlaceholder(
                    "Analyze All 버튼을 눌러 전체 분석을 실행합니다");
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

            int heavyImpact = _impactAll?.Count(r => r.BundleCount > 1) ?? 0;
            int issues = (_dupReport?.Count ?? 0)
                       + (_budgetViolations?.Count ?? 0)
                       + (_diffWarnings?.Count ?? 0)
                       + (_healthScores?.Count(s => s.Score < 50) ?? 0)
                       + heavyImpact
                       + (_nondetWarnings?.Count ?? 0);

            _notification = issues > 0
                ? $"분석 완료 — {issues}개 이슈 발견"
                : "분석 완료 — 이슈 없음";
            _notificationType = issues > 0
                ? EditorUI.NotificationType.Error
                : EditorUI.NotificationType.Success;
        }

        // ─── 요약 카드 ───

        void DrawSummaryCards()
        {
            EditorUI.BeginRow();
            EditorUI.DrawStatCard("Duplicates",
                $"{_dupReport?.Count ?? 0}",
                (_dupReport?.Count ?? 0) > 0 ? COL_ERROR : COL_SUCCESS);
            EditorUI.DrawStatCard("Low Health",
                $"{_healthScores?.Count(s => s.Score < 50) ?? 0}",
                (_healthScores?.Any(s => s.Score < 50) ?? false)
                    ? COL_ERROR : COL_SUCCESS);
            EditorUI.DrawStatCard("Over Budget",
                $"{_budgetViolations?.Count ?? 0}",
                (_budgetViolations?.Count ?? 0) > 0 ? COL_ERROR : COL_SUCCESS);
            EditorUI.DrawStatCard("Diff Warnings",
                $"{_diffWarnings?.Count ?? 0}",
                (_diffWarnings?.Count ?? 0) > 0 ? COL_WARN : COL_SUCCESS);
            EditorUI.DrawStatCard("Heavy Impact",
                $"{_impactAll?.Count(r => r.BundleCount > 1) ?? 0}",
                (_impactAll?.Any(r => r.BundleCount > 1) ?? false)
                    ? COL_WARN : COL_SUCCESS);
            EditorUI.DrawStatCard("Non-det",
                $"{_nondetWarnings?.Count ?? 0}",
                (_nondetWarnings?.Count ?? 0) > 0 ? COL_WARN : COL_SUCCESS);
            EditorUI.EndRow();
        }

        // ─── Duplicates 섹션 ───

        void DrawDuplicatesSection()
        {
            var count = _dupReport?.Count ?? 0;
            if (!EditorUI.DrawSectionFoldout(ref _foldDup,
                    $"Duplicates ({count})", COL_DUP)) return;

            if (count == 0)
            {
                EditorUI.DrawDescription("  중복 에셋 없음");
                EditorGUILayout.Space(4);
                return;
            }

            foreach (var entry in _dupReport.Value.Entries)
            {
                var key = $"dup_{entry.AssetPath}";
                if (!_itemFoldouts.ContainsKey(key)) _itemFoldouts[key] = false;
                bool ex = _itemFoldouts[key];

                if (EditorUI.BeginServiceCard(
                        System.IO.Path.GetFileName(entry.AssetPath), COL_DUP,
                        $"{entry.Groups.Count} groups", 2,
                        string.Join(", ", entry.Groups), ref ex))
                {
                    var obj = GetAsset(entry.AssetPath);
                    EditorGUILayout.ObjectField("Asset", obj,
                        typeof(UnityEngine.Object), false);
                    EditorUI.DrawDescription($"경로: {entry.AssetPath}");
                    EditorUI.DrawSubLabel("포함된 그룹");
                    foreach (var g in entry.Groups)
                        EditorUI.DrawCellLabel($"  \u2022 {g}");
                }
                EditorUI.EndServiceCard(ref ex);
                _itemFoldouts[key] = ex;
            }

            EditorGUILayout.Space(4);
        }

        // ─── Health 섹션 ───

        void DrawHealthSection()
        {
            var count = _healthScores?.Count ?? 0;
            if (!EditorUI.DrawSectionFoldout(ref _foldHealth,
                    $"Health Score ({count} groups)", COL_HP)) return;

            if (count == 0)
            {
                EditorUI.DrawDescription("  평가할 그룹 없음");
                EditorGUILayout.Space(4);
                return;
            }

            foreach (var gs in _healthScores)
            {
                Color c = gs.Score >= 80 ? COL_SUCCESS
                    : gs.Score >= 50 ? COL_WARN
                    : COL_ERROR;
                int state = gs.Score >= 80 ? 1 : gs.Score >= 50 ? 2 : 0;

                var key = $"hp_{gs.GroupName}";
                if (!_itemFoldouts.ContainsKey(key)) _itemFoldouts[key] = false;
                bool ex = _itemFoldouts[key];

                if (EditorUI.BeginServiceCard(
                        gs.GroupName, c,
                        $"{gs.Score:F0}/100", state,
                        $"{gs.EntryCount} entries, {gs.SizeText}", ref ex))
                {
                    if (gs.Issues.Count > 0)
                        EditorUI.DrawInfoBox(null, gs.Issues.ToArray());
                    else
                        EditorUI.DrawDescription("문제 없음");
                }
                EditorUI.EndServiceCard(ref ex);
                _itemFoldouts[key] = ex;
            }

            EditorGUILayout.Space(4);
        }

        // ─── Budget 섹션 ───

        void DrawBudgetSection()
        {
            var count = _budgetViolations?.Count ?? 0;
            if (!EditorUI.DrawSectionFoldout(ref _foldBudget,
                    $"Size Budget ({count} violations)", COL_BUD)) return;

            if (count == 0)
            {
                EditorUI.DrawDescription("  모든 그룹이 예산 이내");
                EditorGUILayout.Space(4);
                return;
            }

            foreach (var v in _budgetViolations)
            {
                EditorUI.BeginSubBox();
                EditorUI.DrawSectionHeader(v.GroupName, COL_ERROR);
                EditorGUILayout.Space(2);
                EditorUI.BeginRow();
                EditorUI.DrawStatCard("Actual", v.ActualText, COL_ERROR);
                EditorUI.DrawStatCard("Budget", v.BudgetText, COL_MUTED);
                EditorUI.DrawStatCard("Over", $"+{v.OverPercent:F0}%", COL_ERROR);
                EditorUI.EndRow();
                EditorUI.DrawDescription($"에셋 수: {v.EntryCount}");
                EditorUI.EndSubBox();
                EditorGUILayout.Space(4);
            }
        }

        // ─── Diff 섹션 ───

        void DrawDiffSection()
        {
            var count = _diffWarnings?.Count ?? 0;
            if (!EditorUI.DrawSectionFoldout(ref _foldDiff,
                    $"Behavior Diff ({count} warnings)", COL_DIFF)) return;

            if (count == 0)
            {
                EditorUI.DrawDescription("  알려진 동작 차이 없음");
                EditorGUILayout.Space(4);
                return;
            }

            for (int i = 0; i < _diffWarnings.Count; i++)
            {
                var w = _diffWarnings[i];
                Color rc = w.RuleName.Contains("Resources") ? COL_ERROR
                    : w.RuleName.Contains("Scene") ? COL_WARN
                    : COL_INFO;

                var key = $"diff_{i}_{w.AssetPath}";
                if (!_itemFoldouts.ContainsKey(key)) _itemFoldouts[key] = false;
                bool ex = _itemFoldouts[key];

                if (EditorUI.BeginServiceCard(
                        w.RuleName, rc, "Warning", 2,
                        w.AssetPath ?? "", ref ex))
                {
                    EditorUI.DrawDescription(w.Message);
                    if (!string.IsNullOrEmpty(w.AssetPath))
                    {
                        EditorGUILayout.Space(4);
                        var obj = GetAsset(w.AssetPath);
                        EditorGUILayout.ObjectField("Asset", obj,
                            typeof(UnityEngine.Object), false);
                    }
                }
                EditorUI.EndServiceCard(ref ex);
                _itemFoldouts[key] = ex;
            }

            EditorGUILayout.Space(4);
        }

        // ─── Impact 전체 스캔 (Analyze All 포함) ───

        void DrawImpactAllSection()
        {
            var heavy = _impactAll?.Where(r => r.BundleCount > 1).ToList();
            var count = heavy?.Count ?? 0;

            if (!EditorUI.DrawSectionFoldout(ref _foldImpact,
                    $"Impact ({count} heavy)", COL_IMPACT)) return;

            if (_impactAll == null || _impactAll.Count == 0)
            {
                EditorUI.DrawDescription("  분석할 에셋 없음");
                EditorGUILayout.Space(4);
                return;
            }

            if (count == 0)
            {
                EditorUI.DrawDescription("  모든 에셋이 단일 번들 로드 (연쇄 없음)");
                EditorGUILayout.Space(4);
                return;
            }

            foreach (var report in heavy)
            {
                var key = $"impall_{report.Address}";
                if (!_itemFoldouts.ContainsKey(key)) _itemFoldouts[key] = false;
                bool ex = _itemFoldouts[key];

                if (EditorUI.BeginServiceCard(
                        report.Address ?? System.IO.Path.GetFileName(report.AssetPath),
                        COL_IMPACT,
                        $"{report.BundleCount} bundles, {report.TotalSizeText}",
                        report.BundleCount > 2 ? 0 : 2,
                        $"Source: {report.SourceGroup}", ref ex))
                {
                    foreach (var impact in report.Impacts)
                    {
                        bool isSrc = impact.GroupName == report.SourceGroup;
                        var prefix = isSrc ? "(source)" : "(chain)";
                        EditorUI.DrawCellLabel(
                            $"  {prefix} {impact.GroupName} — {impact.SizeText} ({impact.Assets.Count} assets)");
                    }
                }
                EditorUI.EndServiceCard(ref ex);
                _itemFoldouts[key] = ex;
            }

            EditorGUILayout.Space(4);
        }

        // ─── Non-determinism 섹션 ───

        void DrawNondetSection()
        {
            var count = _nondetWarnings?.Count ?? 0;
            if (!EditorUI.DrawSectionFoldout(ref _foldNondet,
                    $"Non-determinism ({count})", COL_DIFF)) return;

            if (count == 0)
            {
                EditorUI.DrawDescription("  비결정성 패턴 없음");
                EditorGUILayout.Space(4);
                return;
            }

            foreach (var w in _nondetWarnings)
            {
                var key = $"nondet_{w.FilePath}_{w.Line}";
                if (!_itemFoldouts.ContainsKey(key)) _itemFoldouts[key] = false;
                bool ex = _itemFoldouts[key];

                var fileName = System.IO.Path.GetFileName(w.FilePath);

                if (EditorUI.BeginServiceCard(
                        $"{fileName}:{w.Line}", COL_DIFF,
                        "Warning", 2,
                        w.Message, ref ex))
                {
                    EditorUI.DrawDescription($"경로: {w.FilePath}");
                    EditorUI.DrawDescription($"패턴: {w.Pattern}");
                    EditorGUILayout.Space(4);
                    var obj = AssetDatabase.LoadMainAssetAtPath(w.FilePath);
                    if (obj != null)
                        EditorGUILayout.ObjectField("Script", obj,
                            typeof(UnityEngine.Object), false);
                }
                EditorUI.EndServiceCard(ref ex);
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
