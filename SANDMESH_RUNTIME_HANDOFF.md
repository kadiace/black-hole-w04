# SandMesh 런타임 모래 시스템 변경 설명

이 문서는 기존 `SandMesh` 작성자에게 전달하기 위한 구현 설명이다. 기존 마우스 조형과 모래 완화 기능을 유지하면서 다음 기능을 추가했다.

- 기존 SandMesh 위로 모래가 떨어지면 해당 위치의 높이를 올림
- SandMesh가 없는 바닥에 모래가 떨어지면 런타임 SandMesh 패치를 생성
- 메시 가장자리의 모래가 바닥으로 흘러내림
- Rigidbody 낙하 오브젝트와 SandMesh를 연결하는 `SandVolumeDeposit` 컴포넌트
- 기능을 바로 확인할 수 있는 독립 테스트 씬

## 변경 파일

- `Assets/Scripts/KSJ/SandMesh.cs`
- `Assets/Scripts/KSJ/MeshMake.cs`
- `Assets/Scripts/KSJ/SandVolumeDeposit.cs`
- `Assets/Scripts/KSJ/SandMeshRuntimeTestScene.cs`
- `Assets/Scenes/SandMeshRuntimeTest.unity`

전체 런타임 흐름은 다음과 같다.

```text
Rigidbody 모래 오브젝트 충돌
        ↓
SandVolumeDeposit
        ↓
SandMesh.AddSandAtWorld(worldPosition, amount)
        ↓
기존 SandMesh 영역인가?
   ├─ 예: 해당 메시 높이맵에 모래 추가
   └─ 아니오: 바닥 Raycast 후 SandMesh Runtime Patch 생성
        ↓
높이맵 갱신
        ↓
경사 완화 및 가장자리 흘러내림
        ↓
Mesh / MeshCollider 갱신
```

## SandMesh의 초기화와 메시 관리

기존 코드는 `Start()`에서 바로 `MeshFilter.mesh`를 가져왔다. 현재는 `EnsureInitialized()`에서 필요한 컴포넌트와 배열을 한 번만 준비한다.

초기화 대상은 다음과 같다.

- `MeshFilter`
- `MeshCollider`
- `MeshRenderer`
- 원본 버텍스 배열
- 수정용 버텍스 배열
- 흐름 방향 배열
- 높이 변화 누적 배열
- 메시의 로컬 X/Z 최소·최대 범위
- X/Z 버텍스 간격
- 그리드 stride

Play Mode에서는 `MeshFilter.mesh`를 사용해 인스턴스 메시를 수정한다. 따라서 원본 메시 에셋을 직접 수정하지 않는다. Edit Mode에서는 `sharedMesh`를 사용한다.

메시가 수정되면 즉시 메시를 여러 번 쓰는 대신 `m_meshDirty`를 true로 만들고 `RecalculateMesh()`에서 한 번 반영한다. 반영 시 다음을 수행한다.

```csharp
m_mesh.vertices = m_modifiedVerts;
m_mesh.uv2 = m_flowDirections;
m_mesh.RecalculateNormals();
m_mesh.RecalculateBounds();

m_meshCollider.sharedMesh = null;
m_meshCollider.sharedMesh = m_mesh;
```

`MeshCollider.sharedMesh`를 null로 비운 뒤 다시 연결하는 이유는 변형된 메시 콜라이더를 확실히 갱신하기 위해서다.

## 그리드 크기와 버텍스 간격

기존 코드는 `xSize`, `zSize`, `divid`가 항상 정확하다고 가정했다. 현재는 실제 버텍스 수와 설정값이 맞지 않으면 `InferGridDimensions()`가 버텍스 개수에서 그리드 크기를 추정한다.

주요 값은 다음과 같다.

```csharp
m_stride = xSize + 1;
m_spacingX = X 방향 버텍스 간격;
m_spacingZ = Z 방향 버텍스 간격;
```

월드 좌표를 높이맵 셀로 변환하기 위해 로컬 X/Z 경계도 계산한다.

기존 코드의 Z 방향 인접 버텍스 인덱스는 `index + zSize + 1`을 사용했지만, 행 stride는 `xSize + 1`이어야 하므로 현재는 `index + m_stride`를 사용한다.

## 기존 마우스 조형 기능 변경

기존 기능은 유지된다.

- 우클릭: 높이 올리기
- 좌클릭: 높이 낮추기

다음 문제가 보완됐다.

### 입력 장치 null 처리

`Mouse.current` 또는 `Camera.main`이 없는 환경에서도 예외가 발생하지 않도록 확인한다.

### 현재 SandMesh만 조형

레이가 맞은 콜라이더의 부모에서 현재 `SandMesh`를 확인한다. 여러 SandMesh가 있는 경우 다른 메시가 같은 마우스 입력에 반응하지 않는다.

### 로컬 X/Z 기준 브러시

레이캐스트 히트 위치를 SandMesh 로컬 좌표로 변환한 후 X/Z 거리만 비교한다.

```csharp
Vector3 localPoint = transform.InverseTransformPoint(worldPoint);
```

기존에는 제곱 거리와 일반 반지름을 비교하고 있었지만, 현재는 `radius * radius`와 비교한다.

브러시 가장자리에는 선형 falloff를 적용한다.

```csharp
float falloff = 1f - Mathf.Sqrt(distanceSquared) / radius;
```

낮추기 작업은 `floorLocalHeight` 아래로 내려가지 않는다.

## SandRelaxation 변경

`SandRelaxation()`은 일정 경사 이상인 인접 버텍스의 높이를 분배한다.

처리 순서는 다음과 같다.

1. 흐름 방향 초기화
2. 높이 변화 배열 초기화
3. X 방향 인접 셀 비교
4. Z 방향 인접 셀 비교
5. 허용 경사를 넘은 높이 차이 분배
6. 바닥 높이 아래로 내려가지 않도록 제한
7. 가장자리 spill 처리
8. 흐름 방향 정규화
9. 메시 dirty 처리

허용되는 높이 차이는 다음으로 계산한다.

```csharp
float maxSlope = Mathf.Tan(reposeAngle * Mathf.Deg2Rad);
float allowedHeightDifference = maxSlope * spacing;
```

X/Z 방향의 실제 간격을 별도로 사용한다.

기존에는 시뮬레이션 시간이 한 번 실행될 때마다 0으로 초기화됐다. 현재는 누적 시간을 반복 소비한다.

```csharp
while (m_timer >= interval)
{
    SandRelaxation(interval);
    m_timer -= interval;
}
```

따라서 프레임이 잠시 멈췄다가 다시 실행되어도 경과 시간이 유실되지 않는다.

## 메시 가장자리 spill

다음 설정값을 추가했다.

```csharp
private bool spillAtEdges = true;
private float spillRate = 1f;
private float spillHeightThreshold = 0.01f;
private float floorLocalHeight;
```

`ProcessEdgeSpill()`은 네 방향의 경계 버텍스를 검사한다.

- X 최소 경계
- X 최대 경계
- Z 최소 경계
- Z 최대 경계

각 버텍스에서 다음 값을 계산한다.

```csharp
excess = vertexHeight - floorLocalHeight - spillHeightThreshold;
```

초과 높이가 있으면 현재 셀 간격만큼 메시 바깥으로 이동한 위치를 구한다.

```csharp
outsideLocal = localVertex
    + new Vector3(
        edgeDirectionX * m_spacingX,
        0f,
        edgeDirectionZ * m_spacingZ);
```

바깥 위치에서 바닥을 아래 방향으로 Raycast한다. `floorMask`에 포함된 콜라이더만 대상으로 하며 Trigger는 무시한다. 현재 SandMesh 자신의 콜라이더는 제외한다.

바닥이 없으면 해당 방향으로는 spill하지 않는다. 바닥이 발견되면 다음 경로로 모래를 전달한다.

```csharp
AddSandAtWorldInternal(
    targetWorld,
    amount,
    this,
    createPatchOnEmptyFloor
);
```

현재 SandMesh를 `excluded`로 전달하는 이유는 메시 가장자리 바깥 위치가 현재 메시 내부로 다시 판정되는 것을 막기 위해서다.

모래가 실제로 전달되면 원래 경계 버텍스 높이를 줄인다.

```csharp
m_modifiedVerts[index].y = Mathf.Max(
    floorLocalHeight,
    m_modifiedVerts[index].y - consumed
);
```

## 기존 SandMesh 위에 모래 쌓기

`AddSand()`가 특정 SandMesh에 모래를 추가한다.

```csharp
public float AddSand(Vector3 worldPosition, float amount)
```

월드 위치를 로컬 X/Z 위치로 변환하고 현재 메시 범위 안에 있는지 확인한다. Y 좌표는 무시한다. 낙하 오브젝트가 위에서 메시를 맞기 때문이다.

위치를 셀 좌표로 변환한 뒤 주변 네 버텍스에 bilinear 방식으로 높이를 분배한다.

```csharp
m_modifiedVerts[i00].y += amount * (1f - tx) * (1f - tz);
m_modifiedVerts[i10].y += amount * tx * (1f - tz);
m_modifiedVerts[i01].y += amount * (1f - tx) * tz;
m_modifiedVerts[i11].y += amount * tx * tz;
```

따라서 낙하 지점 하나의 버텍스만 튀어 오르지 않고 주변 영역이 부드럽게 올라간다.

반환값은 실제로 받아들인 모래 양이다.

## 월드 좌표 기반 모래 투입 API

외부 시스템은 다음 정적 API를 호출할 수 있다.

```csharp
float accepted = SandMesh.AddSandAtWorld(
    worldPosition,
    amount,
    createPatch: true
);
```

처리 순서는 다음과 같다.

1. 등록된 SandMesh 목록을 검색한다.
2. 위치를 포함하는 기존 SandMesh가 있으면 해당 메시의 `AddSand()`를 호출한다.
3. 기존 메시가 없고 `createPatch`가 true이면 바닥을 Raycast한다.
4. 바닥 위치에 `SandMesh Runtime Patch`를 생성한다.
5. 새 패치에 최초 모래 양을 적재한다.

모든 활성 SandMesh는 `OnEnable()`에서 정적 목록에 들어가고 `OnDisable()` 또는 `OnDestroy()`에서 제거된다.

```csharp
private static readonly List<SandMesh> s_instances;
```

현재는 모든 SandMesh를 순회한다. 패치가 많은 대형 월드에서는 Bounds 검색이나 월드 그리드 인덱스가 필요할 수 있다.

## 런타임 SandMesh 패치 생성

`CreateRuntimePatch()`는 다음 오브젝트를 런타임에 만든다.

- `GameObject`
- `MeshFilter`
- `MeshRenderer`
- `MeshCollider`
- `MeshMake`
- `SandMesh`

패치 생성 시 기존 SandMesh의 다음 설정을 복사한다.

- 패치 크기
- cell size
- 시뮬레이션 간격
- repose angle
- flow rate
- edge spill 설정
- 빈 바닥 패치 생성 허용 여부
- 머티리얼

패치 이름은 다음과 같다.

```text
SandMesh Runtime Patch
```

패치 메시 위치는 바닥 높이보다 `0.002` 위에 배치한다. Z-fighting을 피하기 위한 오프셋이다.

패치는 런타임 GameObject이므로 Play 종료나 씬 재로드 시 사라진다. 현재는 런타임 지형 저장 기능이 없다.

## MeshMake 변경

`MeshMake`는 기존 Perlin Noise 지형 생성 동작을 유지한다.

추가된 필드는 다음과 같다.

```csharp
[SerializeField] private bool usePerlinNoise = true;
[SerializeField] private float baseHeight;
```

기존 씬에서는 `usePerlinNoise`가 true이므로 기존 형태를 유지한다.

런타임 패치에서만 다음 메서드를 호출한다.

```csharp
public void ConfigureRuntime(
    int runtimeXSize,
    int runtimeZSize,
    float cellSize,
    float runtimeBaseHeight
)
```

이 메서드는 다음을 설정한다.

- X/Z 크기
- `Divid = 1f / cellSize`
- 평면 기준 높이
- Perlin Noise 비활성화

이후 메시를 다시 생성한다. 따라서 빈 바닥 패치는 평면으로 생성되고, 이후 모래가 적재된 부분만 높아진다.

추가 변경 사항은 다음과 같다.

- 사용하지 않는 `UnityEngine.UIElements` 제거
- `MeshFilter` null 체크
- `Divid`가 0에 가까울 때 나누기 방지
- 메시 생성 후 `RecalculateNormals()` 호출

## SandVolumeDeposit

`SandVolumeDeposit`는 Rigidbody 낙하 오브젝트와 SandMesh를 연결한다.

필요한 기본 구성은 다음과 같다.

```text
Collider
Rigidbody
SandVolumeDeposit
```

충돌 방식은 두 가지를 지원한다.

- `OnCollisionEnter`
- `OnTriggerEnter`

충돌 지점에서 다음 API를 호출한다.

```csharp
float accepted = SandMesh.AddSandAtWorld(
    collisionPoint,
    sandAmount,
    true
);
```

모래가 실제로 받아들여진 경우에만 한 번 처리하고 기본 설정에서는 오브젝트를 제거한다.

```csharp
m_deposited = true;
Destroy(gameObject);
```

외부 스포너는 다음 메서드로 모래 양을 지정할 수 있다.

```csharp
deposit.SetSandAmount(amount);
```

`destroyAfterDeposit`은 현재 serialized private 필드이므로 Inspector에서는 변경할 수 있지만, 외부 코드용 setter는 아직 없다.

## 테스트 씬

테스트 씬은 `Assets/Scenes/SandMeshRuntimeTest.unity`다.

씬 에셋 자체에는 다음 루트 하나만 저장되어 있다.

```text
SandMesh Runtime Test
└─ SandMeshRuntimeTestScene
```

Play 시 다음 요소를 런타임에 생성한다.

### 테스트 바닥

```text
Test Floor (empty-floor target)
```

- Unity Plane
- scale 2.5
- MeshCollider 포함
- 빈 바닥 패치 생성 테스트용

### 테스트 SandMesh

```text
Test SandMesh Surface
```

설정값은 다음과 같다.

```text
위치: (-8, 0.03, -8)
Grid: 32 x 32
Cell size: 0.5
World size: 약 16 x 16
Simulation interval: 0.1초
```

컴포넌트는 다음과 같다.

- MeshFilter
- MeshRenderer
- MeshCollider
- MeshMake
- SandMesh

머티리얼은 `Resources/KSJ/Mat/SandFlowMat`을 우선 사용하고, 없으면 URP/Lit 또는 Standard 머티리얼을 런타임 생성한다.

### 낙하 오브젝트

총 36개의 Cube를 생성한다.

- Rigidbody
- Collider
- `SandVolumeDeposit`
- mass `0.25`
- Continuous collision detection
- sand amount `0.45`

세 레인으로 나뉜다.

#### 중앙 레인

기존 SandMesh 범위 안에 떨어진다. 메시의 높이가 올라가는지 확인한다.

#### 가장자리 레인

`x = 7.25` 부근에 떨어진다. 메시의 오른쪽 가장자리에서 모래가 쌓이고 바닥으로 spill되는지 확인한다.

#### 빈 바닥 레인

`x = 10 ~ 11.3` 부근에 떨어진다. 기존 SandMesh 바깥이지만 테스트 Plane 위에 있으므로 런타임 패치가 생성된다.

낙하 오브젝트는 Play 시작 시 즉시 생성된다. 실제 Unity 에디터에서는 Rigidbody가 자연스럽게 낙하한다.

## 기존 작성자가 확인해야 할 전제

### 메시 방향

현재 구현은 다음을 전제로 한다.

- 높이는 로컬 Y
- 지형 평면은 로컬 X/Z
- 버텍스 배열은 Z 행 단위이며 X가 빠르게 증가
- stride는 `xSize + 1`

메시가 회전된 상태에서 로컬 Y가 월드 수직과 다르면 높이 계산과 바닥 Raycast 결과가 의도와 달라질 수 있다.

### 바닥 Collider

가장자리 spill과 빈 바닥 패치 생성에는 바닥 Collider가 필요하다.

관련 설정은 다음과 같다.

```csharp
floorMask
floorRayDistance
```

기본 `floorMask`는 모든 레이어다. 실제 게임에서는 바닥 전용 레이어를 만들고 `floorMask`를 제한하는 편이 안전하다.

### 런타임 패치 증가

현재 빈 바닥 위치마다 별도의 `SandMesh Runtime Patch`를 만든다. 패치 병합이나 기존 패치 확장은 구현되어 있지 않다.

넓은 월드에서 지속적으로 모래가 떨어지면 다음 최적화가 필요할 수 있다.

- 월드 그리드 기반 패치 검색
- Bounds 기반 후보 필터링
- 인접 패치 병합
- 먼 패치 제거
- 모래량이 없는 패치 정리

### 런타임 저장

현재 높이 변화와 런타임 패치는 Play 종료 시 사라진다. 런타임 지형을 저장하려면 별도 저장 포맷이 필요하다.

## 검증 결과

확인된 내용은 다음과 같다.

- Unity CLI 연결 정상
- Unity 재컴파일 성공
- `compilationFailed: false`
- 테스트 씬 Play 진입 성공
- 테스트 바닥과 SandMesh 생성 확인
- 낙하 payload 생성 확인
- `SandMesh.AddSandAtWorld()`가 빈 바닥에서 모래를 받아들이는 것 확인
- 기존 SandMesh 1개에서 런타임 패치 생성 후 2개로 증가하는 것 확인
- 최종 콘솔 에러 0개

자동화된 Unity 에디터는 포커스가 없는 상태에서 물리 프레임 진행이 제한되어 시각적인 낙하와 spill 완료까지는 자동 확인하지 못했다. 실제 Unity 창에서 Play하면 물리 처리가 진행된다.

## 별도 확인이 필요한 파일

Git 상태에는 작업 시작 전부터 dirty 상태였던 `Assets/Scenes/KCH.unity`가 별도로 남아 있다. 해당 파일에는 테스트 씬과 무관한 Cube 오브젝트 변경이 있으므로, 커밋할 때 이번 SandMesh 작업과 분리해서 확인해야 한다.
