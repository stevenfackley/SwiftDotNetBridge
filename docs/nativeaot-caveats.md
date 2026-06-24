# NativeAOT Caveats (and how this design avoids each)

The gotchas that bite Swift↔.NET NativeAOT bridges, with the specific mitigation baked into this
repo. Sources: Microsoft Learn (Native AOT deployment, iOS-like platforms, interop) and the project's
own build configuration.

## 1. Exports are only emitted from the *published* assembly
`[UnmanagedCallersOnly]` methods in a **project reference or NuGet package are NOT exported** — only
those in the assembly being published with `PublishAot`. → All `dni_*` exports live in
`DotnetBridge.Native/Exports.cs` (the publish head), delegating to `BridgeRuntime` in the Host library.

## 2. `[UnmanagedCallersOnly]` constraints
Must be `static`, non-generic, not in a generic type, and take **only blittable args**. → Our four
exports are parameterless and return `int`/`void`. No strings/structs cross the boundary.

## 3. Exceptions must not cross the ABI
A managed exception escaping into native code is undefined behavior. → Every export wraps its body in
`try/catch` and maps failures to `DNI_INTERNAL` (`-5`); `dni_shutdown` swallows everything.

## 4. Trimming disables reflection-based JSON
With trimming on (implied by AOT), `System.Text.Json`'s reflection path is turned off. → DTOs use a
source-generated `JsonSerializerContext` (`CustomerJsonContext`); `DotnetBridge.Native` sets
`JsonSerializerIsReflectionEnabledByDefault=false` to fail fast if anything regresses.

## 5. No dynamic loading / no `Reflection.Emit`
`Assembly.LoadFile`, `Reflection.Emit`, and runtime codegen are unavailable. → Modules are registered
**statically** in `Bootstrap.cs`; nothing is loaded at runtime.

## 6. `InvariantGlobalization`
Named cultures (`new CultureInfo("en-US")`) throw; `ToUpper/ToLower` become ASCII-only. → The repo
sets `InvariantGlobalization=true` and uses `CultureInfo.InvariantCulture` (e.g. timestamp formatting
in the mock store).

## 7. No cross-OS compilation
NativeAOT **cannot** build an Apple binary from Windows/Linux. → The iOS/macOS/Mac Catalyst dylibs and
the xcframework are built on macOS (`build/build-xcframework.sh`). Develop + unit-test anywhere; ship
the native artifact from a Mac.

## 8. iOS requires a `.framework`, not a bare dylib
A raw `.dylib` in an xcframework is valid only for macOS dynamic linking; **iOS needs a `.framework`
bundle**. → The build script wraps each dylib as `CDni.framework` (binary + `Headers/dni.h` +
`Modules/module.modulemap`) before `xcodebuild -create-xcframework`.

## 9. `@rpath` install name + re-sign
NativeAOT's default install name is absolute, which can't be embedded; and `install_name_tool`
invalidates the signature. → The script runs `install_name_tool -id @rpath/CDni.framework/CDni` then
`codesign --force --timestamp`. Consumers set the xcframework to **Embed & Sign**.

## 10. Deterministic output name
NativeAOT prefixes `lib` on Unix outputs (`libdni.dylib`). → `UseNativeLibPrefix=false` keeps the
artifact named `dni.dylib`; the build glob (`ls *.dylib`) is tolerant either way.

## 11. Bitcode is gone
Deprecated since Xcode 14; `ENABLE_BITCODE` is a no-op in current Xcode. → Nothing to do; don't
re-enable it.

## 12. Module map, not bridging header
SwiftPM **library** targets reject Objective-C bridging headers — only a Clang module map exposes the
C API as an importable module. → `build/module.modulemap` declares `framework module CDni`, shipped
inside each `.framework`, so consumers `import CDni`.

## 13. A guest runtime has no console — don't swallow errors silently
Because exceptions must not cross the ABI (caveat #3), every export and every connection handler
traps broadly. The trap is correct; a *silent* trap is not — a bare `-5` is unsupportable in the
field. → All swallowed failures route through `BridgeDiagnostics.OnError` (an `Action<string,
Exception?>`), defaulting to `Console.Error`, which surfaces in `os_log`/the device console. Set it
to forward bridge-internal errors into your app's logging stack. This is a managed-side seam, so the
C ABI stays frozen (caveats #1–#2).
