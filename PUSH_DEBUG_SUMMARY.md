# Push Debug Summary (17 Nisan 2026)

## Current Status
- Mobile app can connect to local backend.
- Local login works (test user reset + verified).
- Test notification endpoint works end-to-end up to backend.
- FCM send still fails with:
  - `UNAUTHENTICATED`
  - `THIRD_PARTY_AUTH_ERROR`

## Key Diagnosis
- OAuth access token is valid.
- Token scopes are valid:
  - `https://www.googleapis.com/auth/cloud-platform`
  - `https://www.googleapis.com/auth/firebase.messaging`
- Therefore issue is not local backend auth.
- Most likely failure point is Firebase -> APNs chain (Apple side / iOS push config mismatch).

## Changes Made Today
- Mobile API base URL became configurable with `--dart-define`.
  - File: `mobile/lib/api/api_client.dart`
- iOS ATS enabled for local HTTP testing.
  - File: `mobile/ios/Runner/Info.plist`
- iOS deployment targets aligned to 16.0.
  - Files:
    - `mobile/ios/Runner.xcodeproj/project.pbxproj`
    - `mobile/ios/Podfile`
- Added development-only user reset endpoint for local auth recovery.
  - File: `backend/Controllers/AuthController.cs`
- Added Firebase debug endpoints.
  - File: `backend/Program.cs`
- Added FCM HTTP v1 fallback for diagnostics.
  - File: `backend/Services/PriceCheckJob.cs`

## Verified Facts
- Firebase service account file is present and configured in appsettings.
- Firebase Cloud Messaging API enabled (confirmed by user).
- IAM role check completed (confirmed by user).
- APNs key exists in Firebase Console (confirmed by user).

## Most Probable Remaining Root Causes
1. Bundle ID mismatch across Firebase iOS app, Xcode Runner target, and plist.
2. Apple Developer App ID missing Push Notifications capability.
3. APNs key metadata mismatch (Key ID / Team ID).
4. Old device token still in DB (app not reinstalled after APNs updates).

## First Steps for Next Session
1. Verify bundle ID equality in all 3 places:
   - Firebase iOS app bundle id
   - Xcode Runner bundle id
   - iOS GoogleService-Info.plist BUNDLE_ID
2. Verify Apple Developer > Identifiers > App ID has Push Notifications enabled.
3. Re-check Firebase APNs upload values:
   - `.p8`
   - Key ID
   - Team ID
4. Delete iOS app from device, reinstall, allow notifications, regenerate token, re-run test.

## Useful Test Command
Use local login token and run test notification:

```bash
cd /Users/cufica/Documents/price-tracker && \
TOKEN=$(curl -s -X POST http://localhost:5254/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"sinansengun61@gmail.com","password":"123456"}' \
  | python3 -c 'import sys,json; print(json.load(sys.stdin).get("token",""))') && \
FIRST_ID=$(curl -s http://localhost:5254/api/products -H "Authorization: Bearer $TOKEN" \
  | python3 -c 'import sys,json; d=json.load(sys.stdin); print(d[0]["id"] if d else "")') && \
curl -s -i -X POST "http://localhost:5254/api/products/${FIRST_ID}/test-notification" \
  -H "Authorization: Bearer $TOKEN"
```

## Notes
- Some debug helpers were added for diagnosis; remove before production hardening.
- Continue from APNs/Firebase iOS chain checks first; backend auth path is already validated.
