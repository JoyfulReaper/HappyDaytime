/*
 * Happy Daytime Service
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License.
 */


using HappyDaytime.Events;
using JoyfulReaperLib.MissionControl;
using JoyfulReaperLib.TcpServer;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace HappyDaytime;

public sealed class DaytimeConnectionHandler(
    ILogger<DaytimeConnectionHandler> logger,
    IMissionControlClient missionControlClient,
    IOptions<HappyDaytimeOptions> options) : ITcpConnectionHandler

{
    public async ValueTask HandleAsync(TcpConnectionContext context, CancellationToken cancellationToken)
    {
        DaytimeConnectionResult result = await HandleConnectionAsync(
            context, cancellationToken);

        if (result.ShouldPublish)
        {
            context.RegisterAfterClose(afterCloseToken =>
                PublishTelemetryAsync(context.ConnectionId, result, afterCloseToken));
        }
    }

    private async ValueTask<DaytimeConnectionResult> HandleConnectionAsync(
        TcpConnectionContext context,
        CancellationToken cancellationToken)
    {
        DateTimeOffset occurredAt = DateTimeOffset.UtcNow;
        string correlationId = Guid.NewGuid().ToString("N");

        bool succeeded = false;
        bool shouldPublish = true;
        string outcome = "failed";
        string response = string.Empty;
        long durationMilliseconds = 0;

        EndPoint? remote = context.RemoteEndPoint;
        string remoteString = remote?.ToString() ?? "unknown";

        bool isIgnoredTelemetrySource = IsIgnoredTelemetrySource(remote);
        Stopwatch? stopwatch = null;

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(GetRequestTimeout(options.Value));

            stopwatch = Stopwatch.StartNew();
            response = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            byte[] responseBytes = Encoding.ASCII.GetBytes(response + "\r\n");

            await context.Stream.WriteAsync(responseBytes, timeout.Token);
            await context.Stream.FlushAsync(timeout.Token);

            stopwatch?.Stop();
            durationMilliseconds = stopwatch?.ElapsedMilliseconds ?? 0;

            succeeded = true;
            outcome = "success";
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            shouldPublish = false;

            logger.LogDebug(
                "Connection {ConnectionId} from {Remote} was cancelled during shutdown.",
                context.ConnectionId,
                remote);
        }
        catch (OperationCanceledException)
        {
            outcome = "timeout";

            logger.LogWarning(
                "Connection {ConnectionId} from {Remote} timed out.",
                context.ConnectionId,
                remote);
        }
        catch (IOException exception)
        {
            outcome = "io-error";

            logger.LogDebug(
                exception,
                "Connection {ConnectionId} from {Remote} ended early.",
                context.ConnectionId,
                remote);
        }
        catch (SocketException exception)
        {
            outcome = "socket-error";

            logger.LogDebug(
                exception,
                "Socket error on connection {ConnectionId} from {Remote}.",
                context.ConnectionId,
                remote);
        }
        catch (Exception exception)
        {
            outcome = "failed";

            logger.LogError(
                exception,
                "Unhandled error on connection {ConnectionId} from {Remote}.",
                context.ConnectionId,
                remote);
        }
        finally
        {
            if (stopwatch is { IsRunning: true })
            {
                stopwatch.Stop();

                durationMilliseconds =
                    stopwatch.ElapsedMilliseconds;
            }
        }

        return new DaytimeConnectionResult(
            ShouldPublish: shouldPublish,
            IsIgnoredTelemetrySource: isIgnoredTelemetrySource,
            Remote: remoteString,
            Response: response,
            DurationMilliseconds: durationMilliseconds,
            Outcome: outcome,
            Succeeded: succeeded,
            OccurredAt: occurredAt,
            CorrelationId: correlationId
        );
    }

    private bool IsIgnoredTelemetrySource(EndPoint? remoteEndPoint)
    {
        string? remoteAddress =
            (remoteEndPoint as IPEndPoint)?
                .Address
                .MapToIPv4()
                .ToString();

        return !string.IsNullOrWhiteSpace(options.Value.TelemetryIgnoredRemoteAddress) &&
                string.Equals(remoteAddress, options.Value.TelemetryIgnoredRemoteAddress, StringComparison.OrdinalIgnoreCase);
    }

    private async ValueTask PublishTelemetryAsync(
        long connectionId, DaytimeConnectionResult result, CancellationToken cancellationToken)
    {
        if (result.IsIgnoredTelemetrySource)
        {
            logger.LogDebug(
                "Skipping telemetry for health-check connection {ConnectionId} from {Remote}.",
                connectionId,
                result.Remote);

            return;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2)); // TODO Make configurable

        try
        {
            bool published = await missionControlClient.TryPublishAsync(
                eventType: DaytimeRequestCompletedEvent.EventName,
                payload: new DaytimeRequestCompletedEvent(
                    Remote: result.Remote,
                    Response: result.Response,
                    DurationMilliseconds: result.DurationMilliseconds,
                    Outcome: result.Outcome,
                    Succeeded: result.Succeeded),
                    payloadTypeInfo: HappyDaytimeJsonContext.Default.DaytimeRequestCompletedEvent,
                    occurredAt: result.OccurredAt,
                    correlationId: result.CorrelationId,
                    cancellationToken: timeout.Token
                );

            if (!published)
            {
                logger.LogWarning(
                    "Mission Control did not accept {EventType} for connection {ConnectionId}.",
                    DaytimeRequestCompletedEvent.EventName,
                    connectionId);
            }

        }
        catch (OperationCanceledException)
           when (cancellationToken
               .IsCancellationRequested)
        {
            logger.LogDebug(
                "Mission Control event publication for connection {ConnectionId} was cancelled during shutdown.",
                connectionId);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning(
                "Mission Control event publication for connection {ConnectionId} timed out.",
                connectionId);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Failed to publish Mission Control event for connection {ConnectionId}.",
                connectionId);
        }
    }

    internal static TimeSpan GetRequestTimeout(
        HappyDaytimeOptions options) =>
        TimeSpan.FromSeconds(
            options.RequestTimeoutSeconds);

    internal sealed record DaytimeConnectionResult(
        bool ShouldPublish,
        bool IsIgnoredTelemetrySource,
        string Remote,
        string Response,
        long DurationMilliseconds,
        string Outcome,
        bool Succeeded,
        DateTimeOffset OccurredAt,
        string CorrelationId);
}
