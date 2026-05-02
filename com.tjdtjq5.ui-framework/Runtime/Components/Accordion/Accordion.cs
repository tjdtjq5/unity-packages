using UnityEngine;
using UnityEngine.UI;

namespace Tjdtjq5.UIFramework.Components
{
    /// <summary>
    /// 펼침/접힘 컨테이너. AccordionElement 자식들을 그룹으로 관리.
    /// HorizontalLayoutGroup 또는 VerticalLayoutGroup + ContentSizeFitter + ToggleGroup 필수.
    /// </summary>
    [RequireComponent(typeof(HorizontalOrVerticalLayoutGroup), typeof(ContentSizeFitter), typeof(ToggleGroup))]
    public sealed class Accordion : MonoBehaviour
    {
        public enum Transition
        {
            Instant,
            Tween,
        }

        [SerializeField] private Transition _transition = Transition.Tween;
        [SerializeField] private float _transitionDuration = 0.3f;

        bool _expandVertical = true;

        public Transition TransitionMode => _transition;
        public float TransitionDuration => _transitionDuration;
        public bool ExpandVertical => _expandVertical;

        void Awake()
        {
            // VerticalLayoutGroup 있으면 vertical, HorizontalLayoutGroup 있으면 horizontal
            _expandVertical = GetComponent<HorizontalLayoutGroup>() == null;
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            if (GetComponent<HorizontalLayoutGroup>() == null && GetComponent<VerticalLayoutGroup>() == null)
            {
                Debug.LogError("[Accordion] HorizontalLayoutGroup 또는 VerticalLayoutGroup 필요", this);
            }
        }
#endif
    }
}
