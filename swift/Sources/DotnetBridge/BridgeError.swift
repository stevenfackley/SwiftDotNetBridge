public enum BridgeError: Error, Sendable, Equatable {
    /// A negative DNI_* status code returned by a C export (init/start failure).
    case status(Int)
    /// A non-success HTTP status from a route handler.
    case http(Int)
    /// Could not build a request URL for the given path.
    case badURL(String)
}
