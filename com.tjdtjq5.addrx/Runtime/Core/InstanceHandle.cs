using System;

namespace Tjdtjq5.AddrX
{
    /// <summary>
    /// 이미 생성된 값(인스턴스 등)을 감싸고, Dispose 시 커스텀 해제 동작을 실행하는 핸들.
    /// 풀링/DI를 끼운 <c>AddrX.InstantiateAsync</c>가 반환하며, Dispose는 등록된
    /// <see cref="IAddrXInstantiator"/>의 파괴 경로(예: Pool.Push 또는 Object.Destroy)로 라우팅된다.
    /// </summary>
    public sealed class InstanceHandle<T> : SafeHandle<T>
    {
        readonly T _value;
        readonly Action _release;

        internal InstanceHandle(T value, Action release, object key = null) : base(key)
        {
            _value = value;
            _release = release;
        }

        public override T Value
        {
            get
            {
                if (_released)
                    throw new ObjectDisposedException(
                        nameof(InstanceHandle<T>), "핸들이 이미 해제되었습니다.");
                return _value;
            }
        }

        // 주의: _value는 UnityEngine.Object일 수 있어 == 비교가 메인스레드 전용이다.
        // 파이널라이저(별도 스레드)에서 호출하지 말 것.
        public override bool IsValid => !_released && _value != null;

        public override bool IsReady => IsValid;

        public override float Progress => _released ? 0f : 1f;

        public override HandleStatus Status =>
            _released ? HandleStatus.None : HandleStatus.Succeeded;

        protected override void ReleaseCore()
        {
            _release?.Invoke();
        }

        // 파이널라이저 없음 — 인스턴스(GameObject) 수명 누수는:
        //  (1) 파이널라이저 스레드에서 Unity object 접근이 위험하고,
        //  (2) 씬 언로드 시 Unity가 GO를 일괄 파괴하면 오경고가 쏟아진다.
        // 따라서 인스턴스 누수는 태그/트래커 기반으로 추적한다.
    }
}
