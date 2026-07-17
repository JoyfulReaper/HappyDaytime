/*
 * Happy Daytime Server
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License.
 */

using JoyfulReaperLib.MissionControl;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Sockets;
using System.Collections.Concurrent;
using System.Buffers;
using JoyfulReaperLib.JRNet;

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
    }
}
