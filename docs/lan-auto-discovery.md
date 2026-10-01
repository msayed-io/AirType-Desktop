# LAN auto-discovery

AirType Desktop implements the Android discovery contract over IPv4 UDP while preserving the existing authenticated WebSocket pairing handshake.

## Transport

- Listener: `0.0.0.0:53018/UDP`
- Active while the Pair Phone window is open and, when trusted devices exist, automatically while Desktop is waiting for trusted reconnect.
- Direct response: unicast to the source IP and source port of a valid request.
- Proactive advertisement: IPv4 broadcast to `255.255.255.255:53018` every 2.5 seconds.
- Pauses on successful pairing and resumes after an unexpected trusted-device disconnect. Explicit Desktop disconnect and process shutdown stop it.

Request:

```json
{"type":"livetype_discover"}
```

Response:

```json
{
  "type":"livetype_beacon",
  "name":"Mohamed-PC",
  "host":"192.168.1.15",
  "port":53017,
  "sessionId":"current-session-id"
}
```

Only an exact JSON `type` value is accepted; substring matches, malformed JSON, oversized datagrams, and non-LAN source addresses are ignored. Replies from one source IP are rate-limited. The advertised TCP port is the server's actual bound port, including fallback when the configured port is busy.

## Security boundary

Discovery reveals routing metadata and the temporary runtime session identifier; it never reveals the PIN, persistent device token, text content, or server secret. First-time PIN and subsequent `clientId + token` authentication happen only in the WebSocket `pair_request`. UDP discovery therefore does not bypass pairing authentication.

## Firewall

The executable needs private-network access for both the actual WebSocket TCP port (normally 53017) and UDP 53018. AirType remains `asInvoker` and does not silently elevate or install firewall rules. Windows may show its standard application-access prompt; the user should allow AirType on Private networks. Guest Wi-Fi/AP isolation can still block peer discovery.

## Scope decision

mDNS is not included in this release. UDP is the Android client's recommended direct contract, requires no additional native dependency, and is covered by integration tests. mDNS can be added later without changing this payload or the WebSocket protocol.
