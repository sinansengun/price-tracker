import Flutter
import UIKit

@main
@objc class AppDelegate: FlutterAppDelegate, FlutterImplicitEngineDelegate {
  static var shareChannel: FlutterMethodChannel?
  static var pendingSharedUrl: String?

  override func application(
    _ application: UIApplication,
    didFinishLaunchingWithOptions launchOptions: [UIApplication.LaunchOptionsKey: Any]?
  ) -> Bool {
    return super.application(application, didFinishLaunchingWithOptions: launchOptions)
  }

  func didInitializeImplicitFlutterEngine(_ engineBridge: FlutterImplicitEngineBridge) {
    GeneratedPluginRegistrant.register(with: engineBridge.pluginRegistry)

    // SceneDelegate mimarisinde window nil olabileceği için messenger'ı registrar'dan alıyoruz
    guard let messenger = engineBridge.pluginRegistry
            .registrar(forPlugin: "SharePlugin")?.messenger() else { return }

    AppDelegate.shareChannel = FlutterMethodChannel(
      name: "com.pricetracker.mobile/share",
      binaryMessenger: messenger
    )

    AppDelegate.shareChannel?.setMethodCallHandler { call, result in
      if call.method == "getPendingUrl" {
        // App Group UserDefaults'tan oku (Extension'ın bıraktığı URL)
        let userDefaults = UserDefaults(suiteName: "group.com.pricetracker.mobile")
        if let storedUrl = userDefaults?.string(forKey: "sharedUrl"), !storedUrl.isEmpty {
          userDefaults?.removeObject(forKey: "sharedUrl")
          result(storedUrl)
        } else if let pending = AppDelegate.pendingSharedUrl {
          AppDelegate.pendingSharedUrl = nil
          result(pending)
        } else {
          result(nil)
        }
      } else {
        result(FlutterMethodNotImplemented)
      }
    }
  }

  // URL scheme ile açıldığında (pricetracker://share?url=...)
  override func application(
    _ app: UIApplication,
    open url: URL,
    options: [UIApplication.OpenURLOptionsKey: Any] = [:]
  ) -> Bool {
    handleIncomingURL(url)
    return true
  }

  static func sendSharedUrl(_ urlString: String) {
    DispatchQueue.main.async {
      if let channel = shareChannel {
        channel.invokeMethod("sharedUrl", arguments: urlString)
      } else {
        pendingSharedUrl = urlString
      }
    }
  }
}

func handleIncomingURL(_ url: URL) {
  guard url.scheme == "pricetracker",
        url.host == "share",
        let components = URLComponents(url: url, resolvingAgainstBaseURL: false),
        let urlParam = components.queryItems?.first(where: { $0.name == "url" })?.value
  else { return }
  // UserDefaults'u hemen temizle: didChangeAppLifecycleState → getPendingUrl
  // ile URL scheme → sharedUrl channel yollarının aynı URL'yi çift işlemesini engelle.
  UserDefaults(suiteName: "group.com.pricetracker.mobile")?.removeObject(forKey: "sharedUrl")
  AppDelegate.sendSharedUrl(urlParam)
}

