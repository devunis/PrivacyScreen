import Cocoa

final class AppDelegate: NSObject, NSApplicationDelegate {
    private var cover: WindowCoverController!
    private var statusBar: StatusBarController!

    private let alphaKey = "coverAlpha"
    private let frontalFocusEnabledKey = "frontalFocusEnabled"
    private let frontalFocusWidthKey = "frontalFocusWidth"
    private let defaultAlpha: CGFloat = 0.92
    private let defaultFrontalFocusWidth: CGFloat = 0.25

    func applicationDidFinishLaunching(_ notification: Notification) {
        let savedAlpha = UserDefaults.standard.object(forKey: alphaKey) as? Double
        let alpha = CGFloat(savedAlpha ?? Double(defaultAlpha))
        let savedFrontalFocusEnabled = UserDefaults.standard.object(forKey: frontalFocusEnabledKey) as? Bool
        let frontalFocusEnabled = savedFrontalFocusEnabled ?? false
        let savedFrontalFocusWidth = UserDefaults.standard.object(forKey: frontalFocusWidthKey) as? Double
        let frontalFocusWidth = CGFloat(savedFrontalFocusWidth ?? Double(defaultFrontalFocusWidth))

        cover = WindowCoverController(
            alpha: alpha,
            frontalFocusEnabled: frontalFocusEnabled,
            frontalFocusWidth: frontalFocusWidth
        )
        let key = alphaKey
        let focusEnabledKey = frontalFocusEnabledKey
        let focusWidthKey = frontalFocusWidthKey
        statusBar = StatusBarController(
            cover: cover,
            onAlphaChange: { newAlpha in
                UserDefaults.standard.set(Double(newAlpha), forKey: key)
            },
            onFrontalFocusEnabledChange: { newValue in
                UserDefaults.standard.set(newValue, forKey: focusEnabledKey)
            },
            onFrontalFocusWidthChange: { newValue in
                UserDefaults.standard.set(Double(newValue), forKey: focusWidthKey)
            }
        )
    }
}

let app = NSApplication.shared
app.setActivationPolicy(.accessory) // Dock 아이콘 없음, 메뉴바만
let delegate = AppDelegate()
app.delegate = delegate
app.run()
