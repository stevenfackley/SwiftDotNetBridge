using System;

namespace DotnetBridge.Host;

/// <summary>
/// Thrown when a request is received but violates an HTTP framing or size limit
/// (oversized body, oversized/too-many headers, malformed request line). Carries the
/// <see cref="StatusCode"/> the server should return to the client. This is deliberately
/// distinct from a route-handler fault: a protocol violation is the client's fault and
/// maps to a 4xx, whereas a handler throwing is our fault and always maps to a 500.
/// </summary>
public sealed class BridgeProtocolException : Exception
{
    /// <summary>The HTTP status code to send for this protocol violation (e.g. 413, 431, 400).</summary>
    public int StatusCode { get; }

    /// <summary>
    /// Bytes the client will still transmit (e.g. the Content-Length of a rejected body). The
    /// server bounded-drains this before closing so the client can read the error response
    /// cleanly instead of getting a TCP reset. Zero when no further body is expected.
    /// </summary>
    public int PendingBodyBytes { get; }

    /// <summary>Create a protocol exception carrying the HTTP status the server should return.</summary>
    /// <param name="statusCode">The HTTP status code to send (typically 4xx).</param>
    /// <param name="message">A human-readable description of the violation.</param>
    /// <param name="pendingBodyBytes">Bytes the client will still send (for a bounded drain); 0 if none.</param>
    public BridgeProtocolException(int statusCode, string message, int pendingBodyBytes = 0) : base(message)
    {
        StatusCode = statusCode;
        PendingBodyBytes = pendingBodyBytes;
    }
}
