# SwiftDotNetBridge

Embed a **.NET 10 library inside a Swift app** and call into it over an in-process loopback HTTP
server — no Kestrel, no external process, nothing leaves the device. Shipped as a Swift package
wrapping a NativeAOT `.xcframework`.

> **Why this exists:** on Apple platforms .NET runs as a guest runtime with no ASP.NET hosting under
> NativeAOT (Kestrel has no mobile runtime pack). A raw `TcpListener` on `127.0.0.1` plus a
> four-function C ABI is the smallest thing that actually works — and keeps the native surface
> trivially correct.

## How it works (30 seconds)

1. Your business logic implements `IBridgeModule` and declares HTTP routes.
2. It compiles into a **.NET 10 NativeAOT shared library** (`dni.dylib`) that hosts a loopback
   HTTP/1.1 server. Four C exports control it: `dni_initialize`, `dni_http_start` (returns a port),
   `dni_http_stop`, `dni_shutdown`.
3. The dylib is packaged as `CDni.xcframework` and consumed via SwiftPM. Swift's `BridgeClient`
   talks to `http://127.0.0.1:<port>` with `URLSession`.

See **[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)** for diagrams and the full design.

## Repository layout

| Path | What |
|---|---|
| `dotnet/DotnetBridge.Abstractions` | The contract you implement (`netstandard2.0;net10.0`). |
| `dotnet/DotnetBridge.Host` | Reusable server engine (HTTP parser/writer, TcpListener, runtime). |
| `dotnet/Sample.CrudModule` | Example: Customers CRUD over a mocked `ICustomerStore`. |
| `dotnet/DotnetBridge.Native` | NativeAOT publish head — the only place the C exports live. |
| `dotnet/abi/dni.h` | The C ABI header. |
| `swift/` | SwiftPM package: `CDni` binary target + `BridgeClient` wrapper. |
| `build/` | `build-xcframework.sh`, `verify-native.sh`, `module.modulemap`. |
| `examples/SampleApp` | Minimal SwiftUI app. |
| `docs/` | Architecture, interop contract, AOT caveats, Mac build runbook. |

## Quickstart

**1. Develop & unit-test (any OS):**
```bash
dotnet test
```

**2. Build the framework (macOS only — NativeAOT can't cross-compile):**
```bash
./build/build-xcframework.sh      # -> swift/Frameworks/CDni.xcframework
./build/verify-native.sh          # smoke test: exports + curl /health -> ok
cd swift && swift test            # Swift round-trip against the embedded .NET
```

**3. Use it in your app:** add the `swift/` package, set `CDni.xcframework` to **Embed & Sign**, then:
```swift
let client = BridgeClient()
let json = try await client.get("/api/customers")
```

## Make it yours

- **Add endpoints:** `routes.MapGet(...)` in your module — no ABI or Swift change.
- **Swap the data layer:** implement `ICustomerStore` (mock for tests, EF Core/Dapper/HTTP for prod).
- **Add a module:** implement `IBridgeModule` and register it in `DotnetBridge.Native/Bootstrap.cs`.

## The .NET 10 + .NET Standard split

The runtime host (`DotnetBridge.Native`) is **.NET 10 + NativeAOT** — the only thing that can emit a
native library. The portable contract you implement (`DotnetBridge.Abstractions`) and your module
multi-target **`netstandard2.0;net10.0`**, so the same business logic also runs in servers, desktop
apps, or older runtimes. NativeAOT can *consume* a `netstandard2.0` dependency; it just can't *be*
`netstandard`.

## Requirements

.NET 10 SDK · Xcode (for the framework build) · Swift 6 · iOS 15+ / macOS 12+ / Mac Catalyst 15+.

## License

MIT — see [LICENSE](LICENSE).
