# mobile

A new Flutter project.

## Getting Started

This project is a starting point for a Flutter application.

A few resources to get you started if this is your first Flutter project:

- [Learn Flutter](https://docs.flutter.dev/get-started/learn-flutter)
- [Write your first Flutter app](https://docs.flutter.dev/get-started/codelab)
- [Flutter learning resources](https://docs.flutter.dev/reference/learning-resources)

For help getting started with Flutter development, view the
[online documentation](https://docs.flutter.dev/), which offers tutorials,
samples, guidance on mobile development, and a full API reference.

## Firebase Analytics DebugView (MVP Funnel)

This app logs MVP funnel events with a strict no-PII policy.

- Never log raw URL, email, JWT/FCM token, full product name, or free text input.
- Logged params are sanitized and bucketed (for example: `product_count_bucket`, `url_domain_group`).

### Verify in DebugView

1. Run app in debug mode on device/emulator.
2. Enable Analytics debug mode:
	 - Android:
		 `adb shell setprop debug.firebase.analytics.app com.cufica.pricetracker`
	 - iOS:
		 Add launch argument in Xcode scheme:
		 `-FIRDebugEnabled`
3. Open Firebase Console > Analytics > DebugView.
4. Trigger these flows and confirm events:
	 - Auth: `login_attempt`, `login_success`, `login_failed`, `signup_attempt`, `signup_success`, `signup_failed`
	 - Product add funnel: `add_product_opened`, `add_product_submitted`, `add_product_success`, `add_product_failed`
	 - Views: `products_screen_viewed`, `product_detail_viewed`
	 - Push: `push_permission_result`, `push_foreground_received`, `push_opened_from_background`

### Basic validation checklist

1. Event names are lowercase snake_case.
2. No personal or high-cardinality raw fields are sent.
3. Failed auth/product events include only normalized reason values.
4. Push events do not include payload bodies, tokens, or user identifiers.

### Quick smoke flow (recommended order)

1. Open app and complete login flow.
	- Expect: `login_attempt` then either `login_success` or `login_failed`
2. Land on product list.
	- Expect: `products_screen_viewed`
3. Open add product sheet (FAB or share intent), submit a URL.
	- Expect: `add_product_opened` -> `add_product_submitted` -> (`add_product_success` or `add_product_failed`)
4. Open a product detail page.
	- Expect: `product_detail_viewed`
5. Trigger push permission prompt / foreground push / background open.
	- Expect: `push_permission_result`, `push_foreground_received`, `push_opened_from_background`

### Parameter sanity examples

- `add_product_submitted`
  - Allowed: `has_target_price`, `url_domain_group`
  - Not allowed: full URL/query/path
- `login_failed` / `signup_failed`
  - Allowed: normalized `reason` (for example: `network`, `invalid_credentials`)
  - Not allowed: backend raw error text containing user input
- `push_*`
  - Allowed: `status`, `source`, boolean flags
  - Not allowed: token, message body, user identifier
