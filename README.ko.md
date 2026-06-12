# VMG — Vector Motion Graphics Framework

[English](README.md) · [한국어](README.ko.md)

Unity용 절차적(procedural) 벡터 모션 그래픽 런타임. After Effects의 셰이프 레이어 같은 표현력을, UGUI와 월드 스페이스 양쪽 렌더러에서 Unity Animator / Timeline과 완전히 통합되는 형태로 제공합니다.

## 주요 기능 (0.26.0)

- Path + Node 데이터 모델, 노드별 큐빅 베지어(`inTangent` / `outTangent`) 지원. 모든 모디파이어 적용 전에 미리 테셀레이션됨
- CPU 기반 절차적 메시 생성
- **ShapeStack** — 최대 4개의 프리미티브 셰이프를 arc-length 리샘플링과 슬롯별 강도(intensity) 가중치로 블렌딩. 기존 "단일 셰이프 + Morph 모디파이어" 구조를 대체하며 모든 슬롯이 대칭적임
- 스트로크(Stroke)
  - Inner / Center / Outer 정렬
  - Cap: Butt / Square / Round
  - Join: Miter (limit 지원) / Bevel / Round
- 필(Fill) — 자체 ear-clipping 삼각화기 (오목 다각형 안전)
- **Depth (3D 두께)** — `VectorSpriteRenderer` 전용. 필을 Z축 방향으로 extrude하며 Front / Center / Back 피벗 정렬 선택 가능. Vertex normal을 명시해서 lit 머티리얼이 측면 음영을 처리함. **3D URP 렌더러(Forward / Forward+ / Deferred)와 Opaque 머티리얼이 필요함** — 2D Renderer나 Transparent 머티리얼에서는 광원/오클루전이 정상 작동하지 않음
- 모디파이어 (고정 순서: RoundCorner → Trim)
  - Round Corner — 인접 코너 클램핑 포함 실제 경로 레벨 라운딩
  - Trim Path — 닫힌 경로 wrap 지원, 열린 경로 안전 클램프(오프셋이 끝을 넘어도 깜빡임 없음)
- 프리미티브: Circle, Ellipse, Rectangle, Rounded Rectangle, Polygon, Free Path
- UGUI 렌더러 (`VectorImageGraphic`) — `MaskableGraphic`이라 `Mask` / `RectMask2D`와 연동
- 월드 렌더러 (`VectorSpriteRenderer`) — `MeshFilter` + `MeshRenderer`
- SVG 임포트 — 프로젝트에 `.svg` 파일을 드롭하면 ScriptedImporter가 `VMGShapeAsset`을 생성, 양쪽 렌더러 모두 참조 가능. path `d` grammar 전체, 기본 도형, viewBox, transform, fill/stroke 스타일 모두 지원
- SceneView 핸들 — 활성 스택 슬롯의 FreePath 노드와 베지어 탄젠트를 직접 드래그해서 편집. SceneView 좌상단의 작은 오버레이로 핸들이 어느 슬롯을 대상으로 할지 선택
- 렌더러별 커스텀 셰이더 머티리얼 / 텍스처
  - UGUI: `Material` 슬롯 + `Texture` (`Graphic.mainTexture` 바인딩, 머티리얼 인스턴싱 없음)
  - World: `Material` 슬롯 + `Texture` (`MaterialPropertyBlock` 바인딩, 공유 머티리얼 보존)
  - 메시 UV는 렌더러 footprint에 대해 `[0,1]`로 정규화됨
- `VectorSpriteRenderer`의 Sorting Layer / Order in Layer 필드 (`SpriteRenderer`와 동일한 사용성)
- 모든 애니메이션 가능 파라미터가 `[SerializeField]`로 노출되어 AnimationClip / Timeline에서 키프레임 가능
- 에디터 메뉴
  - `GameObject ▸ UI ▸ Vector Image`
  - `GameObject ▸ 2D Object ▸ Vector Sprite Renderer`

## 설치

`Packages/manifest.json`에 추가:

```json
"com.ilsang.vmg": "https://github.com/ilsang93/unity-vector-motion-graphics-framework.git"
```

또는 `Packages/com.ilsang.vmg`에 로컬/임베디드 패키지로 복사해서 사용.

## 샘플

Package Manager ▸ VMG ▸ Samples ▸ Basic Shapes에서 임포트 (DOTween이 설정되어 있으면 Tween Integration도).

## DOTween / UniTask 연동

프로젝트에 DOTween이 설치되어 있고 `VMG_DOTWEEN` 스크립팅 디파인 심볼이 정의되어 있을 때 (UPM 설치 시 자동 정의됨), 별도 어셈블리에서 단축 익스텐션을 제공합니다:

```csharp
using VMG.Tween;

vectorImage.DOFade(0f, 0.4f);
vectorImage.DOTrim(1f, 0.8f).SetEase(Ease.OutCubic);
vectorImage.DOStrokeColor(Color.red, 0.5f);
await vectorImage.DOSize(new Vector2(300, 300), 0.6f).AsyncWaitForCompletion();

// 두 셰이프 간 크로스페이드:
vectorImage.DOSlotIntensity(1, 1f, 0.8f);    // 슬롯 1을 켜기
vectorImage.DOSlotIntensity(0, 0f, 0.8f);    // 슬롯 0을 끄기
```

DOTween이 없는 프로젝트에는 영향을 주지 않습니다 (하드 의존성 없음). 전체 surface는 `Samples~/TweenIntegration/README.md` 참고.

## 독립형 애니메이션 (VMGAnimator)

아래의 Unity AnimationClip / Timeline 경로 외에도, VMG는 `PlayableDirector`나 Unity Timeline에 의존하지 않는 자체 애니메이터를 함께 제공합니다. 세 가지 작성 방식이 모두 같은 엔진을 구동합니다:

- **`VMGAnimationClip` + VMGAnimator** — ScriptableObject 클립 에셋, 전용 타임라인 윈도우에서 편집. 트랙별 키와 ease, 다중 타겟, 이벤트, baseline 복원 지원.
- **코드 API (anime.js 스타일 fluent 빌더)** — `VMGFx.Animate(target).To(...).Duration(...).Ease(...).Play()`, 시퀀싱용 `VMGFx.Timeline()` (상대 위치 `"+=0.2"`, `"<"`, `"-=F"`), 타겟별 오프셋용 `VMGFx.Stagger(targets, ...)`, spring / motion-path / function-value 채널.
- **`.vmgfx` DSL** — 평문 스크립트 (`add`, `animate`, `timeline`, `keyframes`, `stagger` 등) 가 같은 엔진으로 컴파일됨. `.vmgfx` 파일(또는 임의의 TextAsset)을 `VMGAnimator.script`에 할당하면 enable 시점에 하위 계층이 빌드됨. 1회 재생 vs 무한 재생을 위한 `playOnEnable` / `loopScript` 토글 제공.

### CSS `@keyframes` 임포터

`VMG.Animation.Serialization.VMGCssKeyframes.Translate(css, out warnings)` — self-contained CSS 키프레임 애니메이션을 `.vmgfx` 텍스트로 변환. AE / Figma / Bodymovin의 CSS export를 대상으로 설계됨 — `transform`, `opacity`, 색상 / 테두리 채널과 W3C 스펙 cubic-bezier easing 매핑 지원. 에디터 진입점:

- `Tools ▸ VMG ▸ Import CSS @keyframes…` — 파일 다이얼로그
- `Tools ▸ VMG ▸ CSS → VMGFx Window` — 붙여넣기 윈도우

의도적으로 제외된 범위: HTML 동반 입력, CSS cascade, pseudo-class 상태, element 별 custom property 기반 stagger. 야생 데모는 `@keyframes` 핵심만 추려서 임포트하고, element 단위 효과는 `VMGFx.Stagger`와 타임라인 상태로 재구성하는 흐름을 권장.

## 애니메이션 지원

VMG의 설계 목표는 "인스펙터에서 편집할 수 있는 모든 파라미터는 `AnimationClip` / Timeline에서도 키프레임 가능하다"입니다. 두 렌더러 모두 매 프레임 dirty 마크 처리되므로 (UGUI는 `LateUpdate`, World는 `Update`) Animator가 쓰는 값이 항상 메시에 반영됩니다.

### AnimationClip에서 키프레임 가능

모든 인스펙터 필드가 struct 멤버로 노출되어 있어서 Animation 윈도우의 "Add Property" 트리가 안쪽까지 들어갈 수 있습니다. 전체 목록:

- **ShapeStack** — `resampleCount`, 그리고 4개 슬롯:
  - `Slot0..Slot3.intensity` — 블렌드 가중치 (0이면 비활성)
  - `Slot0..Slot3.shape.*` — PrimitiveShapeSource 전체 surface
- **프리미티브 셰이프 (슬롯별)** — `kind`, `center.x/y`, `size.x/y`, `sides`, `cornerRadii.x/y`, `circleSegments`, `bezierSamplesPerSegment`, `freeClosed`, `activeNodeCount`
- **FreePath 노드 (슬롯별)** — 플랫 슬롯별로 `Node00.position.x/y`, `Node00.inTangent.x/y`, `Node00.outTangent.x/y`, `Node00.type` ... `Node63`까지. Animation 윈도우에서 직접 바인딩하거나, Record가 켜진 상태에서 SceneView 핸들을 드래그하면 자동으로 플레이헤드 위치에 키프레임이 생성됨
- **Stroke** — `enabled`, `color.rgba`, `width`, `alignment`, `cap`, `join`, `miterLimit`
- **Fill** — `enabled`, `color.rgba`
- **모디파이어** — `RoundCornerModifier`와 `TrimPathModifier`의 모든 직렬화 필드 (각자의 `enabled` 플래그 포함 — 클립 중간에 모디파이어를 켜고 끌 수 있음)
- **UGUI 렌더러** — `FitToRect`, `Graphic.color`
- **월드 렌더러** — `Tint`, `SvgUnitsPerWorldUnit`, `SortingLayerID`, `SortingOrder`
- **Depth (월드 렌더러 전용)** — `Depth.enabled`, `Depth.thickness`, `Depth.alignment`

### 다중 셰이프 블렌딩

ShapeStack이 기존 PathMorphModifier를 대체:

1. "출발" 셰이프를 슬롯 0에 (intensity 1)
2. "도착" 셰이프를 슬롯 1에 (intensity 0)
3. `Slot1.intensity`를 0 → 1로 키프레임 — 렌더러가 두 경로를 arc-length 리샘플링한 뒤 인덱스별로 lerp
4. 선택적으로 슬롯 0의 intensity를 병렬로 페이드아웃하면 클립 끝에서 순수한 도착 셰이프가 됨

4개 슬롯 모두 동등하게 가중치 처리됩니다. "베이스" 슬롯이 따로 없습니다. 3~4개 슬롯을 동시에 활성화하면 매끄러운 N-way 블렌드가 가능.

### FreePath 노드 애니메이션

노드를 평소처럼 편집하면 됩니다 — SceneView 핸들은 모든 드래그를 `SerializedProperty` 경유로 처리하므로, Animation 윈도우의 Record 모드가 자동으로 플레이헤드 위치에 키프레임을 캡처합니다. 별도 동기화 작업이나 평행 surface 없음.

SceneView 좌상단 오버레이로 핸들이 어느 스택 슬롯의 노드를 다룰지 선택합니다. FreePath가 아닌 슬롯을 선택하면 핸들이 표시되지 않습니다 (`kind` 필드가 결정 — Circle, Rectangle 등에는 노드 핸들이 없음).

Unity AnimationClip의 유일한 실제 제약:

- **노드 개수(`activeNodeCount`)는 키프레임 가능하지만**, 클립 중간에 새로 나타나는 슬롯은 그 슬롯 필드에 저장되어 있던 데이터를 그대로 사용하지, 이전 프레임의 보이는 노드로부터 매끄럽게 들어오지 않습니다. 시각적 노드 개수가 자연스럽게 늘어나야 하는 트랜지션(삼각형 → 오각형)에는 각 셰이프를 별도 ShapeStack 슬롯에 두고 intensity를 애니메이션하는 것을 권장.

### AnimationClip에서 키프레임 불가

| 필드 | 이유 | 우회 방법 |
|---|---|---|
| `Material`, `Texture`, `SvgAsset` (모든 Object 참조) | AnimationClip의 Object 트랙은 PPtr 전용이며 이 슬롯들에 노출되지 않음 | 스크립트로 교체 (`AnimationEvent` 콜백 또는 Timeline `Signal`) |
| FreePath 노드 순서 변경 | 슬롯 인덱스가 키프레임 채널이므로, 이름 변경/순서 변경은 바인딩을 깨뜨림 | 끝에서만 추가/제거 (인스펙터의 +/- 버튼) |
