using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Client;
using Sitas.Edge.Core.Abstractions;
using Sitas.Edge.Core.Enums;

namespace Sitas.Edge.Mqtt;

/// <summary>
/// MQTT publisher implementation.
/// </summary>
internal sealed class MqttPublisher : IMqttPublisher
{
    private readonly IMqttClient _client;
    private readonly IMessageSerializer _serializer;
    private readonly ILogger _logger;

    public MqttPublisher(
        IMqttClient client,
        IMessageSerializer serializer,
        ILogger logger)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task PublishAsync<TMessage>(
        string topic,
        TMessage message,
        QualityOfService qos = QualityOfService.AtLeastOnce,
        bool retain = false,
        CancellationToken cancellationToken = default)
        where TMessage : class
    {
        var options = new MqttPublishOptions
        {
            QualityOfService = qos,
            Retain = retain
        };

        await PublishAsync(topic, message, options, cancellationToken).ConfigureAwait(false);
    }

    public async Task PublishAsync(
        string topic,
        ReadOnlyMemory<byte> payload,
        QualityOfService qos = QualityOfService.AtLeastOnce,
        bool retain = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(topic);

        EnsureConnected();

        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload.ToArray())
            .WithQualityOfServiceLevel(MapQos(qos))
            .WithRetainFlag(retain)
            .Build();

        await _client.PublishAsync(message, cancellationToken).ConfigureAwait(false);

        _logger.LogDebug("Published message to topic {Topic}", topic);
    }

    public async Task PublishAsync<TMessage>(
        string topic,
        TMessage message,
        MqttPublishOptions options,
        CancellationToken cancellationToken = default)
        where TMessage : class
    {
        ArgumentNullException.ThrowIfNull(topic);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(options);

        EnsureConnected();

        var payload = _serializer.Serialize(message);

        var messageBuilder = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload.ToArray())
            .WithQualityOfServiceLevel(MapQos(options.QualityOfService))
            .WithRetainFlag(options.Retain);

        // Apply MQTT 5.0 specific options
        if (options.MessageExpiryIntervalSeconds.HasValue)
        {
            messageBuilder.WithMessageExpiryInterval(options.MessageExpiryIntervalSeconds.Value);
        }

        if (!string.IsNullOrEmpty(options.ContentType))
        {
            messageBuilder.WithContentType(options.ContentType);
        }

        if (options.CorrelationData is not null)
        {
            messageBuilder.WithCorrelationData(options.CorrelationData);
        }

        if (!string.IsNullOrEmpty(options.ResponseTopic))
        {
            messageBuilder.WithResponseTopic(options.ResponseTopic);
        }

        if (options.UserProperties is not null)
        {
            foreach (var (key, value) in options.UserProperties)
            {
                messageBuilder.WithUserProperty(key, value);
            }
        }

        var mqttMessage = messageBuilder.Build();

        await _client.PublishAsync(mqttMessage, cancellationToken).ConfigureAwait(false);

        _logger.LogDebug("Published message to topic {Topic}", topic);
    }

    private void EnsureConnected()
    {
        if (!_client.IsConnected)
        {
            throw new InvalidOperationException(
                "Cannot publish message: MQTT client is not connected. Call ConnectAsync() first.");
        }
    }

    private static MQTTnet.Protocol.MqttQualityOfServiceLevel MapQos(QualityOfService qos)
    {
        return qos switch
        {
            QualityOfService.AtMostOnce => MQTTnet.Protocol.MqttQualityOfServiceLevel.AtMostOnce,
            QualityOfService.AtLeastOnce => MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce,
            QualityOfService.ExactlyOnce => MQTTnet.Protocol.MqttQualityOfServiceLevel.ExactlyOnce,
            _ => MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce
        };
    }
}
