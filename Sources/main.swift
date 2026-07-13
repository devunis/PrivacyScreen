import Cocoa

final class AppDelegate: NSObject, NSApplicationDelegate {
    private var cover: WindowCoverController!
    private var statusBar: StatusBarController!

    private let alphaKey = "coverAlpha"
    private let defaultAlpha: CGFloat = 0.92

    func applicationDidFinishLaunching(_ notification: Notification) {
        let savedAlpha = UserDefaults.standard.object(forKey: alphaKey) as? Double
        let alpha = CGFloat(savedAlpha ?? Double(defaultAlpha))

        cover = WindowCoverController(alpha: alpha)
        let key = alphaKey
        statusBar = StatusBarController(cover: cover) { newAlpha in
            UserDefaults.standard.set(Double(newAlpha), forKey: key)
        }
    }
}

let app = NSApplication.shared
app.setActivationPolicy(.accessory) // Dock 아이콘 없음, 메뉴바만
let delegate = AppDelegate()
app.delegate = delegate
app.run()
