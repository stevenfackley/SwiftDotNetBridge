# Interop Contract

The frozen agreement between the Swift host and the .NET NativeAOT library. Treat the C ABI as
versioned API — changing a signature is a breaking change.

## C ABI (from `dotnet/abi/dni.h`)

| Entry point | Signature | Returns | Ownership / notes |
|---|---|---|---|
| `dni_initialize` | `int32_t (void)` | `DNI_OK` (0) or negative | Builds the route table from registered modules. Idempotent. |
| `dni_http_start` | `int32_t (void)` | bound port `>0`, or negative | Binds `127.0.0.1:0`. Idempotent — returns the cached port if already running. |
| `dni_http_stop` | `int32_t (void)` | `DNI_OK` | Stops the listener. Safe to call when not running. |
| `dni_shutdown` | `void (void)` | — | Releases the server. Never throws. Call last. |

All four are `static`, parameterless, and return only blittable types — the NativeAOT
`[UnmanagedCallersOnly]` constraints. **Managed exceptions never cross the boundary**: each export
traps and maps to `DNI_INTERNAL`, and reports the cause through `BridgeDiagnostics.OnError`
(default sink: `stderr`, which surfaces in `os_log`) so a `-5` is observable rather than silent.
The ABI itself is unchanged — diagnostics are a managed-side seam, so the C surface stays frozen.

### Status codes

| Constant | Value |
|---|---|
| `DNI_OK` | `0` |
| `DNI_NOT_INITIALIZED` | `-1` |
| `DNI_INVALID_ARGUMENT` | `-2` |
| `DNI_ALREADY_RUNNING` | `-4` |
| `DNI_INTERNAL` | `-5` |

A positive return from `dni_http_start` is the TCP port; any negative value is a status code.

## Lifecycle

```
dni_initialize()   // once, idempotent
   -> dni_http_start()   // idempotent; returns the live port
   -> ... HTTP requests over 127.0.0.1:port ...
   -> dni_http_stop()
   -> dni_shutdown()     // last
```

**iOS rule:** the OS tears down the listener when the app is suspended. The Swift host MUST call
`dni_http_start()` again on every foreground-resume to obtain a (possibly new) port before issuing
requests. `BridgeClient.ensureStarted()` does this automatically by calling it before each request.

**Lifecycle hardening.** Each (re)bind is tagged with a generation, so a stale accept-loop from an
old epoch can never tear down a newer listener. `dni_http_stop`/`dni_shutdown` stop accepting
immediately, let in-flight requests finish within `BridgeLimits.GracefulStopTimeout` (2&#160;s), then
abort the rest. Shutdown is re-initializable (it returns to the uninitialized state); calling
`dni_http_start` before `dni_initialize` deterministically returns `DNI_NOT_INITIALIZED` rather than
half-starting. On iOS, `BridgeClient` refuses to start when `< 5 s` of background time remains,
throwing `BridgeError.backgroundExpiringSoon` so the caller retries when foregrounded.

## Payload contract

Request/response bodies **never cross the C ABI** — they travel as HTTP bytes over the loopback
socket. The bridge core is therefore JSON-agnostic:

- A route handler receives a `BridgeRequest` (method, path, query, headers, raw `byte[]` body,
  route values) and returns a `BridgeResponse` (status, content-type, raw `byte[]` body).
- Your module performs its own (de)serialization. Use **`System.Text.Json` source generation**
  (a `JsonSerializerContext`) — reflection-based JSON is disabled under trimming/AOT.

## Route contract

Implement `IBridgeModule` and register routes on the `RouteTable`:

```csharp
public sealed class MyModule : IBridgeModule
{
    public void Configure(RouteTable routes)
    {
        routes.MapGet("/things/{id}", (req, ct) => { /* ... */ });
        routes.MapPost("/things", (req, ct) => { /* ... */ });
    }
}
```

- Patterns support literal segments and `{name}` parameters; matched values arrive in
  `request.RouteValues`.
- Register your module instance(s) in `DotnetBridge.Native/Bootstrap.cs` (NativeAOT forbids runtime
  assembly loading, so modules are wired at compile time).

## HTTP-layer error handling & limits

The loopback transport is the one place untrusted, attacker-shaped bytes enter the process, so the
reader is bounded and the server fails closed:

| Condition | Response | Knob (`BridgeLimits`) |
|---|---|---|
| Request/header line too long | `431` | `MaxLineBytes` (8 KiB) |
| Too many headers | `431` | `MaxHeaderCount` (100) |
| `Content-Length` over limit | `413` (before allocating) | `MaxBodyBytes` (16 MiB) |
| `Transfer-Encoding`, duplicate framing header, or malformed `Content-Length` | `400` | — |
| Too many concurrent connections | `503` (load shed) | `MaxConcurrentConnections` (16) |
| Under iOS memory pressure (degraded mode) | `503` (shed all) | `BridgeMemory.Degraded` |
| Client stalls mid-request | connection dropped | `ReadTimeout` (30 s) |
| Path matches, method does not | `405` | — |
| No matching route | `404` | — |
| Route handler throws | `500` (generic body; detail → `BridgeDiagnostics`) | — |
| `Host` not loopback, `CONNECT`, absolute-form target, or `%2f`/`%5c`/`.`/`..`/`//` in path | `400` | — |
| Token configured but `X-DNI-Auth` missing/wrong | `401` | `DNI_AUTH_TOKEN` env |

- A rejected oversized request is **bounded-drained** (≤ 64 KiB) before the connection closes, so the
  client reads the `413` cleanly instead of a TCP reset — without letting a huge body make the drain
  itself a DoS.
- A `500` never echoes the exception message or stack to the client; the full detail goes to
  `BridgeDiagnostics.OnError`, tagged with a `BridgeErrorClass`
  (Protocol/Connection/AcceptLoop/Handler/Lifecycle) for filtering. An `X-Request-ID` sent by the
  client is echoed back in the response and included in handler-error diagnostics, so a Swift-side
  failure correlates to a specific .NET log line.
- Only `Content-Length` framing is supported; `Transfer-Encoding` is rejected (it is ambiguous
  against `Content-Length` and a classic request-smuggling vector). `Expect: 100-continue` is
  honored — the server sends `100 Continue` before reading the body, so larger POSTs don't stall.
- **Memory pressure.** NativeAOT's GC doesn't react to iOS memory warnings on its own, so on Apple
  platforms the runtime wires the OS `dispatch` memory-pressure source to `BridgeMemory.OnMemoryPressure`:
  it forces a GC and enters degraded mode (sheds new connections with `503`) until pressure subsides —
  trading a few 503s for not getting Jetsam-killed. The Native head also builds with workstation,
  non-concurrent GC for a lower footprint.
- Set any `BridgeLimits` value (and `BridgeDiagnostics.OnError`) **before** `dni_http_start`.
- The sample module returns errors as a JSON envelope (`{"error":"..."}`), and the Swift client
  surfaces the response body on `BridgeError.http(status:body:)` rather than discarding it.

## Loopback authentication & request hardening

The loopback port is reachable by any same-device process, so the server treats it as untrusted:

- **Origin binding.** Every request must carry `Host: 127.0.0.1[:port]`; a spoofed/non-loopback
  `Host` is `400` (DNS-rebind defense). Only origin-form targets are accepted — `CONNECT`,
  absolute-form (`http://…`), and asterisk-form are rejected.
- **Path canonicalization.** Encoded separators (`%2f`/`%5c`), dot-segments (`.`/`..`), and interior
  duplicate slashes (`//`) are `400`, so route matching can't be tricked by a non-canonical path.
- **Capability token (optional).** When `DNI_AUTH_TOKEN` is set in the environment **before**
  `dni_initialize`, every request must present that token in `X-DNI-Auth`; the server compares it in
  constant time and returns `401` otherwise. The token never crosses the C ABI — the Swift host
  generates a fresh 256-bit per-launch token, `setenv`s it before init, and sends it on each request.
  When the variable is unset, auth is disabled (the default for tests/samples).

---

## Appendix — FFI / streaming transport (NOT part of the HTTP transport)

If you later add a direct-call or streaming transport (returning C strings, or per-token callbacks),
these are the rules the HTTP transport avoids. Documented here so the patterns aren't lost.

**Returned C strings (caller frees):**
```c
const char* dni_invoke(const char* utf8_json);  // heap UTF-8, or NULL
void        dni_string_free(const char* s);
```
Swift side (NULL-safe, frees on every path):
```swift
func invoke(_ json: String) -> String? {
    json.withCString { c in            // pointer valid ONLY inside the closure
        guard let out = dni_invoke(c) else { return nil }
        defer { dni_string_free(out) } // honor caller-must-free
        return String(cString: out)    // copies into Swift-owned storage
    }
}
```

**C function-pointer callbacks** (`@convention(c)` cannot capture context — bridge via `void*`):
```swift
// top-level, non-capturing trampoline
private func trampoline(_ ud: UnsafeMutableRawPointer?, _ i: Int32,
                        _ text: UnsafePointer<CChar>?, _ final: Int32) {
    let s = text.map { String(cString: $0) } ?? ""   // copy NOW; ptr invalid after return
    guard let ud else { return }
    let recv = Unmanaged<Receiver>.fromOpaque(ud).takeUnretainedValue()
    Task { @MainActor in recv.handle(index: i, text: s, isFinal: final != 0) }  // hop off the bg thread
}
// register: dni_register_cb(trampoline, Unmanaged.passRetained(receiver).toOpaque())
// balance the +1 with takeRetainedValue (or .release()) exactly once.
```

Header hygiene for a pointer-bearing ABI: wrap declarations in
`#pragma clang assume_nonnull begin/end` and mark genuinely-nullable pointers `_Nullable`, so Swift
imports honest optionals instead of implicitly-unwrapped ones.
