# Loading

- **상태**: stable
- **용도**: 확장 로딩 API -- 배치 로드, 라벨 로드, 인스턴스화, Inspector 컴포넌트 자동 로더

## 의존성

| 폴더 | 관계 |
|------|------|
| `../Core/` | AddrX partial class 확장, SafeHandle 사용 |
| `../Logging/` | 로그 출력 (AddrXLog) |

## 구조

```
Runtime/Loading/
├── AddrX.Loading.cs          # AddrX partial class -- LoadAsync(AssetRef), LoadBatchAsync, InstantiateAsync, LoadByLabelAsync
├── AddressableRefAttribute.cs # [AddressableRef] PropertyAttribute -- Inspector 에셋 피커 연동
└── ComponentLoader.cs        # MonoBehaviour -- Inspector에서 주소 지정하면 Awake 자동 로드, OnDestroy 자동 해제
```

## API

### AddrX (partial class 확장)

| 멤버 | 시그니처 | 설명 |
|------|---------|------|
| 메서드 | `Task<SafeHandle<T>> LoadAsync<T>(AssetReference reference)` | AssetReference로 에셋 로드 |
| 메서드 | `Task<SafeHandle<T>[]> LoadBatchAsync<T>(IList<string> keys, Action<float> onProgress = null)` | 여러 에셋 동시 로드 + 진행률 |
| 메서드 | `Task<SafeHandle<GameObject>> InstantiateAsync(string key, Transform parent = null)` | 에셋 로드 + 인스턴스화 |
| 메서드 | `Task<SafeHandle<T>[]> LoadByLabelAsync<T>(AssetLabelReference labelRef, Action<float> onProgress = null)` | AssetLabelReference로 라벨 로드 |
| 메서드 | `Task<SafeHandle<T>[]> LoadByLabelAsync<T>(string label, Action<float> onProgress = null)` | 라벨 문자열로 에셋 로드 |
| 메서드 | `Task<SafeHandle<T>[]> LoadByLabelAsync<T>(IList<string> labels, MergeMode mergeMode, Action<float> onProgress = null)` | 다중 라벨 조합 로드 (Union/Intersection) |

### AddressableRefAttribute (sealed class : PropertyAttribute)

| 용도 | 설명 |
|------|------|
| `[AddressableRef]` | string 필드에 적용하면 Inspector에서 Addressable 에셋 피커 표시 |

### ComponentLoader (MonoBehaviour)

| 멤버 | 시그니처 | 설명 |
|------|---------|------|
| 프로퍼티 | `Object LoadedAsset` | 로드된 에셋 (로드 전이면 null) |
| 프로퍼티 | `bool IsLoaded` | 로드 완료 여부 |
| 프로퍼티 | `HandleStatus Status` | 현재 상태 |

## 사용 예시

```csharp
// 배치 로드
var keys = new[] { "enemy_00", "enemy_01", "enemy_02" };
var handles = await AddrX.LoadBatchAsync<GameObject>(keys, p => Debug.Log($"{p:P0}"));

// 라벨 로드
var weapons = await AddrX.LoadByLabelAsync<WeaponData>("weapons");

// 다중 라벨 (교집합)
var rareWeapons = await AddrX.LoadByLabelAsync<WeaponData>(
    new[] { "weapons", "rare" }, Addressables.MergeMode.Intersection);

// 인스턴스화
var handle = await AddrX.InstantiateAsync("enemy_prefab", parentTransform);
```

## 주의사항

- `LoadBatchAsync`는 모든 키를 동시에 로드한다 (`Task.WhenAll`). 대량 로드 시 메모리 주의.
- `LoadByLabelAsync` 반환 배열의 각 `SafeHandle`은 개별적으로 Dispose 해야 한다.
- `InstantiateAsync`는 `Addressables.InstantiateAsync`를 래핑하므로 GO 파괴 시 Addressables.ReleaseInstance 대신 SafeHandle.Dispose를 사용해야 한다.
- `ComponentLoader`는 `async void Awake`를 사용. 예외 발생 시 로그만 출력하고 조용히 실패한다.
- `EnsureInitialized()`를 내부적으로 호출하므로 별도 초기화 없이 사용 가능하나, 초기화 실패 시 `InvalidOperationException`이 발생한다.
