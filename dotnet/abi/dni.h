/*
 * dni.h — C ABI for the Swift <-> .NET 10 in-process HTTP bridge.
 *
 * Produced by DotnetBridge.Native published with NativeAOT (NativeLib=Shared).
 * All functions are plain C, no C++ name mangling. Symbols are resolved at
 * link time against the embedded dylib (no dlopen).
 *
 * THREADING: dni_http_start/stop block briefly on the calling (Swift) thread.
 *   The accept loop and route handlers run on .NET thread-pool threads.
 * LIFECYCLE: dni_initialize  (once, idempotent)
 *         -> dni_http_start  (idempotent; returns the live port)
 *         -> dni_http_stop
 *         -> dni_shutdown    (last; releases the server)
 * iOS NOTE: the OS tears down the listener when the app is suspended. The Swift
 *   host MUST call dni_http_start again on every foreground-resume to obtain a
 *   valid (possibly new) port before issuing requests. Start is idempotent.
 */
#ifndef DNI_H
#define DNI_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/* Status codes (returned as int32_t). Ports are positive; errors negative. */
#define DNI_OK                 0
#define DNI_NOT_INITIALIZED   -1
#define DNI_INVALID_ARGUMENT  -2
#define DNI_ALREADY_RUNNING   -4
#define DNI_INTERNAL          -5

/* Initialize the engine + register routes once. Idempotent. Returns DNI_OK or negative. */
int32_t dni_initialize(void);

/* Start the loopback HTTP server on 127.0.0.1:0. Returns the bound port (>0)
 * or a negative status. Idempotent: returns the cached port if already running. */
int32_t dni_http_start(void);

/* Stop the loopback server. Returns DNI_OK. Safe to call when not running. */
int32_t dni_http_stop(void);

/* Shut down the engine and release the server. Never throws. Call last. */
void    dni_shutdown(void);

#ifdef __cplusplus
}
#endif

#endif /* DNI_H */
