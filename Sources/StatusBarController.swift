import Cocoa
import ServiceManagement

/// 메뉴바 아이콘과 메뉴를 관리한다. 메뉴를 열 때마다 실행 중인 앱 목록을 새로 구성한다.
final class StatusBarController: NSObject, NSMenuDelegate {
    private let statusItem: NSStatusItem
    private let cover: WindowCoverController
    private let onAlphaChange: (CGFloat) -> Void

    init(cover: WindowCoverController, onAlphaChange: @escaping (CGFloat) -> Void) {
        self.cover = cover
        self.onAlphaChange = onAlphaChange
        self.statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        super.init()

        let menu = NSMenu()
        menu.delegate = self
        statusItem.menu = menu
        refresh()
    }

    /// 아이콘을 현재 상태에 맞게 갱신한다.
    func refresh() {
        guard let button = statusItem.button else { return }
        let symbol = cover.isEnabled ? "eye.slash.fill" : "eye.slash"
        button.image = NSImage(systemSymbolName: symbol, accessibilityDescription: "화면 가리기")
        button.image?.isTemplate = true
    }

    // MARK: - NSMenuDelegate

    func menuNeedsUpdate(_ menu: NSMenu) {
        menu.removeAllItems()

        let names = cover.targets.values.sorted { $0.localizedCaseInsensitiveCompare($1) == .orderedAscending }
        let headerTitle = names.isEmpty ? "대상: 없음" : "대상: \(names.joined(separator: ", "))"
        let header = NSMenuItem(title: headerTitle, action: nil, keyEquivalent: "")
        header.isEnabled = false
        menu.addItem(header)

        let toggle = NSMenuItem(
            title: cover.isEnabled ? "가리기 끄기" : "가리기 켜기",
            action: #selector(toggleTapped),
            keyEquivalent: ""
        )
        toggle.target = self
        toggle.isEnabled = cover.hasTargets
        menu.addItem(toggle)

        menu.addItem(NSMenuItem.separator())

        let pick = NSMenuItem(title: "대상 앱 선택 (여러 개 가능)", action: nil, keyEquivalent: "")
        let submenu = NSMenu()
        for app in runningApps() {
            guard let bundleID = app.bundleIdentifier else { continue }
            let item = NSMenuItem(
                title: app.localizedName ?? bundleID,
                action: #selector(pickApp(_:)),
                keyEquivalent: ""
            )
            item.target = self
            item.representedObject = app
            item.state = cover.isTargeted(bundleID: bundleID) ? .on : .off
            submenu.addItem(item)
        }
        pick.submenu = submenu
        menu.addItem(pick)

        menu.addItem(makeDarknessSliderItem())

        menu.addItem(NSMenuItem.separator())

        let login = NSMenuItem(title: "로그인 시 자동 실행", action: #selector(toggleLaunchAtLogin), keyEquivalent: "")
        login.target = self
        login.state = launchAtLoginEnabled ? .on : .off
        menu.addItem(login)

        let quit = NSMenuItem(title: "종료", action: #selector(quitTapped), keyEquivalent: "q")
        quit.target = self
        menu.addItem(quit)
    }

    private func runningApps() -> [NSRunningApplication] {
        NSWorkspace.shared.runningApplications
            .filter { $0.activationPolicy == .regular && ($0.localizedName?.isEmpty == false) && $0.bundleIdentifier != nil }
            .sorted {
                ($0.localizedName ?? "").localizedCaseInsensitiveCompare($1.localizedName ?? "") == .orderedAscending
            }
    }

    // MARK: - 로그인 항목

    private var launchAtLoginEnabled: Bool {
        SMAppService.mainApp.status == .enabled
    }

    @objc private func toggleLaunchAtLogin() {
        do {
            if SMAppService.mainApp.status == .enabled {
                try SMAppService.mainApp.unregister()
            } else {
                try SMAppService.mainApp.register()
            }
        } catch {
            NSLog("로그인 항목 설정 실패: \(error)")
        }
    }

    // MARK: - 액션

    @objc private func toggleTapped() {
        cover.toggle()
        refresh()
    }

    @objc private func pickApp(_ sender: NSMenuItem) {
        guard let app = sender.representedObject as? NSRunningApplication,
              let bundleID = app.bundleIdentifier else { return }
        cover.toggleTarget(bundleID: bundleID, name: app.localizedName ?? bundleID)
        refresh()
    }

    /// 어둡기 조절 슬라이더를 담은 메뉴 항목.
    private func makeDarknessSliderItem() -> NSMenuItem {
        let container = NSView(frame: NSRect(x: 0, y: 0, width: 220, height: 46))

        let label = NSTextField(labelWithString: "어둡기")
        label.font = NSFont.systemFont(ofSize: 12)
        label.textColor = .secondaryLabelColor
        label.frame = NSRect(x: 14, y: 26, width: 190, height: 16)
        container.addSubview(label)

        let slider = NSSlider(
            value: Double(cover.coverAlpha),
            minValue: 0.3,
            maxValue: 1.0,
            target: self,
            action: #selector(alphaSliderChanged(_:))
        )
        slider.isContinuous = true // 드래그 중 실시간 반영
        slider.frame = NSRect(x: 14, y: 4, width: 192, height: 20)
        container.addSubview(slider)

        let item = NSMenuItem()
        item.view = container
        return item
    }

    @objc private func alphaSliderChanged(_ sender: NSSlider) {
        let alpha = CGFloat(sender.doubleValue)
        cover.setAlpha(alpha)
        onAlphaChange(alpha)
    }

    @objc private func quitTapped() {
        NSApplication.shared.terminate(nil)
    }
}
