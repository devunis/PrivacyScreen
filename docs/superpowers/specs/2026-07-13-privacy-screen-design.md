# PrivacyScreen — 특정 앱 창 가리기 유틸리티 설계

## 목적

카페·사무실 등에서 옆·뒤 사람의 훔쳐보기를 줄인다. **선택한 앱의 창만 어두운 덮개로
가려**, 그 앱(예: 메신저·메일·은행)의 내용을 옆 사람이 훑어보지 못하게 한다.

## 물리적 한계 (중요)

"정면에서는 보이고 옆에서만 안 보이게"(물리 사생활 필름과 동일한 효과)는 **소프트웨어로
구현 불가**하다. 화면에서 나온 빛이 퍼지는 각도는 패널 하드웨어가 결정하고 소프트웨어는
관여할 수 없기 때문이다. 따라서 가려진 창은 옆 사람뿐 아니라 **본인에게도** 어둡게 보인다.
이 앱의 실효적 용도는 **지금 직접 보고 있지 않은 배경 창**(뒤에 떠 있는 메신저 등)을
가려두는 것이다. 진짜 각도 기반 보호가 필요하면 물리 사생활 필름을 부착해야 한다.

## 형태

- Swift + AppKit 네이티브 메뉴바 앱 (의존성 0, `LSUIElement`).
- 메뉴바에서 **대상 앱 선택** + **가리기 켜기/끄기**(수동 토글).

## 핵심 컴포넌트

### WindowCoverController + CoverView
- **화면당 투명 오버레이 1장**(`CoverView`). 대상 창의 '보이는 영역' 사각형들의 합집합을
  `NSBezierPath` 단일 fill(nonzero) 로 칠한다 → **겹쳐도 픽셀당 한 번만** 칠해져 이중
  어두워짐이 없다.
- 갱신 주기: `CADisplayLink`(macOS 14+, 화면 vsync 동기 → 저지연 팔로우), 미만은
  60Hz `Timer`(tolerance 0) 폴백. 매 틱마다 `CGWindowListCopyWindowInfo`
  (onScreenOnly, 앞→뒤 순서) 폴링 → `layer == 0` + 40px 이상 + 자기 PID 제외 창 수집.
- **가려짐(occlusion) 처리**: 각 대상 창에서 '앞에 있는 비대상 창'의 사각형을 빼서
  실제로 보이는(맨 위인) 부분만 남긴다(사각형 차집합 → 최대 4개 띠). 대상 창이 다른
  창에 완전히 가려지면 덮개를 그리지 않는다.
- **둥근 모서리**: clip = 보이는 영역(사각형), fill = 대상 창 전체를 둥근 사각형
  (`appendRoundedRect`, radius 10)으로 한 번에 칠함. 겉모서리는 둥글고, 가려진 곳은
  clip 으로 잘리며, 겹쳐도 픽셀당 한 번만.
- **다중 대상**: 대상은 **번들 ID 집합**(`targets: [bundleID: name]`). 매 프레임
  실행 중 앱에서 대상 PID 집합을 만들어 매칭.
- 좌표 변환: CG(좌상단 원점, y↓) → Cocoa(좌하단 원점, y↑),
  `cocoaY = primaryHeight - cgY - height`. 다중 디스플레이 대응.
- 오버레이 창: `backgroundColor=.clear`, `ignoresMouseEvents=true`(클릭 통과),
  `level=.floating`,
  `collectionBehavior=[.canJoinAllSpaces,.stationary,.ignoresCycle,.fullScreenAuxiliary]`
  (`.canJoinAllSpaces` 로 "디스플레이마다 개별 공간"에서도 보조 모니터 표시).
- **성능**: `CoverView.setGeometry` 가 이전 프레임과 동일하면 재그리지 않음(idle 60fps 방지).
- 어둡기(coverAlpha)는 `CoverView.coverColor` 로 즉시 반영.
- **대상 앱 저장**: 번들 ID 를 `UserDefaults(targetApps)` 에 저장 → 재시작 후 유지.
- **권한 불필요**: bounds/ownerPID 만 사용(창 제목은 읽지 않음 → 화면 녹화 권한 불요).

### StatusBarController
- `NSStatusItem` 아이콘(`eye.slash` / 켜짐 `eye.slash.fill`).
- `NSMenuDelegate.menuNeedsUpdate` 로 메뉴를 열 때마다 재구성:
  - 헤더 "대상: <앱들>", **가리기 켜기/끄기**(대상 없으면 비활성),
    **대상 앱 선택**(다중 체크), **어둡기 슬라이더**(0.3~1.0 실시간 조절, `NSMenuItem.view`),
    **로그인 시 자동 실행**(`SMAppService.mainApp`), **종료**.

## 데이터 흐름

```
대상 앱 토글 → cover.toggleTarget(bundleID,name) → UserDefaults 저장
토글        → cover.toggle() → 타이머 시작/정지
매 프레임    → CGWindowList 폴링 → (fill: 대상 창 / clip: 보이는 영역) → 오버레이 갱신
```

## 알려진 한계

- 대상 창이 최소화되거나 다른 Space 로 가면 덮개는 사라지고, 돌아오면 다시 가린다.
- 둥근 모서리 반경은 고정(10pt). 사각 모서리 앱은 모서리에 미세한 틈이 생길 수 있다.
- **로그인 자동 실행**은 애드혹 서명 + `/Applications` 배치에서 안정적. Desktop 의
  미서명 실행본은 시스템 설정 > 일반 > 로그인 항목에서 승인이 필요할 수 있다.

## 파일 구조

```
privacy/
  Sources/
    main.swift                  # 진입점, 컴포넌트 조립
    WindowCoverController.swift  # 창 추적 + 덮개
    StatusBarController.swift    # 메뉴바 UI
  build.sh                       # swiftc → PrivacyScreen.app
  Info.plist                     # LSUIElement 등
```

## 비목표 (YAGNI)

- 각도 기반 보호(물리적으로 불가), 흐림(블러) 효과, 자동 드러내기(활성 시 자동 해제),
  글로벌 단축키, 오클루전 정밀 계산.

## 검증

1. `./build.sh` 클린 컴파일, 앱 실행 시 크래시 없음 — 확인됨.
2. 창 추적 로직 독립 검증: 권한 없이 타 앱 창 bounds 취득 + 좌표 변환 정확 — 확인됨.
3. (사용자 확인) 메뉴바에서 대상 앱 선택 → 가리기 켜기 → 해당 앱 창이 어두워지고,
   창을 움직이면 덮개가 따라옴. 끄면 원복.
