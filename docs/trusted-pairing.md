# Permanent trusted pairing

## Wire contract

### First-time PIN/QR authentication

```json
{
  "type": "pair_request",
  "sessionId": "current-session-id-from-beacon",
  "pin": "123456",
  "deviceName": "Android · Samsung S24",
  "clientId": "android_9a8b7c6d5e4f3a2b"
}
```

After the PIN is verified, Desktop creates a cryptographically random 256-bit device token, stores only its SHA-256 hash, and replies:

```json
{
  "type": "session_ready",
  "ok": true,
  "token": "64-character-lowercase-hex-device-token",
  "heartbeatIntervalMs": 2500
}
```

The Android client persists `clientId + token` securely.

### Trusted reconnect

After reading the current session ID from UDP discovery, Android sends:

```json
{
  "type": "pair_request",
  "sessionId": "current-session-id-from-beacon",
  "clientId": "android_9a8b7c6d5e4f3a2b",
  "token": "saved-device-token",
  "deviceName": "Android · Samsung S24"
}
```

Desktop compares the SHA-256 token hash in fixed time. Success returns the same token and heartbeat interval. An invalid, missing, or revoked credential returns:

```json
{
  "type": "error",
  "errorCode": "bad_token",
  "errorMessage": "Device token is invalid or revoked"
}
```

A request containing a token is always treated as trusted authentication; it never falls back to a PIN in the same request. Android must send a new PIN request after `bad_token`.

## Lifecycle

- If trusted devices exist, Desktop starts the WebSocket server and UDP discovery automatically on startup.
- Discovery advertises a fresh runtime session ID while disconnected.
- Successful connection pauses discovery.
- Unexpected phone disconnect resumes trusted discovery automatically.
- Explicit Desktop Disconnect suppresses automatic reconnect until the user starts pairing again or restarts Desktop.
- “Forget all trusted devices” removes all hashes, stops discovery, and disconnects the current phone.

## Storage

Trusted records are held in `AppSettings.TrustedDevices` and contain `clientId`, display name, SHA-256 token hash, creation time, and last-seen time. Raw device tokens are never written to disk or logs. The list is de-duplicated, sanitized on load, and capped at 20 devices.

The permanent bearer token travels over the existing local `ws://` transport. It should therefore be handled by Android as a secret and stored in platform secure storage. This mechanism provides persistent device trust and revocation; it does not claim the end-to-end cryptographic properties of WhatsApp Web.
