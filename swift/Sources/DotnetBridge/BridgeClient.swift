import Foundation
import CDni

/// Thread-safe facade over the in-process .NET HTTP bridge.
/// `ensureStarted()` calls the (idempotent) C exports; requests go over loopback.
public actor BridgeClient {
    /// A shared, process-wide client over the default `URLSession`.
    public static let shared = BridgeClient()

    private let session: URLSession
    private var initialized = false
    private var port = 0

    /// One capability token per process, sent on every request as `X-DNI-Auth`. The .NET side
    /// reads the same value from the `DNI_AUTH_TOKEN` environment variable at initialize. It is a
    /// process-global static (not per-instance, not regenerated on reconnect) because the .NET
    /// runtime configures it once at init — a regenerated token would 401 itself after a re-bind.
    private static let authToken: String = {
        var bytes = [UInt8](repeating: 0, count: 32)        // 256 bits of CSPRNG randomness
        for i in bytes.indices { bytes[i] = UInt8.random(in: 0...255) }
        return Data(bytes).base64EncodedString()
    }()

    /// Creates a client.
    /// - Parameter session: the `URLSession` used for loopback requests (defaults to `.shared`).
    public init(session: URLSession = .shared) { self.session = session }

    /// Initialize the engine once, then (re)start the loopback server.
    /// dni_http_start is idempotent; re-calling refreshes the port after an iOS
    /// background suspend kills the listener. Blocking C calls run off-actor.
    public func ensureStarted() async throws {
        if !initialized {
            // Hand the capability token to .NET out-of-band (no ABI change): set it in the
            // environment before the first dni_initialize reads it. Re-setting the same value on a
            // later restart is harmless.
            setenv("DNI_AUTH_TOKEN", Self.authToken, 1)
            let rc = await Task.detached(priority: .userInitiated) { Int(dni_initialize()) }.value
            guard rc == 0 else { throw BridgeError.status(rc) }
            initialized = true
        }
        let p = await Task.detached(priority: .userInitiated) { Int(dni_http_start()) }.value
        guard p > 0 else { throw BridgeError.status(p) }
        port = p
    }

    /// Issue a request to a user-defined route. Returns (body, httpStatus).
    ///
    /// If the loopback connection is refused — the listener was torn down by an iOS
    /// background-suspend or a server-side accept-loop reset — this forces a fresh
    /// `ensureStarted()` and retries exactly once. The retry is intentionally limited
    /// to `cannotConnectToHost` (a refused TCP connect, meaning the request never
    /// reached the server), so a non-idempotent POST is never silently re-sent after a
    /// request that may have already been processed.
    public func request(_ method: String, _ path: String,
                        body: Data? = nil,
                        contentType: String = "application/json") async throws -> (Data, Int) {
        do {
            return try await send(method, path, body: body, contentType: contentType)
        } catch let error as URLError where error.code == .cannotConnectToHost {
            // Listener gone; the connect was refused so nothing ran. Re-bind and retry once.
            initialized = false
            port = 0
            return try await send(method, path, body: body, contentType: contentType)
        }
    }

    private func send(_ method: String, _ path: String,
                      body: Data?, contentType: String) async throws -> (Data, Int) {
        try await ensureStarted()
        guard let url = URL(string: "http://127.0.0.1:\(port)\(path)") else {
            throw BridgeError.badURL(path)
        }
        var req = URLRequest(url: url)
        req.httpMethod = method
        req.setValue(Self.authToken, forHTTPHeaderField: "X-DNI-Auth")
        if let body {
            req.httpBody = body
            req.setValue(contentType, forHTTPHeaderField: "Content-Type")
        }
        let (data, resp) = try await session.data(for: req)
        return (data, (resp as? HTTPURLResponse)?.statusCode ?? -1)
    }

    /// GET `path`, returning the response body. Throws ``BridgeError/http(status:body:)``
    /// (carrying the server's response body) on any non-200 status.
    /// - Parameter path: the route path, e.g. `/api/customers`.
    public func get(_ path: String) async throws -> Data {
        let (data, code) = try await request("GET", path)
        guard code == 200 else { throw BridgeError.http(status: code, body: data) }
        return data
    }

    /// POST `body` to `path`, returning the response body. Throws
    /// ``BridgeError/http(status:body:)`` (carrying the server's response body) on any
    /// non-2xx status.
    /// - Parameters:
    ///   - path: the route path, e.g. `/api/customers`.
    ///   - body: the request body bytes (typically JSON).
    public func post(_ path: String, body: Data) async throws -> Data {
        let (data, code) = try await request("POST", path, body: body)
        guard (200..<300).contains(code) else { throw BridgeError.http(status: code, body: data) }
        return data
    }

    /// Stop the server and release the engine. Call on teardown.
    public func shutdown() async {
        await Task.detached { dni_http_stop(); dni_shutdown() }.value
        initialized = false
        port = 0
    }
}
