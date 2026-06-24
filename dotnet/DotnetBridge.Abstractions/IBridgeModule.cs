using System.Threading;
using System.Threading.Tasks;

namespace DotnetBridge.Abstractions;

/// <summary>A handler for one matched route. Return bytes; the bridge does no JSON itself.</summary>
public delegate Task<BridgeResponse> RouteHandler(BridgeRequest request, CancellationToken ct);

/// <summary>Implement this in YOUR library and register it in the publish head's Bootstrap.</summary>
public interface IBridgeModule
{
    void Configure(RouteTable routes);
}
