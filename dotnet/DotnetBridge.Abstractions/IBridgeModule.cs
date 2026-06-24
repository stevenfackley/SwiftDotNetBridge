using System.Threading;
using System.Threading.Tasks;

namespace DotnetBridge.Abstractions;

/// <summary>A handler for one matched route. Return bytes; the bridge does no JSON itself.</summary>
public delegate Task<BridgeResponse> RouteHandler(BridgeRequest request, CancellationToken ct);

/// <summary>Implement this in YOUR library and register it in the publish head's Bootstrap.</summary>
public interface IBridgeModule
{
    /// <summary>Register the module's routes on the shared <see cref="RouteTable"/>.</summary>
    /// <param name="routes">The route table to add handlers to.</param>
    void Configure(RouteTable routes);
}
