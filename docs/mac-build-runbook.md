# macOS Build Runbook

NativeAOT can't cross-OS compile, so the native artifacts are produced on a Mac. This is the
end-to-end recipe.

## Prerequisites (on the Mac)
- .NET 10 SDK (`dotnet --version` → 10.0.x), matching `global.json`.
- Xcode (command-line tools: `xcode-select -p` must resolve; `xcodebuild -version`).
- The repo checked out with **LF** line endings (the repo's `.gitattributes` enforces this).

## 1. Build the xcframework
```bash
cd <repo>
chmod +x build/*.sh
./build/build-xcframework.sh
# -> swift/Frameworks/CDni.xcframework  (ios-arm64, ios-sim, macos, maccatalyst slices)
```
Override the signing identity for distribution (default is ad-hoc `-`):
```bash
CODESIGN_IDENTITY="Apple Development: You (TEAMID)" ./build/build-xcframework.sh
```

## 2. Smoke-test the native image
```bash
./build/verify-native.sh
# Expect: a list of dni_* exports, then "PORT=<n>" and "ok".
```

## 3. Run the Swift round-trip tests
```bash
cd swift && swift test
# Expect: testHealth, testListSeededCustomers, testCreateCustomer all pass.
```

## 4. Build the sample app
Open the sample in Xcode (see `examples/SampleApp/README.md`), set `CDni.xcframework` to
**Embed & Sign**, and run on a simulator, device, or macOS.

---

## Working over SSH (headless Mac, e.g. a Mac mini)

`dotnet` and Xcode tools are usually only on the **login** PATH, so wrap remote commands in a login
shell:
```bash
ssh you@your-mac "zsh -lc 'cd <repo> && ./build/build-xcframework.sh'"
```

### Code-signing over SSH: `errSecInternalComponent`
A fresh SSH session's login keychain is **locked**, so `xcodebuild`/`codesign` fail with
`errSecInternalComponent`. Unlock it first, in the same command:
```bash
# store the password once, 0600
ssh you@your-mac "zsh -lc '
  security unlock-keychain -p \"\$(cat ~/.kc_pw)\" ~/Library/Keychains/login.keychain-db &&
  cd <repo> && xcodebuild ... '"
```
GUI login unlocks the keychain for GUI processes, but each independent SSH session starts locked —
there is no persistent password-free fix.

## Speed tip — device-only iteration
Building all four platform slices is the slow path. For fast on-device iteration, trim
`build-xcframework.sh` to publish only `ios-arm64` and create a single-slice xcframework (valid for
device builds; simulator needs the simulator slice). Roughly halves build time.
