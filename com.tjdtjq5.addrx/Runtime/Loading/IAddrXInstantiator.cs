using UnityEngine;

namespace Tjdtjq5.AddrX
{
    /// <summary>
    /// 인스턴스 생성/파괴 전략 확장점. AddrX는 VContainer/풀링을 알지 못하며,
    /// 프로젝트가 부트스트랩에서 <c>AddrX.Instantiator</c>에 구현체를 등록해
    /// 풀링(Pool.Pop/Push)·DI 주입(InjectGameObject) 등을 끼운다.
    /// </summary>
    public interface IAddrXInstantiator
    {
        /// <summary>prefab으로부터 인스턴스를 생성한다. 풀링·DI 주입 등 프로젝트 정책을 적용한다.</summary>
        GameObject Instantiate(GameObject prefab, Transform parent, bool inWorldSpace);

        /// <summary>인스턴스를 파괴하거나 풀에 반환한다.</summary>
        void Destroy(GameObject instance);
    }

    /// <summary>기본 전략: Object.Instantiate / Object.Destroy (풀링·DI 없음).</summary>
    internal sealed class DefaultInstantiator : IAddrXInstantiator
    {
        public GameObject Instantiate(GameObject prefab, Transform parent, bool inWorldSpace)
            => parent != null
                ? Object.Instantiate(prefab, parent, inWorldSpace)
                : Object.Instantiate(prefab);

        public void Destroy(GameObject instance) => Object.Destroy(instance);
    }
}
