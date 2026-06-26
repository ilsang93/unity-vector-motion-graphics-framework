# VMG — Vector Motion Graphics Framework

[English](README.md) · [한국어](README.ko.md)

> Unity용 절차적(procedural) **벡터 모션 그래픽** 프레임워크 — After Effects
> 스타일의 셰이프 레이어(path / stroke / fill / trim / round-corner), SVG·
> TextMeshPro 벡터화, 스텐실 마스킹, 독립형 애니메이터를 **UGUI와 월드 스페이스
> 양쪽**에서 줌과 무관한 안티앨리어싱으로 렌더링합니다.

---

## ✨ 핵심 기능 한눈에

| | |
|---|---|
| 🟦 **절차적 셰이프** | Circle, Rectangle, Polygon, Free Path… 4슬롯 **ShapeStack**으로 블렌딩 |
| ✒️ **스트로크 & 필** | 정렬 / cap / join, 오목 다각형 안전 필, **2-stop 그라데이션** |
| 🔠 **벡터 텍스트 (TMP)** | TextMeshPro를 진짜 글리프 **외곽선**으로 렌더 + **WordArt 워프** |
| 🖼️ **SVG 임포트** | `.svg` 드롭 → 렌더 가능한 `VMGShapeAsset` (path, `defs`/`use`, 스타일) |
| ✂️ **스텐실 마스킹** | `VMGMaskGroup` / `Source` / `Client` 기반 다중 소스 동적 마스크 |
| 🌊 **모디파이어** | Round Corner, Trim Path, **AE 스타일 Wiggle** |
| 🎞️ **애니메이터** | AnimationClip/Timeline로 전부 키프레임, **또는** 내장 `VMGAnimator` |
| 🧊 **월드 전용 기능** | 3D **Depth** 두께, **Billboard**, Sorting Layer/Order |
| 🪶 **어떤 줌에서도 선명** | 두 렌더러 모두 SDF 기반 엣지 안티앨리어싱 |

---

## 📦 설치

`Packages/manifest.json`에 추가:

```json
"com.ilsang.vmg": "https://github.com/ilsang93/unity-vector-motion-graphics-framework.git"
```

또는 `Packages/com.ilsang.vmg`에 로컬/임베디드 패키지로 복사.

**요구 사항:** Unity **6000.3+**, uGUI(`com.unity.ugui`). Unity 6에서는 TextMeshPro가
uGUI에 포함되어 있으므로 벡터 텍스트를 위한 별도 의존성이 필요 없습니다.

---

## 🚀 빠른 시작

렌더러는 둘, 셰이프 모델은 동일합니다:

| 렌더러 | 컴포넌트 | 생성 메뉴 |
|---|---|---|
| **UI (캔버스)** | `VectorImageGraphic` | `GameObject ▸ UI (Canvas) ▸ Vector Image` |
| **월드 (3D/2D)** | `VectorSpriteRenderer` | `GameObject ▸ 2D Object ▸ Vector Sprite Renderer` |

1. 위 중 하나를 생성하면 160×160 벡터 원이 만들어집니다.
2. 인스펙터에서 **ShapeStack ▸ Slot 0 ▸ Shape ▸ Kind** 설정 (Circle / Rectangle /
   Rounded Rectangle / Polygon / Free Path).
3. **Fill**과 **Stroke**를 독립적으로 토글, `linear-gradient` 필도 시도해 보세요.
4. 모디파이어(Trim / Round Corner / Wiggle)를 추가하거나 아무 필드나 키프레임 —
   그대로 동작합니다.

---

## 🧩 주요 기능

### 셰이프 — ShapeStack

최대 **4개의 프리미티브 셰이프**를 arc-length 리샘플링과 슬롯별 **intensity**
가중치로 블렌딩합니다 (별도의 "morph" 모디파이어 없이 모든 슬롯이 대칭).
`SlotN.intensity`를 애니메이션하면 셰이프 간 모핑이 됩니다.

- **프리미티브:** Circle, Ellipse, Rectangle, Rounded Rectangle, Polygon, Free Path
- **Free Path:** 노드별 큐빅 베지어(`inTangent` / `outTangent`), Scene 뷰에서 핸들
  드래그로 편집 (작은 오버레이로 핸들 대상 슬롯 선택)

### 스트로크 & 필

- **스트로크** — Inner / Center / Outer 정렬 · cap(Butt / Square / Round) ·
  join(Miter + limit / Bevel / Round)
- **필** — 자체 ear-clipping 삼각화기(오목 다각형 안전), 다중 컨투어 구멍 카빙(even-odd)
- **그라데이션** — fill과 stroke 모두 2-stop **Linear / Radial** 지원, CPU에서
  per-vertex로 베이크(완전 키프레임 가능), fill+stroke 공유 bounds 기준 매핑

### 모디파이어  *(고정 순서: Round Corner → Trim → Wiggle)*

- **Round Corner** — 인접 코너 클램핑 포함 실제 경로 레벨 라운딩
- **Trim Path** — start / end / offset, 닫힌 경로 wrap, 열린 경로 깜빡임 없는 안전 클램프
- **Wiggle** — After Effects 스타일로 *선을 따라* 출렁이는 리플(arc-length 리샘플,
  스파이크 없음), intensity / frequency / spacing / seed 조절

### 벡터 텍스트 (TMP)

**TextMeshPro** 텍스트를 진짜 VMG 벡터 **외곽선**으로 렌더 — 필, 스트로크(테두리),
두께, Wiggle, WordArt 워프. TMP는 순수하게 **레이아웃 엔진**으로만 사용하고
(`DontRender`), 각 글리프의 모양은 폰트의 TrueType(`.ttf`) 외곽선을 직접 파싱해서
가져옵니다.

- `VMG ▸ Rendering ▸ Vector Text (UI, TMP)` — `TextMeshProUGUI`와 연동
- `VMG ▸ Rendering ▸ Vector Text World (TMP)` — 월드 `TextMeshPro`와 연동
- **워프(WordArt):** Arc · Circle · Trapezoid · Wave · **Grid** (Scene 뷰에서
  컨트롤 포인트 핸들 드래그; 모든 포인트가 키프레임 가능)
- **빌드 베이크:** 폰트 바이트가 컴포넌트에 임베드되고 빌드 시 자동 베이크되므로,
  TMP 폰트에 소스 파일 참조가 없어도 플레이어에서 텍스트가 렌더됩니다.
  *(TrueType 전용; CFF/`.otf`는 미지원.)*

### SVG 임포트

프로젝트에 `.svg`를 드롭하면 ScriptedImporter가 두 렌더러 모두 참조할 수 있는
`VMGShapeAsset`을 생성합니다. path `d` grammar 전체, 기본 도형, `viewBox`,
transform, fill/stroke 스타일, **`<defs>`/`<use>`/`<symbol>` 인라이닝**,
**`<style>` 클래스 셀렉터**를 지원합니다.

### 스텐실 마스킹

Unity의 단일 그래픽 `Mask`를 넘어서는 동적·다중 소스 마스크:

- **`VMGMaskGroup`** — 하위 트리에 마스크 영역 정의
- **`VMGMaskSource`** — 마스크 모양을 *기록*하는 그래픽
- **`VMGMaskClient`** — 그 마스크를 통해 *드러나는* 그래픽

여러 소스가 하나의 스텐실 채널로 결합되며(비트 슬롯 풀링), 표준 `Mask` 안에도,
다른 `VMGMaskGroup` 안에도 중첩됩니다(안쪽 영역이 바깥 영역과 교집합). 그룹의
**`Invert`** 를 켜면 소스 *안쪽* 대신 *바깥쪽* 이 드러납니다. `VMGMaskSource` 의
`Show Source` 로 소스를 디버그용으로 표시할 수 있습니다. DSL로도 작성 가능:
`mask <name> [invert] { … }` + `add … in=<maskName>`.

> 커스텀 머티리얼은 표준 UGUI 스텐실 블록(`_Stencil`, `_StencilComp` …)을
> 선언해야 마스킹됩니다 — `VMG/UI/VectorSDF` 또는 `UI/Default` 를 기반으로 하세요.
> 스텐실 블록이 없는 머티리얼은 경고를 출력하고 클리핑 없이 렌더됩니다.
> (월드 스페이스 `Vector Sprite Renderer` 는 스텐실 마스킹 대상이 아닙니다 — UI/Canvas 전용.)

### 선명한 엣지 (SDF 안티앨리어싱)

두 렌더러 모두 signed-distance 채널을 출력하므로, `VMG/UI/VectorSDF` ·
`VMG/World/VectorSDF` 셰이더가 **줌과 무관하게** 약 1px 엣지를 페이드합니다 —
확대하든 축소하든 벡터가 깔끔하게 유지됩니다.

---

## 🌍 월드 렌더러 전용 기능

- **Depth (3D 두께)** — 필을 Z축으로 extrude(Front / Center / Back 피벗), 실제
  vertex normal로 lit 음영 처리. *3D URP 렌더러 + **Opaque** 머티리얼 필요;
  2D Renderer / Transparent에서는 광원·오클루전이 정상 작동하지 않음.*
- **Billboard** (`VMG ▸ Utility ▸ Billboard`) — 카메라 또는 타겟을 바라보기, 축
  제약과 tilt 오프셋 옵션 포함.
- **Sorting** — `Sorting Layer` / `Order in Layer` 필드 (`SpriteRenderer`와 동일).

---

## 🎞️ 애니메이션

### AnimationClip / Timeline에서 키프레임

설계 목표: **모든 인스펙터 필드가 키프레임 가능**. 두 렌더러 모두 매 프레임 dirty로
마크되므로(UGUI `LateUpdate`, World `Update`) Animator가 쓰는 값이 항상 메시에
다시 반영됩니다.

노출 채널: **ShapeStack**(`resampleCount`, 슬롯별 `intensity` 및 셰이프 전체 surface),
**FreePath 노드**(`Node00…Node63` position/tangent — Record 중 핸들을 드래그하면
키프레임 생성), **Stroke / Fill**(그라데이션 포함), **모디파이어**(각 `enabled`
플래그 포함), **Depth**.

**셰이프 모핑:** 각 셰이프를 별도 슬롯에 넣고 intensity(0 ↔ 1)를 키프레임하세요.
4개 슬롯 모두 동등하게 가중되며 "베이스" 슬롯은 없습니다.

> 키프레임 불가: `Material` / `Texture` / `SvgAsset` 오브젝트 참조(→ `AnimationEvent`나
> Timeline `Signal`로 교체), FreePath 노드 순서 변경(→ 인스펙터 +/- 버튼 사용).

### VMGAnimator — 내장, Timeline 의존성 없음

`PlayableDirector` / Unity Timeline이 **필요 없는** 자체 애니메이터. 세 가지
작성 방식이 하나의 엔진을 구동합니다:

- **`VMGAnimationClip`** — ScriptableObject 클립, 전용 타임라인 윈도우에서 편집:
  트랙별 키 + ease, 다중 타겟, 이벤트, baseline 복원, 여러 GameObject에 걸친
  **트랙 그룹**.
- **코드 API (anime.js 스타일):**
  ```csharp
  VMGFx.Animate(target).To(...).Duration(0.4f).Ease(Ease.OutCubic).Play();
  VMGFx.Timeline().Add(a, "+=0.2").Add(b, "<");   // 상대 위치
  VMGFx.Stagger(targets, ...);                    // 타겟별 오프셋
  ```
  여기에 spring, motion-path, function-value 채널까지.
- **`.vmgfx` DSL** — 평문 스크립트(`add`, `animate`, `timeline`, `keyframes`,
  `stagger`, `mask` 등). 에셋을 `VMGAnimator.script`에 할당하면 enable 시 빌드됩니다.
  `playOnEnable` / `loopScript` 토글 포함.

### CSS `@keyframes` 임포터

`VMGCssKeyframes.Translate(css, out warnings)` — self-contained CSS 키프레임
애니메이션을 `.vmgfx` 텍스트로 변환. AE / Figma / Bodymovin export 대상
(`transform`, `opacity`, 색상/테두리, W3C cubic-bezier easing).

- `Tools ▸ VMG ▸ Import CSS @keyframes…` (파일 다이얼로그)
- `Tools ▸ VMG ▸ CSS → VMGFx Window` (붙여넣기)

*제외 범위: HTML 동반 입력, CSS cascade, pseudo-class 상태, element별 custom
property stagger — `@keyframes` 핵심만 추린 뒤 element 효과는 `VMGFx.Stagger`로
재구성하세요.*

---

## 🔌 DOTween 연동 *(선택)*

DOTween이 설치되어 있고 `VMG_DOTWEEN`이 정의되어 있으면(UPM 설치 시 자동 설정),
별도 어셈블리가 fluent 단축 익스텐션을 추가합니다 — 코어에 하드 의존성 없음:

```csharp
using VMG.Tween;

vectorImage.DOFade(0f, 0.4f);
vectorImage.DOTrim(1f, 0.8f).SetEase(Ease.OutCubic);
vectorImage.DOStrokeColor(Color.red, 0.5f);
vectorImage.DOSlotIntensity(1, 1f, 0.8f);   // 셰이프 간 크로스페이드
```

---

## 📚 샘플

**Package Manager ▸ VMG ▸ Samples**에서 임포트:

| 샘플 | 내용 |
|---|---|
| **Basic Shapes** | Trim 스윕, 라운드 사각형, 원 ⇄ 사각형 모핑 |
| **Vector Text (TMP)** | TMP → 벡터 외곽선, Canvas + World, 실시간 워프 데모 |
| **SVG Import** | ScriptedImporter를 통한 `.svg` 아이콘 |
| **Animator** | `VMGAnimator`를 구동하는 `.vmgfx` 스크립트 (AnimationClip 불필요) |
| **Showcase** | 전체 DSL — stagger, spring/cubic-bezier ease, keyframes, 이벤트 |
| **DOTween Integration** | `DOFade` / `DOTrim` / `DOSize` 익스텐션 (DOTween 필요) |

---

## 📄 라이선스 & 링크

- **저장소:** <https://github.com/ilsang93/unity-vector-motion-graphics-framework>
- **패키지 id:** `com.ilsang.vmg` · **네임스페이스:** `VMG.Core`, `VMG.UI`,
  `VMG.World`, `VMG.Svg`, `VMG.Text`, `VMG.Tween`
