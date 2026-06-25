import Foundation

/// Errors surfaced by ``BridgeClient``.
public enum BridgeError: Error, Sendable, Equatable {
    /// A negative `DNI_*` status code returned by a C export (init/start failure).
    case status(Int)
    /// A non-success HTTP status from a route handler, with the raw response body.
    /// The body is usually a JSON error envelope (e.g. `{"error":"..."}`) explaining
    /// the failure — surfaced here instead of discarded so callers can act on it.
    case http(status: Int, body: Data)
    /// Could not build a request URL for the given path.
    case badURL(String)
    /// Too little iOS background execution time remains to safely start work; retry when foregrounded.
    case backgroundExpiringSoon
}
