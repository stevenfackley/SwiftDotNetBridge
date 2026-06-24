import Foundation
import CDni

/// Thread-safe facade over the in-process .NET HTTP bridge.
/// `ensureStarted()` calls the (idempotent) C exports; requests go over loopback.
public actor BridgeClient {
    public static let shared = BridgeClient()

    private let session: URLSession
    private var initialized = false
    private var port = 0

    public init(session: URLSession = .shared) { self.session = session }

    /// Initialize the engine once, then (re)start the loopback server.
    /// dni_http_start is idempotent; re-calling refreshes the port after an iOS
    /// background suspend kills the listener. Blocking C calls run off-actor.
    public func ensureStarted() async throws {
        if !initialized {
            let rc = await Task.detached(priority: .userInitiated) { Int(dni_initialize()) }.value
            guard rc == 0 else { throw BridgeError.status(rc) }
            initialized = true
        }
        let p = await Task.detached(priority: .userInitiated) { Int(dni_http_start()) }.value
        guard p > 0 else { throw BridgeError.status(p) }
        port = p
    }

    /// Issue a request to a user-defined route. Returns (body, httpStatus).
    public func request(_ method: String, _ path: String,
                        body: Data? = nil,
                        contentType: String = "application/json") async throws -> (Data, Int) {
        try await ensureStarted()
        guard let url = URL(string: "http://127.0.0.1:\(port)\(path)") else {
            throw BridgeError.badURL(path)
        }
        var req = URLRequest(url: url)
        req.httpMethod = method
        if let body {
            req.httpBody = body
            req.setValue(contentType, forHTTPHeaderField: "Content-Type")
        }
        let (data, resp) = try await session.data(for: req)
        return (data, (resp as? HTTPURLResponse)?.statusCode ?? -1)
    }

    public func get(_ path: String) async throws -> Data {
        let (data, code) = try await request("GET", path)
        guard code == 200 else { throw BridgeError.http(code) }
        return data
    }

    public func post(_ path: String, body: Data) async throws -> Data {
        let (data, code) = try await request("POST", path, body: body)
        guard (200..<300).contains(code) else { throw BridgeError.http(code) }
        return data
    }

    /// Stop the server and release the engine. Call on teardown.
    public func shutdown() async {
        await Task.detached { dni_http_stop(); dni_shutdown() }.value
        initialized = false
        port = 0
    }
}
