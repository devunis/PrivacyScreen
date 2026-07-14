import Cocoa
import QuartzCore

/// 화면 하나를 덮는 투명 오버레이.
/// - clipRects: 실제로 보이는(가려지지 않은) 영역 → 클립.
/// - fillRects: 대상 창 전체 사각형 → 둥근 사각형으로 칠함(겉모서리 라운딩).
/// 클립 안에서 단일 fill 이므로 겹쳐도 픽셀당 한 번만, 가려진 곳은 그리지 않는다.
private final class CoverView: NSView {
    private var clipRects: [NSRect] = []
    private var fillRects: [NSRect] = []
    private var frontalFocusEnabled = false
    private var frontalFocusWidth: CGFloat = 0.65
    var cornerRadius: CGFloat = 10
    var coverColor: NSColor = .black { didSet { needsDisplay = true } }

    func setFrontalFocus(enabled: Bool, width: CGFloat) {
        let clampedWidth = max(0.25, min(width, 1.0))
        if frontalFocusEnabled == enabled && frontalFocusWidth == clampedWidth { return }
        frontalFocusEnabled = enabled
        frontalFocusWidth = clampedWidth
        needsDisplay = true
    }

    /// 변화가 있을 때만 다시 그린다(불필요한 60fps 재그리기 방지).
    func setGeometry(clip: [NSRect], fill: [NSRect]) {
        if clip == clipRects && fill == fillRects { return }
        clipRects = clip
        fillRects = fill
        needsDisplay = true
    }

    override func draw(_ dirtyRect: NSRect) {
        guard !fillRects.isEmpty, !clipRects.isEmpty else { return }
        NSGraphicsContext.saveGraphicsState()
        defer { NSGraphicsContext.restoreGraphicsState() }

        let clipPath = NSBezierPath()
        for rect in clipRects { clipPath.appendRect(rect) }
        clipPath.setClip()

        for rect in fillRects {
            let fillPath = NSBezierPath(roundedRect: rect, xRadius: cornerRadius, yRadius: cornerRadius)
            if frontalFocusEnabled {
                drawFrontalFocusFill(path: fillPath, rect: rect)
            } else {
                coverColor.setFill()
                fillPath.fill()
            }
        }
    }

    private func drawFrontalFocusFill(path: NSBezierPath, rect: NSRect) {
        guard let context = NSGraphicsContext.current?.cgContext else {
            coverColor.setFill()
            path.fill()
            return
        }

        let baseAlpha = coverColor.alphaComponent
        let baseFillAlpha = min(1.0, baseAlpha * 0.55)
        let lineAlpha = min(1.0, baseAlpha + 0.08)
        let scale = window?.backingScaleFactor ?? NSScreen.main?.backingScaleFactor ?? 2.0
        let pixel = 1.0 / scale // 실제 1px
        let stepPixels = max(1, Int((1.0 + frontalFocusWidth * 7.0).rounded()))
        let pitch = CGFloat(stepPixels) * pixel
        let lineWidth = pixel

        context.saveGState()
        path.addClip()
        NSColor.black.withAlphaComponent(baseFillAlpha).setFill()
        path.fill()
        context.setShouldAntialias(false)
        NSColor.black.withAlphaComponent(lineAlpha).setFill()

        var x = floor(rect.minX / pixel) * pixel
        while x < rect.maxX {
            let stripe = NSRect(x: x, y: rect.minY, width: lineWidth, height: rect.height)
            NSBezierPath(rect: stripe).fill()
            x += pitch
        }

        context.restoreGState()
    }

    // 클릭 통과
    override func hitTest(_ point: NSPoint) -> NSView? { nil }
}

/// 선택한 앱들의 창 중 '실제로 보이는(맨 위) 부분'만 어둡게 덮는다.
/// 대상은 번들 ID 로 저장되어 재시작·재실행 후에도 유지된다.
/// CGWindowList 의 bounds/ownerPID 만 사용하므로 화면 녹화·접근성 권한이 필요 없다.
final class WindowCoverController {
    private(set) var isEnabled = false
    /// 대상 앱들: 번들 ID → 표시 이름 (UserDefaults 에 저장).
    private(set) var targets: [String: String] = [:]

    private(set) var coverAlpha: CGFloat
    private(set) var frontalFocusEnabled: Bool
    private(set) var frontalFocusWidth: CGFloat
    private var overlays: [NSWindow] = [] // 화면당 하나
    private var displayLink: CADisplayLink?
    private var timer: Timer? // macOS 14 미만 폴백

    private let targetsKey = "targetApps"

    var hasTargets: Bool { !targets.isEmpty }

    init(alpha: CGFloat, frontalFocusEnabled: Bool, frontalFocusWidth: CGFloat) {
        self.coverAlpha = alpha
        self.frontalFocusEnabled = frontalFocusEnabled
        self.frontalFocusWidth = max(0.25, min(frontalFocusWidth, 1.0))
        if let saved = UserDefaults.standard.dictionary(forKey: targetsKey) as? [String: String] {
            targets = saved
        }
        NotificationCenter.default.addObserver(
            self,
            selector: #selector(screensChanged),
            name: NSApplication.didChangeScreenParametersNotification,
            object: nil
        )
    }

    private var coverColor: NSColor { NSColor.black.withAlphaComponent(coverAlpha) }

    func isTargeted(bundleID: String) -> Bool { targets[bundleID] != nil }

    /// 대상 앱을 추가/제거(토글)하고 저장한다.
    func toggleTarget(bundleID: String, name: String) {
        if targets[bundleID] != nil {
            targets[bundleID] = nil
        } else {
            targets[bundleID] = name
        }
        UserDefaults.standard.set(targets, forKey: targetsKey)
        if targets.isEmpty {
            setEnabled(false)
        } else if isEnabled {
            update()
        }
    }

    /// 덮개 어둡기를 바꾼다. 켜져 있으면 즉시 반영한다.
    func setAlpha(_ value: CGFloat) {
        coverAlpha = value
        for window in overlays {
            if let view = window.contentView as? CoverView {
                applyAppearance(to: view)
            }
        }
    }

    func setFrontalFocusEnabled(_ enabled: Bool) {
        frontalFocusEnabled = enabled
        for window in overlays {
            if let view = window.contentView as? CoverView {
                applyAppearance(to: view)
            }
        }
    }

    func setFrontalFocusWidth(_ width: CGFloat) {
        frontalFocusWidth = max(0.25, min(width, 1.0))
        for window in overlays {
            if let view = window.contentView as? CoverView {
                applyAppearance(to: view)
            }
        }
    }

    func setEnabled(_ on: Bool) {
        if on { guard hasTargets else { isEnabled = false; return } }
        if on == isEnabled { return }
        isEnabled = on
        if on { start() } else { stop() }
    }

    func toggle() { setEnabled(!isEnabled) }

    @objc private func screensChanged() {
        guard isEnabled else { return }
        rebuildOverlays()
        update()
    }

    private func start() {
        rebuildOverlays()
        // 화면 새로고침에 동기화(vsync) → 창을 지연 없이 따라간다.
        if #available(macOS 14.0, *), let screen = NSScreen.main ?? NSScreen.screens.first {
            let link = screen.displayLink(target: self, selector: #selector(tick))
            link.add(to: .main, forMode: .common) // 드래그·메뉴 트래킹 중에도 계속.
            displayLink = link
        } else {
            let t = Timer(timeInterval: 1.0 / 60.0, repeats: true) { [weak self] _ in self?.update() }
            t.tolerance = 0
            RunLoop.main.add(t, forMode: .common)
            timer = t
        }
        update()
    }

    @objc private func tick() { update() }

    private func stop() {
        displayLink?.invalidate()
        displayLink = nil
        timer?.invalidate()
        timer = nil
        for window in overlays { window.orderOut(nil) }
        overlays.removeAll()
    }

    private func rebuildOverlays() {
        for window in overlays { window.orderOut(nil) }
        overlays.removeAll()
        for screen in NSScreen.screens {
            overlays.append(makeOverlay(for: screen))
        }
    }

    private func makeOverlay(for screen: NSScreen) -> NSWindow {
        let window = NSWindow(
            contentRect: screen.frame,
            styleMask: .borderless,
            backing: .buffered,
            defer: false
        )
        window.isOpaque = false
        window.backgroundColor = .clear
        window.ignoresMouseEvents = true
        window.level = .floating
        // .canJoinAllSpaces: "디스플레이마다 개별 공간" 설정에서 보조 모니터에도 표시되도록.
        window.collectionBehavior = [.canJoinAllSpaces, .stationary, .ignoresCycle, .fullScreenAuxiliary]
        window.hasShadow = false
        window.isReleasedWhenClosed = false

        let view = CoverView(frame: NSRect(origin: .zero, size: screen.frame.size))
        applyAppearance(to: view)
        window.contentView = view

        window.setFrame(screen.frame, display: false)
        window.orderFrontRegardless()
        return window
    }

    private func applyAppearance(to view: CoverView) {
        view.coverColor = coverColor
        view.setFrontalFocus(enabled: frontalFocusEnabled, width: frontalFocusWidth)
    }

    private func update() {
        guard isEnabled else { return }

        // 화면 구성이 바뀌었는데 알림을 놓친 경우 대비.
        if overlays.count != NSScreen.screens.count { rebuildOverlays() }

        let targetPIDs = activeTargetPIDs()
        let geo = computeCoverGeometry(targetPIDs: targetPIDs)
        for (index, screen) in NSScreen.screens.enumerated() {
            guard index < overlays.count,
                  let view = overlays[index].contentView as? CoverView else { continue }
            let origin = screen.frame.origin
            view.setGeometry(
                clip: geo.clipRects.map { localize($0, origin) },
                fill: geo.fillRects.map { localize($0, origin) }
            )
        }
    }

    private func localize(_ rect: NSRect, _ origin: NSPoint) -> NSRect {
        NSRect(x: rect.minX - origin.x, y: rect.minY - origin.y, width: rect.width, height: rect.height)
    }

    /// 현재 실행 중인 앱 중 대상 번들 ID 에 해당하는 PID 집합.
    private func activeTargetPIDs() -> Set<pid_t> {
        var pids = Set<pid_t>()
        for app in NSWorkspace.shared.runningApplications {
            if let bundleID = app.bundleIdentifier, targets[bundleID] != nil {
                pids.insert(app.processIdentifier)
            }
        }
        return pids
    }

    /// 대상 창의 전체 사각형(fill)과 가려지지 않은 영역(clip)을 전역 Cocoa 좌표로 구한다.
    private func computeCoverGeometry(targetPIDs: Set<pid_t>) -> (fillRects: [NSRect], clipRects: [NSRect]) {
        let options: CGWindowListOption = [.optionOnScreenOnly, .excludeDesktopElements]
        guard !targetPIDs.isEmpty,
              let list = CGWindowListCopyWindowInfo(options, kCGNullWindowID) as? [[String: Any]] else {
            return ([], [])
        }
        let primaryHeight = NSScreen.screens.first?.frame.height ?? 0
        let myPID = Int(getpid())

        // CGWindowList 는 앞→뒤 순서. 자기 자신(오버레이)은 제외.
        struct Win { let isTarget: Bool; let rect: NSRect }
        var windows: [Win] = []
        for info in list {
            guard let owner = info[kCGWindowOwnerPID as String] as? Int, owner != myPID else { continue }
            guard let layer = info[kCGWindowLayer as String] as? Int, layer == 0 else { continue }
            if let alpha = info[kCGWindowAlpha as String] as? Double, alpha <= 0.01 { continue }
            guard let boundsDict = info[kCGWindowBounds as String] as? NSDictionary,
                  let cgRect = CGRect(dictionaryRepresentation: boundsDict) else { continue }
            if cgRect.width < 40 || cgRect.height < 40 { continue }

            let rect = NSRect(
                x: cgRect.origin.x,
                y: primaryHeight - cgRect.origin.y - cgRect.height,
                width: cgRect.width,
                height: cgRect.height
            )
            windows.append(Win(isTarget: targetPIDs.contains(pid_t(owner)), rect: rect))
        }

        var fillRects: [NSRect] = []
        var clipRects: [NSRect] = []
        for (i, win) in windows.enumerated() where win.isTarget {
            fillRects.append(win.rect)
            // 앞에 있는 '비대상 창'에 가려진 부분을 뺀 나머지가 clip.
            var visible = [win.rect]
            for j in 0..<i {
                let front = windows[j]
                if front.isTarget { continue } // 대상 앱 창끼리는 서로 가리지 않음(어차피 덮음)
                visible = visible.flatMap { WindowCoverController.subtract($0, front.rect) }
                if visible.isEmpty { break }
            }
            clipRects.append(contentsOf: visible)
        }
        return (fillRects, clipRects)
    }

    /// rect 에서 hole 을 뺀 나머지(최대 4개의 사각형 띠).
    private static func subtract(_ rect: NSRect, _ hole: NSRect) -> [NSRect] {
        let inter = rect.intersection(hole)
        if inter.isEmpty { return [rect] }

        var pieces: [NSRect] = []
        if inter.maxY < rect.maxY { // 위 띠
            pieces.append(NSRect(x: rect.minX, y: inter.maxY, width: rect.width, height: rect.maxY - inter.maxY))
        }
        if inter.minY > rect.minY { // 아래 띠
            pieces.append(NSRect(x: rect.minX, y: rect.minY, width: rect.width, height: inter.minY - rect.minY))
        }
        if inter.minX > rect.minX { // 왼쪽 띠(가운데 높이만큼)
            pieces.append(NSRect(x: rect.minX, y: inter.minY, width: inter.minX - rect.minX, height: inter.height))
        }
        if inter.maxX < rect.maxX { // 오른쪽 띠
            pieces.append(NSRect(x: inter.maxX, y: inter.minY, width: rect.maxX - inter.maxX, height: inter.height))
        }
        return pieces
    }
}
