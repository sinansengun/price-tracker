import Flutter
import UIKit

class SceneDelegate: FlutterSceneDelegate {
  // URL scheme ile (SceneDelegate üzerinden) açıldığında
  override func scene(
    _ scene: UIScene,
    openURLContexts urlContexts: Set<UIOpenURLContext>
  ) {
    super.scene(scene, openURLContexts: urlContexts)
    if let url = urlContexts.first?.url {
      handleIncomingURL(url)
    }
  }
}

