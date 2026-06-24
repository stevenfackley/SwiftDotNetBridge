# SampleApp — consuming DotnetBridge

A minimal SwiftUI app that calls the embedded .NET Customers CRUD module over the loopback bridge.

## Prerequisites

On a Mac, build the xcframework first (NativeAOT can't cross-compile from Windows):

```bash
./build/build-xcframework.sh
```

This produces `swift/Frameworks/CDni.xcframework`.

## Add the package

1. In Xcode: **File ▸ Add Package Dependencies… ▸ Add Local…** and select the `swift/` folder.
2. Add the **DotnetBridge** library product to your app target.
3. Select the app target ▸ **General ▸ Frameworks, Libraries, and Embedded Content** and ensure
   `CDni.xcframework` is set to **Embed & Sign** (it's a dynamic library — it must ship in the
   app bundle and be signed).
4. Drop `SampleApp.swift` into the target (or copy its `ContentView` into your own view).

## Run

Build & run for an iOS Simulator, an iOS device, or macOS, then:

- **GET /api/customers** — lists the seeded mock customers (Ada Lovelace, Alan Turing, Grace Hopper).
- **POST /api/customers** — creates "Nikola Tesla" and shows the returned JSON (HTTP 201).

## How it works

`BridgeClient` calls `dni_initialize()` + `dni_http_start()` (both idempotent) to start the in-process
.NET server on `127.0.0.1:<port>`, then issues `URLSession` requests to your routes. There is no
external server and nothing leaves the device — it's all one process, talking to itself over loopback.
See `docs/ARCHITECTURE.md` for the full picture.
