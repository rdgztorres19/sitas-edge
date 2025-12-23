using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sitas.Edge.Core.Abstractions;

namespace Sitas.Edge.DependencyInjection;

/// <summary>
/// Hosted service that manages the lifecycle of all Nexus connections.
/// Automatically connects on startup and disconnects on shutdown.
/// </summary>
internal sealed class SitasEdgeHostedService : IHostedService
{
    private readonly ISitasEdge _nexus;
    private readonly ILogger<SitasEdgeHostedService> _logger;

    public SitasEdgeHostedService(ISitasEdge nexus, ILogger<SitasEdgeHostedService> logger)
    {
        _nexus = nexus;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting Nexus Service Bus connections...");
        
        try
        {
            await _nexus.ConnectAllAsync(cancellationToken);
            _logger.LogInformation(
                "Nexus Service Bus started with {Count} connection(s)",
                _nexus.Connections.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start Nexus Service Bus connections");
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Nexus Service Bus connections...");
        
        try
        {
            await _nexus.DisconnectAllAsync(cancellationToken);
            await _nexus.DisposeAsync();
            _logger.LogInformation("Nexus Service Bus stopped");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error while stopping Nexus Service Bus");
        }
    }
}
