using System;
using Tjdtjq5.EditorToolkit;
using UnityEngine;
using UnityEngine.UI;

namespace Tjdtjq5.UIFramework
{
    /// <summary>
    /// 탭 데이터.
    /// </summary>
    [Serializable]
    public class UITab
    {
        public string name;
        [Required] public Button button;
        [Required] public GameObject content;
        public Image tabImage;
        public Color activeColor = Color.white;
        public Color inactiveColor = new(0.6f, 0.6f, 0.6f);
    }

    /// <summary>
    /// 탭 그룹. 탭 버튼 N개 ↔ 컨텐츠 N개 전환.
    /// 상점, 설정, 도감 등 탭 UI 패턴에 범용 사용.
    /// </summary>
    public class UITabGroup : MonoBehaviour
    {
        [SectionHeader("Tab Group", 0.82f, 0.62f, 1f)]
        [SerializeField] private UITab[] _tabs;
        [SerializeField] private int _defaultTabIndex;

        /// <summary>탭 전환 이벤트. 인덱스 전달.</summary>
        public event Action<int> OnTabChanged;

        /// <summary>현재 선택된 탭 인덱스.</summary>
        public int CurrentIndex { get; private set; } = -1;

        private void Start()
        {
            for (int i = 0; i < _tabs.Length; i++)
            {
                int idx = i;
                if (_tabs[i].button != null)
                    _tabs[i].button.onClick.AddListener(() => SelectTab(idx));
            }

            SelectTab(_defaultTabIndex);
        }

        /// <summary>
        /// 인덱스로 탭 전환.
        /// </summary>
        public void SelectTab(int index)
        {
            if (index < 0 || index >= _tabs.Length) return;
            if (index == CurrentIndex) return;

            CurrentIndex = index;

            for (int i = 0; i < _tabs.Length; i++)
            {
                var tab = _tabs[i];
                bool active = i == index;

                if (tab.content != null)
                    tab.content.SetActive(active);

                if (tab.tabImage != null)
                    tab.tabImage.color = active ? tab.activeColor : tab.inactiveColor;
            }

            OnTabChanged?.Invoke(index);
        }

        /// <summary>
        /// 이름으로 탭 전환.
        /// </summary>
        public void SelectTab(string tabName)
        {
            for (int i = 0; i < _tabs.Length; i++)
            {
                if (_tabs[i].name == tabName)
                {
                    SelectTab(i);
                    return;
                }
            }

            Debug.LogWarning($"[UITabGroup] '{tabName}' 탭을 찾을 수 없습니다: {gameObject.name}", this);
        }
    }
}
