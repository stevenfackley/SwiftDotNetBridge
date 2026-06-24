#!/usr/bin/env bash
# On-Mac smoke test: publish macOS dylib, confirm dni_* exports, start server, curl /health.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJ="$ROOT/dotnet/DotnetBridge.Native/DotnetBridge.Native.csproj"
WORK="$ROOT/build/out/verify"; rm -rf "$WORK"; mkdir -p "$WORK"

dotnet publish "$PROJ" -c Release -r osx-arm64 -o "$WORK" -p:UseAppHost=false
LIB="$(ls "$WORK"/*.dylib | head -n1)"
echo "== exports =="; nm -gU "$LIB" | grep dni_ || { echo "MISSING dni_ exports"; exit 1; }

cat > "$WORK/harness.c" <<'C'
#include <stdio.h>
#include <stdlib.h>
#include "dni.h"
int main(void) {
    if (dni_initialize() != 0) { printf("init failed\n"); return 1; }
    int port = dni_http_start();
    if (port <= 0) { printf("start failed: %d\n", port); return 1; }
    printf("PORT=%d\n", port);
    char cmd[160]; snprintf(cmd, sizeof cmd, "curl -s http://127.0.0.1:%d/health", port);
    int rc = system(cmd); printf("\n");
    dni_http_stop(); dni_shutdown();
    return rc;
}
C
clang "$WORK/harness.c" -I "$ROOT/dotnet/abi" "$LIB" -o "$WORK/harness"
DYLD_LIBRARY_PATH="$WORK" "$WORK/harness"
echo "== expected: PORT=<n> then 'ok' =="
