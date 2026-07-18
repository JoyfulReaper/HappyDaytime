/*
 * Happy Daytime Server
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License.
 */

using HappyDaytime.Events;
using JoyfulReaperLib.JRNet;
using JoyfulReaperLib.MissionControl;
using Microsoft.Extensions.Options;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace HappyDaytime;

public class Worker(
    ILogger<Worker> logger,
    IMissionControlClient missionControlClient,
    IOptions<HappyDaytimeOptions> options) : BackgroundService
{
    private TcpListener? _listener;
    private readonly ConcurrentDictionary<long, Task> _activeConnections = new();
    private volatile bool _stopRequested;
    private readonly SemaphoreSlim _connectionLimit = new(
        options.Value.MaxConcurrentConnections,
        options.Value.MaxConcurrentConnections
    );
    private long _nextConnectionId;
    private IPAddress? _localBoundAddress;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _localBoundAddress = IPAddressUtils.ParseListenAddress(options.Value.ListenAddress);
        _listener = new TcpListener(_localBoundAddress, options.Value.Port);
        _listener.Start();

        logger.LogInformation("HappyDaytime server started on {IPAddress}:{Port}", _localBoundAddress, options.Value.Port);

        var occurredAt = DateTimeOffset.UtcNow;

        try
        {
            await missionControlClient.TryPublishAsync(
                eventType: DaytimeServiceStartedEvent.EventName,
                payload: new DaytimeServiceStartedEvent(
                    $"{_localBoundAddress} {options.Value.Port}"),
                occurredAt: occurredAt,
                correlationId: null,
                cancellationToken: stoppingToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Failed to publish Mission Control event for Daytime Service Started");
        }

        try
        {
            TcpClient client;
            while (!_stopRequested && !stoppingToken.IsCancellationRequested)
            {
                try
                {
                    client = await _listener.AcceptTcpClientAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (SocketException) when (stoppingToken.IsCancellationRequested || _stopRequested)
                {
                    break;
                }
                try
                {
                    await _connectionLimit.WaitAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    client.Dispose();
                    break;
                }

                long connectionId = Interlocked.Increment(ref _nextConnectionId);
                Task task = HandleClientAsync(connectionId, client, stoppingToken);
                _activeConnections[connectionId] = task;

                _ = task.ContinueWith(ct =>
                {
                    _activeConnections.TryRemove(connectionId, out _);
                    _connectionLimit.Release();
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            }
        }
        finally
        {
            _listener.Stop();
            Task[] remaining = _activeConnections.Values.ToArray();
            if (remaining.Length > 0)
            {
                try
                {
                    await Task.WhenAll(remaining);
                }
                catch
                {
                    // Normal Shutdown
                }
            }
        }
    }

    private async Task HandleClientAsync(long connectionId, TcpClient client, CancellationToken stoppingToken)
    {
        var occurredAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var correlationId = Guid.NewGuid().ToString("N");

        bool succeeded = false;
        bool shouldPublish = true;
        string outcome = "failed";
        string remoteString = "unknown";
        string response = string.Empty;

        bool isIgnoredTelemetrySource = false;

        using (client)
        {
            EndPoint? remote = null;

            try
            {
                client.NoDelay = true;
                remote = client.Client.RemoteEndPoint;

                remoteString = remote?.ToString() ?? "unknown";

                string? remoteAddress = (remote as IPEndPoint)?
                    .Address
                    .MapToIPv4()
                    .ToString();

                isIgnoredTelemetrySource =
                    !string.IsNullOrWhiteSpace(
                        options.Value.TelemetryIgnoredRemoteAddress) &&
                    string.Equals(
                        remoteAddress,
                        options.Value.TelemetryIgnoredRemoteAddress,
                        StringComparison.OrdinalIgnoreCase);

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                timeoutCts.CancelAfter(options.Value.RequestTimeoutSeconds);

                CancellationToken connectionToken = timeoutCts.Token;

                response = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                byte[] responseBytes = Encoding.ASCII.GetBytes(response + "\r\n");

                await using NetworkStream stream = client.GetStream();
                await stream.WriteAsync(responseBytes, connectionToken);
                await stream.FlushAsync(connectionToken);

                succeeded = true;
                outcome = "success";
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                // The application is shutting down. This is not a request
                // timeout and does not need to produce a telemetry event.
                shouldPublish = false;

                logger.LogDebug(
                    "Connection {ConnectionId} from {Remote} was cancelled during shutdown.",
                    connectionId,
                    remote);
            }
            catch (OperationCanceledException)
            {
                outcome = "timeout";

                logger.LogWarning(
                    "Connection {ConnectionId} from {Remote} timed out.",
                    connectionId,
                    remote);
            }
            catch (IOException exception)
            {
                outcome = "io-error";

                logger.LogDebug(
                    exception,
                    "Connection {ConnectionId} from {Remote} ended early.",
                    connectionId,
                    remote);
            }
            catch (SocketException exception)
            {
                outcome = "socket-error";

                logger.LogDebug(
                    exception,
                    "Socket error on connection {ConnectionId} from {Remote}.",
                    connectionId,
                    remote);
            }
            catch (Exception exception)
            {
                outcome = "failed";

                logger.LogError(
                    exception,
                    "Unhandled error on connection {ConnectionId} from {Remote}.",
                    connectionId,
                    remote);
            }

            stopwatch.Stop();

            if (!shouldPublish || isIgnoredTelemetrySource)
            {
                logger.LogDebug(
                    "Skipping telemetry for health-check connection {ConnectionId} from {Remote}.",
                    connectionId,
                    remoteString);

                return;
            }

            try
            {
                await missionControlClient.TryPublishAsync(
                    eventType: DaytimeRequestCompletedEvent.EventName,
                    payload: new DaytimeRequestCompletedEvent(
                        Remote: remoteString,
                        Response: response,
                        DurationMilliseconds: stopwatch.ElapsedMilliseconds,
                        Outcome: outcome,
                        Succeeded: succeeded),
                    occurredAt: occurredAt,
                    correlationId: correlationId,
                    cancellationToken: stoppingToken);
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Failed to publish Mission Control event for connection {ConnectionId}.",
                    connectionId);
            }
        }
    }
}
