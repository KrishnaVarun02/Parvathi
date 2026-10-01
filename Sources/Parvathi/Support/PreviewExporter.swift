import SwiftUI
import AppKit

/// Renders the actual SwiftUI dashboard for layout review without microphone or Accessibility access.
@MainActor
enum PreviewExporter {
    static func export(controller: AssistantController, settings: SettingsStore, path: String, completion: @escaping (Bool) -> Void) {
        // AppKit caching includes native ScrollView/TextField content that ImageRenderer omits.
        let host = NSHostingView(rootView: DashboardView(controller: controller, settings: settings)
            .frame(width: 920, height: 680).environment(\.colorScheme, .dark))
        let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 920, height: 680), styleMask: [.borderless], backing: .buffered, defer: false)
        window.appearance = NSAppearance(named: .darkAqua)
        window.contentView = host
        window.orderBack(nil)
        host.layoutSubtreeIfNeeded()
        window.displayIfNeeded()
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.5) {
            defer { window.orderOut(nil) }
            guard let bitmap = host.bitmapImageRepForCachingDisplay(in: host.bounds) else { completion(false); return }
            host.cacheDisplay(in: host.bounds, to: bitmap)
            guard let data = bitmap.representation(using: .png, properties: [:]) else { completion(false); return }
            do { try data.write(to: URL(fileURLWithPath: path)); completion(true) } catch { completion(false) }
        }
    }
}
