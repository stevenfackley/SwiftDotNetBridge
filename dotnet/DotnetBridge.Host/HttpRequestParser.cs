using System;
using System.Collections.Generic;
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

        var (path, query) = SplitTarget(target);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? line;
        while (!string.IsNullOrEmpty(line = await ReadLineAsync(stream, maxLineBytes, ct).ConfigureAwait(false)))
        {
            if (headers.Count >= maxHeaderCount)
                throw new BridgeProtocolException(431, "Too many request headers");
            var idx = line!.IndexOf(':');
            if (idx <= 0) continue;
            headers[line.AsSpan(0, idx).Trim().ToString()] = line.AsSpan(idx + 1).Trim().ToString();
        }

        var body = Array.Empty<byte>();
        if (headers.TryGetValue("Content-Length", out var clRaw) &&
            int.TryParse(clRaw, out var len) && len > 0)
        {
            if (len > maxBodyBytes)
                throw new BridgeProtocolException(413,
                    "Request body of " + len + " bytes exceeds the " + maxBodyBytes + "-byte limit",
                    pendingBodyBytes: len);
            body = await ReadExactAsync(stream, len, ct).ConfigureAwait(false);
        }

        return new BridgeRequest(method, path, query, headers, body,
            new Dictionary<string, string>());
    }

    private static (string Path, string Query) SplitTarget(string target)
    {
        var q = target.IndexOf('?');
        if (q < 0) return (Uri.UnescapeDataString(target), string.Empty);
        return (Uri.UnescapeDataString(target[..q]), target[(q + 1)..]);
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
