import UIKit
import MobileCoreServices
import UniformTypeIdentifiers

class ShareViewController: UIViewController {

    // MARK: - UI

    private let cardView: UIView = {
        let v = UIView()
        v.backgroundColor = .systemBackground
        v.layer.cornerRadius = 18
        v.layer.shadowColor = UIColor.black.cgColor
        v.layer.shadowOpacity = 0.15
        v.layer.shadowRadius = 12
        v.translatesAutoresizingMaskIntoConstraints = false
        return v
    }()

    private let spinner = UIActivityIndicatorView(style: .medium)

    private let messageLabel: UILabel = {
        let l = UILabel()
        l.text = "İşleniyor…"
        l.font = .systemFont(ofSize: 15, weight: .medium)
        l.textAlignment = .center
        l.translatesAutoresizingMaskIntoConstraints = false
        return l
    }()

    // MARK: - Lifecycle

    override func viewDidLoad() {
        super.viewDidLoad()
        view.backgroundColor = UIColor.black.withAlphaComponent(0.45)
        setupCard()
    }

    override func viewDidAppear(_ animated: Bool) {
        super.viewDidAppear(animated)
        handleSharedItems()
    }

    // MARK: - Setup

    private func setupCard() {
        view.addSubview(cardView)
        spinner.translatesAutoresizingMaskIntoConstraints = false
        spinner.startAnimating()
        cardView.addSubview(spinner)
        cardView.addSubview(messageLabel)

        NSLayoutConstraint.activate([
            cardView.centerXAnchor.constraint(equalTo: view.centerXAnchor),
            cardView.centerYAnchor.constraint(equalTo: view.centerYAnchor),
            cardView.widthAnchor.constraint(equalToConstant: 220),
            cardView.heightAnchor.constraint(equalToConstant: 90),

            spinner.centerXAnchor.constraint(equalTo: cardView.centerXAnchor),
            spinner.topAnchor.constraint(equalTo: cardView.topAnchor, constant: 18),

            messageLabel.centerXAnchor.constraint(equalTo: cardView.centerXAnchor),
            messageLabel.bottomAnchor.constraint(equalTo: cardView.bottomAnchor, constant: -16),
            messageLabel.leadingAnchor.constraint(equalTo: cardView.leadingAnchor, constant: 12),
            messageLabel.trailingAnchor.constraint(equalTo: cardView.trailingAnchor, constant: -12),
        ])
    }

    // MARK: - Share handling

    private func handleSharedItems() {
        guard let extensionItems = extensionContext?.inputItems as? [NSExtensionItem] else {
            close(); return
        }

        for extensionItem in extensionItems {
            guard let attachments = extensionItem.attachments else { continue }
            for attachment in attachments {
                let urlTypeId: String = {
                    if #available(iOS 14, *) { return UTType.url.identifier }
                    return kUTTypeURL as String
                }()
                let textTypeId: String = {
                    if #available(iOS 14, *) { return UTType.plainText.identifier }
                    return kUTTypePlainText as String
                }()

                if attachment.hasItemConformingToTypeIdentifier(urlTypeId) {
                    attachment.loadItem(forTypeIdentifier: urlTypeId) { [weak self] item, _ in
                        if let url = item as? URL {
                            self?.saveAndOpen(url.absoluteString)
                        } else if let str = item as? String, str.hasPrefix("http") {
                            self?.saveAndOpen(str)
                        } else {
                            self?.close()
                        }
                    }
                    return
                }
                if attachment.hasItemConformingToTypeIdentifier(textTypeId) {
                    attachment.loadItem(forTypeIdentifier: textTypeId) { [weak self] item, _ in
                        if let text = item as? String, text.hasPrefix("http") {
                            self?.saveAndOpen(text)
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

    // MARK: - Save + open

    private func saveAndOpen(_ urlString: String) {
        // UserDefaults'a kaydet (ana uygulama açıldığında okur)
        let defaults = UserDefaults(suiteName: "group.com.pricetracker.mobile")
        defaults?.set(urlString, forKey: "sharedUrl")
        defaults?.synchronize()

        DispatchQueue.main.async { [weak self] in
            guard let self else { return }

            // URL'yi query param olarak encode ederken ?&=# karakterlerini de encode et
            var safeChars = CharacterSet.urlQueryAllowed
            safeChars.remove(charactersIn: "?&=#[]")
            guard let encoded = urlString.addingPercentEncoding(withAllowedCharacters: safeChars),
                  let appUrl = URL(string: "pricetracker://share?url=\(encoded)") else {
                self.showSavedUI(thenClose: true)
                return
            }

            // Uygulamayı açmayı dene
            self.extensionContext?.open(appUrl) { [weak self] success in
                DispatchQueue.main.async {
                    if success {
                        // Uygulama açıldı, extension'ı kapat
                        self?.close()
                    } else {
                        // Açılamadı — URL kaydedildi, kullanıcıya bildir
                        self?.showSavedUI(thenClose: true)
                    }
                }
            }
        }
    }

    // MARK: - Feedback UI

    private func showSavedUI(thenClose: Bool) {
        spinner.stopAnimating()
        spinner.isHidden = true
        let accent = UIColor(red: 0.388, green: 0.400, blue: 0.945, alpha: 1) // #6366F1
        messageLabel.text = "✓ Kaydedildi"
        messageLabel.textColor = accent

        if thenClose {
            DispatchQueue.main.asyncAfter(deadline: .now() + 1.3) { [weak self] in
                self?.close()
            }
        }
    }

    // MARK: - Close

    private func close() {
        DispatchQueue.main.async { [weak self] in
            self?.extensionContext?.completeRequest(returningItems: [], completionHandler: nil)
        }
    }
}
