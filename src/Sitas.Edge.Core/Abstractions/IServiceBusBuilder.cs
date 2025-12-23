namespace Sitas.Edge.Core.Abstractions;

/// <summary>
/// Base interface for all service bus builders.
/// Follows the Builder pattern for fluent configuration of service bus connections.
/// </summary>
/// <typeparam name="TBuilder">The concrete builder type for fluent chaining.</typeparam>
/// <typeparam name="TConnection">The connection type that will be built.</typeparam>
public interface IServiceBusBuilder<out TBuilder, out TConnection>
    where TBuilder : IServiceBusBuilder<TBuilder, TConnection>
    where TConnection : IServiceBusConnection
{
    /// <summary>
    /// Builds the configured connection instance.
    /// </summary>
    /// <returns>A configured but not yet connected service bus connection.</returns>
    TConnection Build();
}

