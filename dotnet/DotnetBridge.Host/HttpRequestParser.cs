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
/// </summary>
public static class HttpRequestParser
{
    public static async Task<BridgeRequest?> ReadAsync(Stream stream, CancellationToken ct)
    {
        var requestLine = await ReadLineAsync(stream, ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(requestLine)) return null;          // closed/empty

        var parts = requestLine!.Split(' ');
        if (parts.Length < 3) return null;                            // malformed
        var method = parts[0];
        var target = parts[1];

        var (path, query) = SplitTarget(target);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? line;
        while (!string.IsNullOrEmpty(line = await ReadLineAsync(stream, ct).ConfigureAwait(false)))
        {
            var idx = line!.IndexOf(':');
            if (idx <= 0) continue;
            headers[line.AsSpan(0, idx).Trim().ToString()] = line.AsSpan(idx + 1).Trim().ToString();
        }

        var body = Array.Empty<byte>();
        if (headers.TryGetValue("Content-Length", out var clRaw) &&
            int.TryParse(clRaw, out var len) && len > 0)
        {
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

    private static async Task<string?> ReadLineAsync(Stream stream, CancellationToken ct)
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
