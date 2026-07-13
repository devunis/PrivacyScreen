# PrivacyScreen

선택한 앱의 창만 어두운 덮개로 가려, 옆·뒤 사람의 훔쳐보기를 줄이는 macOS 메뉴바 유틸리티.

메뉴바에서 가릴 앱을 고르고 토글하면, 그 앱(예: 메신저·메일)의 창이 어두워집니다.
덮개는 클릭이 통과되어 가려진 상태에서도 그 앱을 조작할 수 있습니다.

## 기능

- **특정 앱만 가리기** — 여러 앱 동시 선택 가능(체크 토글)
- **가려짐 처리** — 대상 창이 다른 창에 가려지면 그 부분은 덮지 않고, 보이는 부분만 가림
- **둥근 모서리** — 앱의 둥근 모서리를 따라 덮개도 둥글게
- **다중 모니터** 지원
- **어둡기 슬라이더** — 30%~100% 실시간 조절
- **대상 앱 기억** — 번들 ID로 저장, 재시작 후에도 유지
- **로그인 시 자동 실행** (메뉴에서 on/off)
- **저지연 팔로우** — `CADisplayLink`(vsync)로 창을 따라감
- **권한 불필요** — 창 위치/크기만 읽고 제목은 읽지 않음(화면 녹화·접근성 권한 불요)

## 요구 사항

- macOS 13 이상 (일부 기능은 macOS 14+)
- Xcode 커맨드라인 도구(`swiftc`)

## 빌드 & 실행

```bash
./build.sh          # PrivacyScreen.app 생성
open PrivacyScreen.app
```

설치하려면 `/Applications` 로 옮기세요:

```bash
cp -R PrivacyScreen.app /Applications/
```

## 사용법

메뉴바의 눈 아이콘(`eye.slash`) 클릭 →

1. **대상 앱 선택** 에서 가릴 앱을 체크(여러 개 가능)
2. **가리기 켜기**
3. 필요 시 **어둡기** 슬라이더로 진하기 조절

## 배포 참고

이 저장소의 빌드는 **애드혹 서명**(Apple Developer 인증서 없음)입니다.
다른 맥에 파일로 전달하면 Gatekeeper가 "확인되지 않은 개발자" 경고를 띄우므로,
받는 사람이 **우클릭 › 열기** 로 한 번 허용해야 합니다.
경고 없는 정식 배포는 Apple Developer Program 가입 후 Developer ID 서명 + 공증이 필요합니다.

또한 애플 실리콘(arm64) 전용으로 빌드됩니다.

## 구조

```
Sources/
  main.swift                  # 진입점, 컴포넌트 조립
  WindowCoverController.swift  # 창 추적 + 덮개 렌더링
  StatusBarController.swift    # 메뉴바 UI
build.sh                       # swiftc → PrivacyScreen.app
Info.plist
docs/…/*-design.md             # 설계 문서
```
