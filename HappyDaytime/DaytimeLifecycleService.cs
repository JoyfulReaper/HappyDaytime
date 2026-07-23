/*
 * Happy Daytime Service
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License.
 */

using HappyDaytime.Events;
using JoyfulReaperLib.JRNet;
using JoyfulReaperLib.MissionControl;
using Microsoft.Extensions.Options;

namespace HappyDaytime;

/// <summary>
/// Handles application-level lifecycle telemetry for HappyDaytime.
/// </summary>
public sealed class DaytimeLifecycleService(
    ILogger<DaytimeLifecycleService> logger,
    IMissionControlClient missionControlClient,
    IOptions<HappyDaytimeOptions> options) : IHostedLifecycleService
{
    /// <inheritdoc />
    public Task StartingAsync(CancellationToken cancellationToken) =>
        Task.CompletedTask;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) =>
        Task.CompletedTask;

    /// <inheritdoc />
    public async Task StartedAsync(CancellationToken cancellationToken)
    {
        var listenAddress = IPAddressUtils.ParseListenAddress(
            options.Value.ListenAddress);

        DateTimeOffset occurredAt = DateTimeOffset.UtcNow;

        using var timeout =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        timeout.CancelAfter(TimeSpan.FromSeconds(2)); // TODO: Make configurable
        logger.LogInformation("HappyDaytime Service Listening on {IPAddress}:{Port}", listenAddress, options.Value.Port);

        try
        {
            bool published = await missionControlClient
                .TryPublishAsync(
                    eventType: DaytimeServiceStartedEvent.EventName,
                    payload: new DaytimeServiceStartedEvent($"{listenAddress}:{options.Value.Port}"),
                    payloadTypeInfo: HappyDaytimeJsonContext
                        .Default
                        .DaytimeServiceStartedEvent,
                    occurredAt:
                        occurredAt,
                    correlationId: null,
                    cancellationToken: timeout.Token);

            if (!published)
            {
                logger.LogWarning(
                    "Mission Control did not accept {EventType}.",
                    DaytimeServiceStartedEvent.EventName);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken
                .IsCancellationRequested)
        {
            logger.LogDebug(
                "Mission Control event publication for Daytime Service Started was cancelled.");
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning(
                "Mission Control event publication for Daytime Service Started timed out.");
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Failed to publish Mission Control event for Daytime Service Started.");
        }
    }

    /// <inheritdoc />
    public Task StoppingAsync(
        CancellationToken cancellationToken)
    {
        logger.LogInformation("HappyDaytime Service Stopping...");

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(
        CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StoppedAsync(
        CancellationToken cancellationToken) => Task.CompletedTask;
}