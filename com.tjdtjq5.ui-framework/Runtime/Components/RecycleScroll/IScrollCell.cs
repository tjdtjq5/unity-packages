namespace Tjdtjq5.UIFramework
{
    /// <summary>
    /// RecycleScrollView 셀 계약.
    /// cellPrefab에 이 인터페이스를 구현한 MonoBehaviour가 있어야 한다.
    /// </summary>
    public interface IScrollCell
    {
        /// <summary>
        /// 셀에 데이터를 바인딩한다. index는 전체 데이터 기준.
        /// </summary>
        void OnUpdateCell(int index);

        /// <summary>
        /// 셀이 재활용되기 직전 호출. 리소스 정리용.
        /// </summary>
        void OnRecycled();
    }
}
