using VContainer;
using VContainer.Unity;

namespace Tjdtjq5.UIFramework
{
    /// <summary>
    /// UI Framework 전용 LifetimeScope.
    /// 씬에 GO로 배치하면 UIManager를 자동 등록합니다.
    /// 프로젝트의 GameLifetimeScope의 자식으로 배치하세요.
    /// </summary>
    public class UILifetimeScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            builder.RegisterComponentInHierarchy<UIManager>();
            builder.RegisterComponentInHierarchy<UIDialog>();
        }
    }
}
