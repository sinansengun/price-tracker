import UIKit
import MobileCoreServices

class ShareViewController: UIViewController {

    override func viewDidAppear(_ animated: Bool) {
        super.viewDidAppear(animated)
        handleSharedItems()
    }

    private func handleSharedItems() {
        guard let extensionItems = extensionContext?.inputItems as? [NSExtensionItem] else {
            close()
            return
        }

        for extensionItem in extensionItems {
            guard let attachments = extensionItem.attachments else { continue }
            for attachment in attachments {
                let urlType = kUTTypeURL as String
                if attachment.hasItemConformingToTypeIdentifier(urlType) {
                    attachment.loadItem(forTypeIdentifier: urlType, options: nil) { [weak self] item, _ in
                        if let url = item as? URL {
                            self?.saveUrl(url.absoluteString)
                        } else if let str = item as? String, str.hasPrefix("http") {
                            self?.saveUrl(str)
                        } else {
                            self?.close()
                        }
                    }
                    return
                }
                let textType = kUTTypePlainText as String
                if attachment.hasItemConformingToTypeIdentifier(textType) {
                    attachment.loadItem(forTypeIdentifier: textType, options: nil) { [weak self] item, _ in
                        if let text = item as? String, text.hasPrefix("http") {
                            self?.saveUrl(text)
                        } else {
                            self?.close()
                        }
                    }
                    return
                }
            }
        }
        close()
    }

    private func saveUrl(_ urlString: String) {
        let userDefaults = UserDefaults(suiteName: "group.com.pricetracker.mobile")
        userDefaults?.set(urlString, forKey: "sharedUrl")
        userDefaults?.synchronize()

        DispatchQueue.main.async {
            // Ana uygulamayı aç, sonra extension'ı kapat
            guard let encoded = urlString.addingPercentEncoding(withAllowedCharacters: .urlQueryAllowed),
                  let appUrl = URL(string: "pricetracker://share?url=\(encoded)") else {
                self.close()
                return
            }
            self.extensionContext?.open(appUrl) { _ in
                self.close()
            }
        }
    }

    private func close() {
        DispatchQueue.main.async {
            self.extensionContext?.completeRequest(returningItems: [], completionHandler: nil)
        }
    }
}
