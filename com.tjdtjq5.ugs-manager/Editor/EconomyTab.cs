#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.UGSManager
{
    /// <summary>
    /// Economy 탭. 통화/아이템/구매 리소스 관리, 인라인 편집, Deploy+Publish, 환경 간 복사.
    /// </summary>
    public class EconomyTab : UGSTabBase
    {
        public override string TabName => "Economy";
        public override Color TabColor => new(0.85f, 0.55f, 0.25f);
        protected override string DashboardPath => "economy/configuration";

        // ─── 데이터 ──────────────────────────────────
        List<EconomyResource> _resources = new();
        string _economyDir = "";

        // ─── UI 상태 ────────────────────────────────
        ResizableColumns _columns;
        const int COL_STATUS = 0, COL_ID = 1, COL_NAME = 2, COL_TYPE = 3, COL_ACT = 4;

        bool _foldResources = true;
        bool _foldCreate;
        int _filterTypeIdx; // 0=All, 1=Currency, 2=Item, 3=Purchase

        // 새 리소스
        int _newTypeIdx;
        string _newId = "";
        string _newName = "";
        int _newInitial;
        int _newMax;

        static readonly string[] TYPE_LABELS = { "CUR", "ITEM", "VP", "RMP" };
        static readonly string[] TYPE_EXTENSIONS = { ".ecc", ".eci", ".ecv", ".ecr" };
        static readonly string[] FILTER_LABELS = { "All", "Currency", "Item", "Purchase" };
        static readonly string[] CREATE_LABELS = { "Currency", "Inventory Item", "Virtual Purchase", "Real Money Purchase" };

        // ─── 데이터 모델 ─────────────────────────────

        enum ResourceType { Currency, InventoryItem, VirtualPurchase, RealMoneyPurchase }
        enum SyncState { Synced, LocalOnly, ServerOnly }

        class EconomyResource
        {
            public string Id;
            public string Name;
            public ResourceType Type;
            public SyncState Status;
            public string FilePath;
            public bool IsExpanded;
            public bool IsDirty;

            // Currency
            public int Initial;
            public int Max;

            // Purchase
            public List<CostReward> Costs = new();
            public List<CostReward> Rewards = new();

            // RMP
            public string StoreId = "";
        }

        struct CostReward
        {
            public string ResourceId;
            public int Amount;
        }

        // ─── 데이터 로드 ─────────────────────────────

        protected override void FetchData()
        {
            _isLoading = false;
            _lastError = null;

            _columns ??= new ResizableColumns("UGS_EC", new[]
            {
                new ColDef("상태", 36f),
                new ColDef("ID", 120f, resizable: true),
                new ColDef("이름", 0f),
                new ColDef("타입", 50f),
                new ColDef("", 44f),
            });

            ResolveEconomyDir();
            ScanLocalFiles();

            _isLoading = true;
            UGSCliRunner.RunAsync("economy get-published -j -q", result =>
            {
                _isLoading = false;
                if (result.Success)
                {
                    _lastError = null;
                    _lastRefreshTime = DateTime.Now;
                    MergeServerResources(result.Output);
                }
                else
                {
                    Debug.LogWarning($"[UGS] economy get-published 실패: {result.Error}");
                }
            });

        }

        void ResolveEconomyDir()
        {
            // UGS 폴더 하위 Economy/ 탐색
            string projectRoot = Application.dataPath.Replace("/Assets", "");
            foreach (var dir in Directory.GetDirectories(Application.dataPath, "UGS", SearchOption.AllDirectories))
            {
                if (dir.Contains("Library") || dir.Contains("Temp") || dir.Contains("PackageCache")) continue;
                string ecoDir = Path.Combine(dir, "Economy");
                if (Directory.Exists(ecoDir) || File.Exists(Path.Combine(dir, "RemoteConfig.rc")))
                {
                    _economyDir = Path.Combine(dir, "Economy");
                    return;
                }
            }
            _economyDir = Path.Combine(projectRoot, "Assets/UGS/Economy");
        }

        // ─── 로컬 스캔 ──────────────────────────────

        void ScanLocalFiles()
        {
            _resources.Clear();
            if (!Directory.Exists(_economyDir)) return;

            foreach (var file in Directory.GetFiles(_economyDir))
            {
                string ext = Path.GetExtension(file).ToLower();
                int typeIdx = Array.IndexOf(TYPE_EXTENSIONS, ext);
                if (typeIdx < 0) continue;

                var res = ParseLocalFile(file, (ResourceType)typeIdx);
                if (res != null) _resources.Add(res);
            }
        }

        EconomyResource ParseLocalFile(string filePath, ResourceType type)
        {
            var res = new EconomyResource
            {
                Id = Path.GetFileNameWithoutExtension(filePath),
                FilePath = filePath,
                Type = type,
                Status = SyncState.LocalOnly
            };

            try
            {
                string json = File.ReadAllText(filePath);
                res.Name = ExtractStr(json, "name");

                if (type == ResourceType.Currency)
                {
                    res.Initial = ExtractInt(json, "initial");
                    res.Max = ExtractInt(json, "max");
                }
                else if (type == ResourceType.VirtualPurchase)
                {
                    res.Costs = ExtractCostRewardArray(json, "costs");
                    res.Rewards = ExtractCostRewardArray(json, "rewards");
                }
                else if (type == ResourceType.RealMoneyPurchase)
                {
                    res.Rewards = ExtractCostRewardArray(json, "rewards");
                    res.StoreId = ExtractNestedStr(json, "storeIdentifiers", "googlePlayStore");
                }
            }
            catch { /* ignore parse errors */ }

            return res;
        }

        // ─── 서버 데이터 병합 ────────────────────────

        void MergeServerResources(string json)
        {
            if (string.IsNullOrEmpty(json)) return;

            // "Resources" 배열 내부만 파싱
            int arrStart = json.IndexOf('[');
            int arrEnd = arrStart >= 0 ? JsonFindBracket(json, arrStart) : -1;
            if (arrStart < 0 || arrEnd < 0) return;
            string arrBlock = json.Substring(arrStart, arrEnd - arrStart + 1);

            var serverResources = new List<(string id, string name, ResourceType type)>();
            int searchFrom = 0;
            while (true)
            {
                int objStart = arrBlock.IndexOf('{', searchFrom);
                if (objStart < 0) break;
                int objEnd = JsonFindBrace(arrBlock, objStart);
                if (objEnd < 0) break;
                string obj = arrBlock.Substring(objStart, objEnd - objStart + 1);

                string id = ExtractStr(obj, "id");
                string name = ExtractStr(obj, "name");
                string typeStr = ExtractStr(obj, "type");

                if (!string.IsNullOrEmpty(id))
                {
                    ResourceType rt = typeStr switch
                    {
                        "CURRENCY" => ResourceType.Currency,
                        "INVENTORY_ITEM" => ResourceType.InventoryItem,
                        "VIRTUAL_PURCHASE" => ResourceType.VirtualPurchase,
                        "MONEY_PURCHASE" => ResourceType.RealMoneyPurchase,
                        _ => ResourceType.InventoryItem
                    };
                    serverResources.Add((id, name, rt));
                }

                searchFrom = objEnd + 1;
            }

            // 로컬과 병합
            var localIds = new HashSet<string>(_resources.Select(r => r.Id));
            foreach (var (id, name, type) in serverResources)
            {
                var existing = _resources.FirstOrDefault(r => r.Id == id);
                if (existing != null)
                {
                    existing.Status = SyncState.Synced;
                    // 로컬 이름 우선 (서버 이름은 인코딩 깨질 수 있음)
                    if (string.IsNullOrEmpty(existing.Name))
                        existing.Name = name;
                }
                else
                {
                    _resources.Add(new EconomyResource
                    {
                        Id = id, Name = name, Type = type,
                        Status = SyncState.ServerOnly
                    });
                }
            }
        }

        // ─── 메인 UI ────────────────────────────────

        public override void OnDraw()
        {
            DrawMainToolbar();
            DrawError();
            DrawSuccess();
            DrawLoading();
            if (_isLoading) return;

            GUILayout.Space(4);
            DrawResourceList();
            GUILayout.Space(8);
            DrawCreateSection();
            DrawEnvCopySection("economy", _economyDir, needsPublish: true,
                publishCmd: "economy publish", onComplete: () => FetchData());
        }

        // ─── 툴바 ──────────────────────────────────

        void DrawMainToolbar()
        {
            EditorGUILayout.BeginHorizontal();

            if (DrawColorBtn("Refresh", COL_INFO, 22)) FetchData();

            bool hasDirty = _resources.Any(r => r.IsDirty);
            GUI.enabled = hasDirty;
            if (DrawColorBtn("Save All", COL_WARN, 22)) SaveAllDirty();
            GUI.enabled = true;

            if (DrawColorBtn("Deploy ↑", COL_SUCCESS, 22)) DeployAndPublish();

            GUILayout.Space(8);
            int lc = _resources.Count(r => r.Status != SyncState.ServerOnly);
            int sc = _resources.Count(r => r.Status != SyncState.LocalOnly);
            EditorGUILayout.LabelField($"로컬: {lc} / 서버: {sc}",
                new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = COL_MUTED }, alignment = TextAnchor.MiddleLeft },
                GUILayout.Width(110));

            GUILayout.FlexibleSpace();

            if (!string.IsNullOrEmpty(DashboardPath) && DrawLinkBtn("Dashboard"))
            {
                if (UGSConfig.IsConfigured)
                {
                    var pid = UGSCliRunner.GetProjectId();
                    if (!string.IsNullOrEmpty(pid))
                    {
                        string url = UGSConfig.GetDashboardUrl(pid, null, DashboardPath);
                        if (!string.IsNullOrEmpty(url)) Application.OpenURL(url);
                    }
                }
            }

            if (_lastRefreshTime != default)
            {
                var el = DateTime.Now - _lastRefreshTime;
                string t = el.TotalSeconds < 60 ? $"{el.Seconds}초 전" : $"{(int)el.TotalMinutes}분 전";
                EditorGUILayout.LabelField(t, new GUIStyle(EditorStyles.miniLabel)
                    { normal = { textColor = COL_MUTED }, alignment = TextAnchor.MiddleRight }, GUILayout.Width(50));
            }

            EditorGUILayout.EndHorizontal();
        }

        // ─── 리소스 목록 ────────────────────────────

        void DrawResourceList()
        {
            if (!DrawSectionFoldout(ref _foldResources, $"Resources ({_resources.Count})", TabColor)) return;
            BeginBody();

            // 타입 필터 탭
            var filterColors = new[] {
                TabColor,
                new Color(0.95f, 0.75f, 0.20f),
                new Color(0.40f, 0.80f, 0.95f),
                new Color(0.70f, 0.55f, 0.95f)
            };
            _filterTypeIdx = DrawStyledTabs(FILTER_LABELS, _filterTypeIdx, filterColors);

            var filtered = GetFilteredResources();

            if (filtered.Count == 0)
            {
                EditorGUILayout.LabelField("리소스 없음", new GUIStyle(EditorStyles.centeredGreyMiniLabel));
            }
            else
            {
                _columns.DrawHeader();
                for (int i = 0; i < filtered.Count; i++)
                    DrawResourceRow(filtered[i], i);
            }

            EndBody();
        }

        List<EconomyResource> GetFilteredResources()
        {
            if (_filterTypeIdx == 0) return _resources;
            return _filterTypeIdx switch
            {
                1 => _resources.Where(r => r.Type == ResourceType.Currency).ToList(),
                2 => _resources.Where(r => r.Type == ResourceType.InventoryItem).ToList(),
                3 => _resources.Where(r => r.Type is ResourceType.VirtualPurchase or ResourceType.RealMoneyPurchase).ToList(),
                _ => _resources
            };
        }

        void DrawResourceRow(EconomyResource res, int index)
        {
            var bg = res.IsDirty ? new Color(0.25f, 0.22f, 0.12f) : (index % 2 == 0 ? BG_CARD : BG_SECTION);
            EditorGUILayout.BeginVertical(GetBgStyle(bg));

            // 메인 행
            EditorGUILayout.BeginHorizontal(GUILayout.Height(18));

            // 상태
            string icon; Color iconColor;
            switch (res.Status)
            {
                case SyncState.Synced: icon = "●"; iconColor = COL_SUCCESS; break;
                case SyncState.LocalOnly: icon = "○"; iconColor = COL_WARN; break;
                default: icon = "☁"; iconColor = COL_MUTED; break;
            }
            EditorGUILayout.LabelField(icon, new GUIStyle(EditorStyles.label)
                { normal = { textColor = iconColor }, alignment = TextAnchor.MiddleCenter, fontSize = 13 },
                GUILayout.Width(_columns.GetWidth(COL_STATUS)));

            // ID
            DrawCellLabel(res.Id, _columns.GetWidth(COL_ID));

            // 이름 (편집 가능)
            if (res.Status != SyncState.ServerOnly)
            {
                string newName = EditorGUILayout.TextField(res.Name ?? "");
                if (newName != (res.Name ?? "")) { res.Name = newName; res.IsDirty = true; }
            }
            else
                DrawCellLabel(res.Name ?? "", 0);

            // 타입
            DrawCellLabel(TYPE_LABELS[(int)res.Type], _columns.GetWidth(COL_TYPE),
                res.Type switch { ResourceType.Currency => new Color(0.95f, 0.75f, 0.20f),
                    ResourceType.InventoryItem => new Color(0.40f, 0.80f, 0.95f),
                    _ => new Color(0.70f, 0.55f, 0.95f) });

            // 액션
            bool hasPurchaseDetail = res.Type is ResourceType.VirtualPurchase or ResourceType.RealMoneyPurchase;
            if (hasPurchaseDetail && res.Status != SyncState.ServerOnly)
            {
                if (GUILayout.Button(res.IsExpanded ? "▾" : "▸", EditorStyles.miniButton,
                    GUILayout.Width(18), GUILayout.Height(16)))
                    res.IsExpanded = !res.IsExpanded;
            }

            if (res.Status == SyncState.ServerOnly)
            {
                if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(18), GUILayout.Height(16)))
                    if (EditorUtility.DisplayDialog("삭제", $"서버에서 '{res.Id}'를 삭제하시겠습니까?", "삭제", "취소"))
                        DeleteServerResource(res.Id);
            }
            else
            {
                if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(18), GUILayout.Height(16)))
                    if (EditorUtility.DisplayDialog("삭제", $"'{res.Id}' 파일을 삭제하시겠습니까?", "삭제", "취소"))
                        DeleteLocalResource(res);
            }

            EditorGUILayout.EndHorizontal();

            // 인라인 편집
            if (res.Status != SyncState.ServerOnly)
                DrawInlineEdit(res);

            EditorGUILayout.EndVertical();
        }

        // ─── 인라인 편집 ────────────────────────────

        void DrawInlineEdit(EconomyResource res)
        {
            switch (res.Type)
            {
                case ResourceType.Currency:
                    DrawCurrencyFields(res);
                    break;
                case ResourceType.VirtualPurchase:
                    if (res.IsExpanded) DrawPurchaseFields(res);
                    break;
                case ResourceType.RealMoneyPurchase:
                    if (res.IsExpanded) DrawRmpFields(res);
                    break;
            }
        }

        void DrawCurrencyFields(EconomyResource res)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(40);
            EditorGUILayout.LabelField("initial:", new GUIStyle(EditorStyles.miniLabel)
                { normal = { textColor = COL_MUTED } }, GUILayout.Width(42));
            int ni = EditorGUILayout.IntField(res.Initial, GUILayout.Width(50));
            if (ni != res.Initial) { res.Initial = ni; res.IsDirty = true; }

            GUILayout.Space(8);
            EditorGUILayout.LabelField("max:", new GUIStyle(EditorStyles.miniLabel)
                { normal = { textColor = COL_MUTED } }, GUILayout.Width(28));
            int nm = EditorGUILayout.IntField(res.Max, GUILayout.Width(50));
            if (nm != res.Max) { res.Max = nm; res.IsDirty = true; }

            EditorGUILayout.EndHorizontal();
        }

        void DrawPurchaseFields(EconomyResource res)
        {
            var allIds = _resources.Where(r => r.Type is ResourceType.Currency or ResourceType.InventoryItem)
                .Select(r => r.Id).ToArray();

            // Costs
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(40);
            EditorGUILayout.LabelField("costs:", new GUIStyle(EditorStyles.miniLabel)
                { normal = { textColor = COL_WARN }, fontStyle = FontStyle.Bold }, GUILayout.Width(50));
            EditorGUILayout.EndHorizontal();

            DrawCostRewardList(res.Costs, allIds, res);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(94);
            if (GUILayout.Button("+ cost", EditorStyles.miniButton, GUILayout.Width(50), GUILayout.Height(14)))
            {
                res.Costs.Add(new CostReward { ResourceId = allIds.Length > 0 ? allIds[0] : "", Amount = 1 });
                res.IsDirty = true;
            }
            EditorGUILayout.EndHorizontal();

            // Rewards
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(40);
            EditorGUILayout.LabelField("rewards:", new GUIStyle(EditorStyles.miniLabel)
                { normal = { textColor = COL_SUCCESS }, fontStyle = FontStyle.Bold }, GUILayout.Width(55));
            EditorGUILayout.EndHorizontal();

            DrawCostRewardList(res.Rewards, allIds, res);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(94);
            if (GUILayout.Button("+ reward", EditorStyles.miniButton, GUILayout.Width(60), GUILayout.Height(14)))
            {
                res.Rewards.Add(new CostReward { ResourceId = allIds.Length > 0 ? allIds[0] : "", Amount = 1 });
                res.IsDirty = true;
            }
            EditorGUILayout.EndHorizontal();
        }

        void DrawRmpFields(EconomyResource res)
        {
            // Store ID
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(40);
            EditorGUILayout.LabelField("Google Play:", new GUIStyle(EditorStyles.miniLabel)
                { normal = { textColor = COL_MUTED } }, GUILayout.Width(72));
            string ns = EditorGUILayout.TextField(res.StoreId ?? "");
            if (ns != (res.StoreId ?? "")) { res.StoreId = ns; res.IsDirty = true; }
            EditorGUILayout.EndHorizontal();

            // Rewards (동일)
            var allIds = _resources.Where(r => r.Type is ResourceType.Currency or ResourceType.InventoryItem)
                .Select(r => r.Id).ToArray();

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(40);
            EditorGUILayout.LabelField("rewards:", new GUIStyle(EditorStyles.miniLabel)
                { normal = { textColor = COL_SUCCESS }, fontStyle = FontStyle.Bold }, GUILayout.Width(55));
            EditorGUILayout.EndHorizontal();

            DrawCostRewardList(res.Rewards, allIds, res);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(94);
            if (GUILayout.Button("+ reward", EditorStyles.miniButton, GUILayout.Width(60), GUILayout.Height(14)))
            {
                res.Rewards.Add(new CostReward { ResourceId = allIds.Length > 0 ? allIds[0] : "", Amount = 1 });
                res.IsDirty = true;
            }
            EditorGUILayout.EndHorizontal();
        }

        void DrawCostRewardList(List<CostReward> list, string[] allIds, EconomyResource owner)
        {
            for (int i = 0; i < list.Count; i++)
            {
                var cr = list[i];
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(94);

                // ResourceId 드롭다운
                int curIdx = Array.IndexOf(allIds, cr.ResourceId);
                if (curIdx < 0) curIdx = 0;
                if (allIds.Length > 0)
                {
                    int newIdx = EditorGUILayout.Popup(curIdx, allIds, GUILayout.Width(120));
                    if (newIdx != curIdx) { cr.ResourceId = allIds[newIdx]; owner.IsDirty = true; }
                }
                else
                {
                    cr.ResourceId = EditorGUILayout.TextField(cr.ResourceId, GUILayout.Width(120));
                }

                EditorGUILayout.LabelField("×", new GUIStyle(EditorStyles.miniLabel)
                    { alignment = TextAnchor.MiddleCenter, normal = { textColor = COL_MUTED } }, GUILayout.Width(14));

                int na = EditorGUILayout.IntField(cr.Amount, GUILayout.Width(50));
                if (na != cr.Amount) { cr.Amount = na; owner.IsDirty = true; }

                list[i] = cr;

                if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(18), GUILayout.Height(14)))
                { list.RemoveAt(i); owner.IsDirty = true; i--; }

                EditorGUILayout.EndHorizontal();
            }
        }

        // ─── 저장 ──────────────────────────────────

        void SaveAllDirty()
        {
            foreach (var res in _resources.Where(r => r.IsDirty && r.FilePath != null))
                SaveResourceFile(res);
            _lastSuccess = "저장 완료";
        }

        void SaveResourceFile(EconomyResource res)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");

            string schema = res.Type switch
            {
                ResourceType.Currency => "economy-currency",
                ResourceType.InventoryItem => "economy-inventory",
                ResourceType.VirtualPurchase => "economy-virtual-purchase",
                ResourceType.RealMoneyPurchase => "economy-real-purchase",
                _ => "economy-currency"
            };
            sb.AppendLine($"  \"$schema\": \"https://ugs-config-schemas.unity3d.com/v1/economy/{schema}.schema.json\",");

            switch (res.Type)
            {
                case ResourceType.Currency:
                    sb.AppendLine($"  \"initial\": {res.Initial},");
                    sb.AppendLine($"  \"max\": {res.Max},");
                    break;
                case ResourceType.VirtualPurchase:
                    sb.Append("  \"costs\": [");
                    sb.Append(string.Join(",", res.Costs.Select(c => $"{{\"resourceId\":\"{c.ResourceId}\",\"amount\":{c.Amount}}}")));
                    sb.AppendLine("],");
                    sb.Append("  \"rewards\": [");
                    sb.Append(string.Join(",", res.Rewards.Select(r => $"{{\"resourceId\":\"{r.ResourceId}\",\"amount\":{r.Amount}}}")));
                    sb.AppendLine("],");
                    break;
                case ResourceType.RealMoneyPurchase:
                    sb.AppendLine($"  \"storeIdentifiers\": {{\"googlePlayStore\": \"{res.StoreId ?? ""}\"}},");
                    sb.Append("  \"rewards\": [");
                    sb.Append(string.Join(",", res.Rewards.Select(r => $"{{\"resourceId\":\"{r.ResourceId}\",\"amount\":{r.Amount}}}")));
                    sb.AppendLine("],");
                    break;
            }

            sb.AppendLine($"  \"name\": \"{res.Name ?? res.Id}\"");
            sb.AppendLine("}");

            File.WriteAllText(res.FilePath, sb.ToString());
            res.IsDirty = false;
        }

        // ─── 새 리소스 ──────────────────────────────

        void DrawCreateSection()
        {
            if (!DrawSectionFoldout(ref _foldCreate, "새 리소스", COL_WARN)) return;
            BeginBody();

            EditorGUILayout.BeginHorizontal(GUILayout.Height(18));
            EditorGUILayout.LabelField("타입:", GUILayout.Width(35));
            _newTypeIdx = EditorGUILayout.Popup(_newTypeIdx, CREATE_LABELS);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal(GUILayout.Height(18));
            EditorGUILayout.LabelField("ID:", GUILayout.Width(35));
            _newId = EditorGUILayout.TextField(_newId);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal(GUILayout.Height(18));
            EditorGUILayout.LabelField("이름:", GUILayout.Width(35));
            _newName = EditorGUILayout.TextField(_newName);
            EditorGUILayout.EndHorizontal();

            if (_newTypeIdx == 0) // Currency
            {
                EditorGUILayout.BeginHorizontal(GUILayout.Height(18));
                EditorGUILayout.LabelField("초기값:", GUILayout.Width(42));
                _newInitial = EditorGUILayout.IntField(_newInitial, GUILayout.Width(60));
                EditorGUILayout.LabelField("최대:", GUILayout.Width(30));
                _newMax = EditorGUILayout.IntField(_newMax, GUILayout.Width(60));
                EditorGUILayout.EndHorizontal();
            }

            GUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            bool isDup = _resources.Any(r => r.Id.Equals(_newId?.Trim() ?? "", StringComparison.OrdinalIgnoreCase));
            GUI.enabled = !string.IsNullOrWhiteSpace(_newId) && !isDup;
            if (GUILayout.Button("+ 생성", EditorStyles.miniButton, GUILayout.Width(50), GUILayout.Height(18)))
                CreateResource();
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            if (isDup && !string.IsNullOrWhiteSpace(_newId))
                EditorGUILayout.LabelField("이미 존재하는 ID입니다",
                    new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = COL_ERROR } });

            EndBody();
        }

        void CreateResource()
        {
            if (!Directory.Exists(_economyDir))
                Directory.CreateDirectory(_economyDir);

            string id = _newId.Trim().ToUpper();
            string ext = TYPE_EXTENSIONS[_newTypeIdx];
            string filePath = Path.Combine(_economyDir, $"{id}{ext}");

            var res = new EconomyResource
            {
                Id = id,
                Name = string.IsNullOrEmpty(_newName) ? id : _newName.Trim(),
                Type = (ResourceType)_newTypeIdx,
                Status = SyncState.LocalOnly,
                FilePath = filePath,
                Initial = _newInitial,
                Max = _newMax
            };

            SaveResourceFile(res);
            AssetDatabase.Refresh();

            _newId = "";
            _newName = "";
            _newInitial = 0;
            _newMax = 0;
            ScanLocalFiles();
        }

        // ─── Deploy + Publish ────────────────────────

        void DeployAndPublish()
        {
            if (!Directory.Exists(_economyDir))
            {
                _lastError = "Economy 폴더가 없습니다.";
                return;
            }

            _isLoading = true;
            _lastError = null;
            _lastSuccess = null;
            string dir = _economyDir.Replace('\\', '/');

            UGSCliRunner.RunAsync($"deploy \"{dir}\" -s economy", deployResult =>
            {
                if (!deployResult.Success)
                {
                    _isLoading = false;
                    var sb = new StringBuilder($"Deploy 실패 (exit {deployResult.ExitCode})");
                    if (!string.IsNullOrEmpty(deployResult.Error)) sb.Append($"\n{deployResult.Error}");
                    if (!string.IsNullOrEmpty(deployResult.Output)) sb.Append($"\n{deployResult.Output}");
                    _lastError = sb.ToString();
                    return;
                }

                // Auto publish
                UGSCliRunner.RunAsync("economy publish", pubResult =>
                {
                    _isLoading = false;
                    if (pubResult.Success)
                    {
                        _lastSuccess = "Deploy + Publish 완료"
                            + (!string.IsNullOrEmpty(deployResult.Output) ? $"\n{deployResult.Output}" : "");
                        FetchData();
                    }
                    else
                    {
                        _lastError = $"Deploy 성공, Publish 실패: {pubResult.Error}";
                    }
                });
            });
        }

        // ─── 삭제 ──────────────────────────────────

        void DeleteServerResource(string id)
        {
            _isLoading = true;
            UGSCliRunner.RunAsync($"economy delete {id}", result =>
            {
                _isLoading = false;
                if (result.Success) { _lastSuccess = $"'{id}' 삭제 완료"; FetchData(); }
                else _lastError = $"삭제 실패: {result.Error}";
            });
        }

        void DeleteLocalResource(EconomyResource res)
        {
            if (!string.IsNullOrEmpty(res.FilePath) && File.Exists(res.FilePath))
            {
                File.Delete(res.FilePath);
                string meta = res.FilePath + ".meta";
                if (File.Exists(meta)) File.Delete(meta);
            }
            AssetDatabase.Refresh();
            ScanLocalFiles();
        }

        // ─── JSON 유틸 ──────────────────────────────

        static string ExtractStr(string json, string field)
        {
            string key = $"\"{field}\"";
            int ki = json.IndexOf(key, StringComparison.Ordinal);
            if (ki < 0) return "";
            int ci = json.IndexOf(':', ki + key.Length);
            if (ci < 0) return "";
            int qs = json.IndexOf('"', ci + 1);
            if (qs < 0) return "";
            int qe = json.IndexOf('"', qs + 1);
            return qe > qs ? json.Substring(qs + 1, qe - qs - 1) : "";
        }

        static int ExtractInt(string json, string field)
        {
            string key = $"\"{field}\"";
            int ki = json.IndexOf(key, StringComparison.Ordinal);
            if (ki < 0) return 0;
            int ci = json.IndexOf(':', ki + key.Length);
            if (ci < 0) return 0;
            int start = ci + 1;
            while (start < json.Length && (json[start] == ' ' || json[start] == '\t')) start++;
            int end = start;
            while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '-')) end++;
            return int.TryParse(json.Substring(start, end - start), out int v) ? v : 0;
        }

        static string ExtractNestedStr(string json, string obj, string field)
        {
            string key = $"\"{obj}\"";
            int ki = json.IndexOf(key, StringComparison.Ordinal);
            if (ki < 0) return "";
            int bs = json.IndexOf('{', ki);
            if (bs < 0) return "";
            int be = JsonFindBrace(json, bs);
            string block = json.Substring(bs, be - bs + 1);
            return ExtractStr(block, field);
        }

        static List<CostReward> ExtractCostRewardArray(string json, string field)
        {
            var list = new List<CostReward>();
            string arr = ExtractArray(json, field);
            if (string.IsNullOrEmpty(arr)) return list;

            int sf = 0;
            while (true)
            {
                int os = arr.IndexOf('{', sf); if (os < 0) break;
                int oe = arr.IndexOf('}', os); if (oe < 0) break;
                string obj = arr.Substring(os, oe - os + 1);
                list.Add(new CostReward
                {
                    ResourceId = ExtractStr(obj, "resourceId"),
                    Amount = ExtractInt(obj, "amount")
                });
                sf = oe + 1;
            }
            return list;
        }

        static string ExtractArray(string json, string field)
        {
            string key = $"\"{field}\"";
            int ki = json.IndexOf(key, StringComparison.Ordinal);
            if (ki < 0) return "";
            int as_ = json.IndexOf('[', ki);
            if (as_ < 0) return "";
            int ae = JsonFindBracket(json, as_);
            return json.Substring(as_, ae - as_ + 1);
        }

        static string ExtractObject(string json, string field)
        {
            string key = $"\"{field}\"";
            int ki = json.IndexOf(key, StringComparison.Ordinal);
            if (ki < 0) return "";
            int bs = json.IndexOf('{', ki + key.Length);
            if (bs < 0) return "";
            int be = JsonFindBrace(json, bs);
            return json.Substring(bs, be - bs + 1);
        }

    }
}
#endif
