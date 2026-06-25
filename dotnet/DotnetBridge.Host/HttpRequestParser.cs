using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DotnetBridge.Abstractions;

namespace DotnetBridge.Host;

/// <summary>
/// Hand-rolled HTTP/1.1 reader. Supports the request line, headers, and a
/// Content-Length body. Deliberately small: loopback, single-purpose, AOT-safe.
/// Chunked transfer-encoding is NOT supported (clients use Content-Length).
/// <para>
/// This is the only place untrusted, attacker-shaped bytes enter the process, so every
/// unbounded dimension is capped: request/header line length and the body size throw a
/// <see cref="BridgeProtocolException"/> (431/413) instead of allocating without bound.
/// </para>
/// </summary>
public static class HttpRequestParser
{
    /// <summary>Reads one request using the process-wide <see cref="BridgeLimits"/>.</summary>
    public static Task<BridgeRequest?> ReadAsync(Stream stream, CancellationToken ct) =>
        ReadAsync(stream, BridgeLimits.MaxLineBytes, BridgeLimits.MaxHeaderCount, BridgeLimits.MaxBodyBytes, ct);

    /// <summary>
    /// Reads one request with explicit limits. Returns <see langword="null"/> if the client
    /// closed the connection or sent an empty/malformed request line.
    /// </summary>
    /// <param name="stream">The connection stream to read from.</param>
    /// <param name="maxLineBytes">Maximum bytes per request line or header line (else 431).</param>
    /// <param name="maxHeaderCount">Maximum number of headers (else 431).</param>
    /// <param name="maxBodyBytes">Maximum Content-Length body size (else 413).</param>
    /// <param name="ct">Cancellation (also used to enforce the read timeout).</param>
    /// <exception cref="BridgeProtocolException">The request violates a size limit.</exception>
    public static async Task<BridgeRequest?> ReadAsync(Stream stream,
        int maxLineBytes, int maxHeaderCount, int maxBodyBytes, CancellationToken ct)
    {
        var requestLine = await ReadLineAsync(stream, maxLineBytes, ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(requestLine)) return null;          // closed/empty

        var parts = requestLine!.Split(' ');
        if (parts.Length < 3) return null;                            // malformed
        var method = parts[0];
        var target = parts[1];

        // CONNECT (tunnel) has no place on a loopback HTTP server — reject it.
        if (string.Equals(method, "CONNECT", StringComparison.OrdinalIgnoreCase))
            throw new BridgeProtocolException(400, "CONNECT is not supported");

        var qIdx = target.IndexOf('?');
        var rawPath = qIdx < 0 ? target : target.Substring(0, qIdx);
        var query = qIdx < 0 ? string.Empty : target.Substring(qIdx + 1);

        // Origin-form only: reject absolute-form (proxy) and asterisk-form targets.
        if (rawPath.Length == 0 || rawPath[0] != '/')
            throw new BridgeProtocolException(400, "Only origin-form request targets are supported");
        // An encoded separator would decode into a path boundary the router never vetted — reject it.
        if (rawPath.Contains("%2f", StringComparison.OrdinalIgnoreCase) ||
            rawPath.Contains("%5c", StringComparison.OrdinalIgnoreCase))
            throw new BridgeProtocolException(400, "Encoded path separators are not allowed");

        var path = Uri.UnescapeDataString(rawPath);
        if (HasUnsafeSegments(path))
            throw new BridgeProtocolException(400, "Path contains empty, '.' or '..' segments");

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? line;
        while (!string.IsNullOrEmpty(line = await ReadLineAsync(stream, maxLineBytes, ct).ConfigureAwait(false)))
        {
            if (headers.Count >= maxHeaderCount)
                throw new BridgeProtocolException(431, "Too many request headers");
            var idx = line!.IndexOf(':');
            if (idx <= 0) continue;
            var name = line.AsSpan(0, idx).Trim().ToString();
            var value = line.AsSpan(idx + 1).Trim().ToString();
            // Reject a duplicate of any framing-sensitive header — conflicting/ambiguous framing
            // is the root of request smuggling. Non-sensitive duplicates keep last-wins semantics.
            if (headers.ContainsKey(name) && IsFramingHeader(name))
                throw new BridgeProtocolException(400, "Duplicate " + name + " header");
            headers[name] = value;
        }

        // We only support Content-Length framing. A Transfer-Encoding (e.g. chunked) request is
        // ambiguous against Content-Length and is a classic smuggling vector — reject it outright.
        if (headers.ContainsKey("Transfer-Encoding"))
            throw new BridgeProtocolException(400, "Transfer-Encoding is not supported; use Content-Length");

        var expectContinue = headers.TryGetValue("Expect", out var expect) &&
            expect.Trim().Equals("100-continue", StringComparison.OrdinalIgnoreCase);

        var body = Array.Empty<byte>();
        if (headers.TryGetValue("Content-Length", out var clRaw))
        {
            // Strict parse (digits only, no sign/whitespace); a malformed length is a 400, not a
            // silently-empty body — which would otherwise be a framing-ambiguity foothold.
            if (!int.TryParse(clRaw, NumberStyles.None, CultureInfo.InvariantCulture, out var len) || len < 0)
                throw new BridgeProtocolException(400, "Invalid Content-Length");

            if (len > maxBodyBytes)
                // If the client is waiting on 100-continue it has NOT sent the body yet, so there
                // is nothing to drain; otherwise the body is already inbound and gets drained.
                throw new BridgeProtocolException(413,
                    "Request body of " + len + " bytes exceeds the " + maxBodyBytes + "-byte limit",
                    pendingBodyBytes: expectContinue ? 0 : len);

            if (len > 0)
            {
                // Honor Expect: 100-continue — without this the client withholds the body while we
                // wait to read it, deadlocking until the read timeout (~30s).
                if (expectContinue)
                    await WriteContinueAsync(stream, ct).ConfigureAwait(false);
                body = await ReadExactAsync(stream, len, ct).ConfigureAwait(false);
            }
        }

        return new BridgeRequest(method, path, query, headers, body,
            new Dictionary<string, string>());
    }

    private static bool IsFramingHeader(string name) =>
        string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "Host", StringComparison.OrdinalIgnoreCase);

    private static readonly byte[] ContinueResponse = Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n");

    private static async Task WriteContinueAsync(Stream stream, CancellationToken ct)
    {
        await stream.WriteAsync(ContinueResponse.AsMemory(), ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// True if the decoded path has an interior empty segment (<c>//</c>), or a <c>.</c>/<c>..</c>
    /// segment — the dot-segment and duplicate-slash cases a canonicalizing router must reject so
    /// route matching can't be tricked by a non-canonical path. A single trailing slash is allowed.
    /// </summary>
    private static bool HasUnsafeSegments(string path)
    {
        var parts = path.Split('/');
        for (var i = 1; i < parts.Length; i++)   // skip the empty segment before the leading '/'
        {
            var p = parts[i];
            if (p.Length == 0 && i != parts.Length - 1) return true;   // interior '//'
            if (p == "." || p == "..") return true;
        }
        return false;
    }

    private static async Task<string?> ReadLineAsync(Stream stream, int maxLineBytes, CancellationToken ct)
    {
        var sb = new StringBuilder();
        var one = new byte[1];
        var prevCr = false;
        var any = false;
        while (true)
        {
            var n = await stream.ReadAsync(one.AsMemory(0, 1), ct).ConfigureAwait(false);
            if (n == 0) return any ? sb.ToString() : null;            // EOF
            any = true;
            var c = (char)one[0];
            if (c == '\n') { if (prevCr) sb.Length--; return sb.ToString(); }
            if (sb.Length >= maxLineBytes)
                throw new BridgeProtocolException(431,
                    "Request line/header exceeds the " + maxLineBytes + "-byte limit");
            sb.Append(c);
            prevCr = c == '\r';
        }
    }

    private static async Task<byte[]> ReadExactAsync(Stream stream, int count, CancellationToken ct)
    {
        var buf = new byte[count];
        var read = 0;
        while (read < count)
        {
            var n = await stream.ReadAsync(buf.AsMemory(read, count - read), ct).ConfigureAwait(false);
            if (n == 0) break;                                        // truncated
            read += n;
        }
        if (read != count) Array.Resize(ref buf, read);
        return buf;
    }
}
