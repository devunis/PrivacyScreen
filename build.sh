#!/bin/bash
# PrivacyScreen 빌드 스크립트: swiftc 로 컴파일 후 .app 번들을 만든다.
set -euo pipefail

cd "$(dirname "$0")"

APP="PrivacyScreen.app"
CONTENTS="$APP/Contents"
MACOS="$CONTENTS/MacOS"

echo "▸ 이전 빌드 정리"
rm -rf "$APP"

echo "▸ 번들 구조 생성"
mkdir -p "$MACOS"
cp Info.plist "$CONTENTS/Info.plist"

echo "▸ swiftc 컴파일"
swiftc -O \
    -framework Cocoa \
    -framework ServiceManagement \
    -o "$MACOS/PrivacyScreen" \
    Sources/*.swift

echo "▸ 애드혹 서명(로그인 항목 안정화)"
codesign --force --deep --sign - "$APP"

echo "▸ 완료: $APP"
echo ""
echo "실행:   open $APP"
echo "설치:   cp -R $APP /Applications/"
