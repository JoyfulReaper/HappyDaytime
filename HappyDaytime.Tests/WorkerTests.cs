using HappyDaytime.Events;
using JoyfulReaperLib.MissionControl;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Serialization.Metadata;
using Xunit;

namespace HappyDaytime.Tests;

public sealed class WorkerTests
{
    [Fact]
    public async Task Start_Publishes_Service_Started_Telemetry()
    {
        var client = new RecordingMissionControlClient();
        int port = GetFreeTcpPort();
        using var worker = CreateWorker(port, client);

        try
        {
            await worker.StartAsync(CancellationToken.None);

            await WaitForAsync(() => client.SuccessfulCalls.Count == 1);

            var publish = Assert.Single(client.SuccessfulCalls);
            Assert.Equal(DaytimeServiceStartedEvent.EventName, publish.EventType);
            Assert.Equal(typeof(DaytimeServiceStartedEvent), publish.PayloadDeclaredType);

            var payload = Assert.IsType<DaytimeServiceStartedEvent>(publish.Payload);
            Assert.Equal($"127.0.0.1:{port}", payload.ListenAddress);
            Assert.Null(publish.CorrelationId);
            Assert.Equal(DateTimeOffset.UtcNow.Date, publish.OccurredAt.Date);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Responds_With_Rfc3339_Timestamp_And_Publishes_Request_Telemetry()
    {
        var client = new RecordingMissionControlClient();
        int port = GetFreeTcpPort();
        using var worker = CreateWorker(port, client);

        try
        {
            await worker.StartAsync(CancellationToken.None);
            await WaitForAsync(() => client.SuccessfulCalls.Count == 1);

            string response = await SendRequestAsync(port);

            await WaitForAsync(() => client.SuccessfulCalls.Count == 2);

            string[] lines = response.Split(new[] { "\r\n" }, StringSplitOptions.None);
            Assert.Equal(2, lines.Length);
            Assert.False(string.IsNullOrWhiteSpace(lines[0]));
            Assert.Empty(lines[1]);

            Assert.True(
                DateTimeOffset.TryParseExact(
                    lines[0],
                    "O",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var parsedResponse));

            var request = Assert.Single(
                client.SuccessfulCalls,
                call => call.EventType == DaytimeRequestCompletedEvent.EventName);
            var payload = Assert.IsType<DaytimeRequestCompletedEvent>(request.Payload);

            Assert.True(payload.Succeeded);
            Assert.Equal("success", payload.Outcome);
            Assert.Equal(lines[0], payload.Response);
            Assert.StartsWith("127.0.0.1:", payload.Remote, StringComparison.Ordinal);
            Assert.Equal(typeof(DaytimeRequestCompletedEvent), request.PayloadDeclaredType);
            Assert.NotEqual(Guid.Empty.ToString("N"), request.CorrelationId);
            Assert.True(parsedResponse >= request.OccurredAt.AddSeconds(-1));
            Assert.True(parsedResponse <= DateTimeOffset.UtcNow.AddSeconds(1));
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Skips_Request_Telemetry_For_ignored_Remote_Address()
    {
        var client = new RecordingMissionControlClient();
        int port = GetFreeTcpPort();
        using var worker = CreateWorker(port, client, options =>
        {
            options.TelemetryIgnoredRemoteAddress = "127.0.0.1";
        });

        try
        {
            await worker.StartAsync(CancellationToken.None);
            await WaitForAsync(() => client.SuccessfulCalls.Count == 1);

            string response = await SendRequestAsync(port);

            Assert.NotEmpty(response);
            await WaitForAsync(() => client.Attempts.Count == 1);
            Assert.Single(client.SuccessfulCalls);
            Assert.DoesNotContain(client.SuccessfulCalls, call => call.EventType == DaytimeRequestCompletedEvent.EventName);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Closes_Client_Before_Request_Telemetry_Completes()
    {
        var client = new RecordingMissionControlClient
        {
            BlockRequestTelemetry = true
        };
        int port = GetFreeTcpPort();
        using var worker = CreateWorker(port, client);

        try
        {
            await worker.StartAsync(CancellationToken.None);
            await WaitForAsync(() => client.SuccessfulCalls.Count == 1);

            string response = await SendRequestAsync(port).WaitAsync(TimeSpan.FromSeconds(5));

            await client.RequestTelemetryStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.NotEmpty(response);
            Assert.Single(client.BlockedRequestTelemetry);
            Assert.DoesNotContain(
                client.SuccessfulCalls,
                call => call.EventType == DaytimeRequestCompletedEvent.EventName);
        }
        finally
        {
            client.ReleaseAllBlockedRequestTelemetry();
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Releases_Connection_Slot_Before_Request_Telemetry_Completes()
    {
        var client = new RecordingMissionControlClient
        {
            BlockRequestTelemetry = true
        };
        int port = GetFreeTcpPort();
        using var worker = CreateWorker(port, client, options =>
        {
            options.MaxConcurrentConnections = 1;
        });

        try
        {
            await worker.StartAsync(CancellationToken.None);
            await WaitForAsync(() => client.SuccessfulCalls.Count == 1);

            string firstResponse = await SendRequestAsync(port).WaitAsync(TimeSpan.FromSeconds(5));
            await WaitForAsync(() => client.BlockedRequestTelemetry.Count == 1);

            string secondResponse = await SendRequestAsync(port).WaitAsync(TimeSpan.FromSeconds(5));
            await WaitForAsync(() => client.BlockedRequestTelemetry.Count == 2);

            Assert.NotEmpty(firstResponse);
            Assert.NotEmpty(secondResponse);
        }
        finally
        {
            client.ReleaseAllBlockedRequestTelemetry();
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Cancels_Request_Telemetry_With_Bounded_Timeout()
    {
        var client = new RecordingMissionControlClient
        {
            CancelRequestTelemetry = true
        };
        int port = GetFreeTcpPort();
        using var worker = CreateWorker(port, client);

        try
        {
            await worker.StartAsync(CancellationToken.None);
            await WaitForAsync(() => client.SuccessfulCalls.Count == 1);

            string response = await SendRequestAsync(port).WaitAsync(TimeSpan.FromSeconds(5));

            await client.RequestTelemetryCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.NotEmpty(response);
            Assert.Single(
                client.Attempts,
                call => call.EventType == DaytimeRequestCompletedEvent.EventName);
            Assert.DoesNotContain(
                client.SuccessfulCalls,
                call => call.EventType == DaytimeRequestCompletedEvent.EventName);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Keeps_Serving_When_Request_Telemetry_Throws()
    {
        var client = new RecordingMissionControlClient
        {
            ThrowOnRequestTelemetry = true
        };
        int port = GetFreeTcpPort();
        using var worker = CreateWorker(port, client, options =>
        {
            options.MaxConcurrentConnections = 1;
        });

        try
        {
            await worker.StartAsync(CancellationToken.None);
            await WaitForAsync(() => client.SuccessfulCalls.Count == 1);

            string firstResponse = await SendRequestAsync(port).WaitAsync(TimeSpan.FromSeconds(5));
            string secondResponse = await SendRequestAsync(port).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.NotEmpty(firstResponse);
            Assert.NotEmpty(secondResponse);
            Assert.Equal(3, client.Attempts.Count);
            Assert.Single(client.SuccessfulCalls);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(15)]
    [InlineData(60)]
    public void Request_Timeout_Uses_Configured_Seconds(
        int requestTimeoutSeconds)
    {
        var options = new HappyDaytimeOptions
        {
            RequestTimeoutSeconds = requestTimeoutSeconds
        };

        TimeSpan timeout = DaytimeWorker.GetRequestTimeout(options);

        Assert.Equal(requestTimeoutSeconds, timeout.TotalSeconds);
    }

    [Fact]
    public async Task Keeps_Serving_When_Startup_Telemetry_Fails()
    {
        var client = new RecordingMissionControlClient
        {
            ThrowOnAttemptNumber = 1
        };
        int port = GetFreeTcpPort();
        using var worker = CreateWorker(port, client);

        try
        {
            await worker.StartAsync(CancellationToken.None);

            string response = await SendRequestAsync(port);

            await WaitForAsync(() => client.SuccessfulCalls.Count == 1);

            Assert.NotEmpty(response);
            Assert.Equal(2, client.Attempts.Count);
            var request = Assert.Single(client.SuccessfulCalls);
            Assert.Equal(DaytimeRequestCompletedEvent.EventName, request.EventType);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Stop_Releases_The_Port()
    {
        int port = GetFreeTcpPort();
        var client = new RecordingMissionControlClient();
        using var worker = CreateWorker(port, client);

        await worker.StartAsync(CancellationToken.None);
        await WaitForAsync(() => client.SuccessfulCalls.Count == 1);

        await worker.StopAsync(CancellationToken.None);

        using var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        listener.Stop();
    }

    private static DaytimeWorker CreateWorker(
        int port,
        RecordingMissionControlClient client,
        Action<HappyDaytimeOptions>? configure = null)
    {
        var options = new HappyDaytimeOptions
        {
            ListenAddress = "127.0.0.1",
            Port = port,
            MaxConcurrentConnections = 2,
            RequestTimeoutSeconds = 2
        };

        configure?.Invoke(options);

        return new DaytimeWorker(
            LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.None)).CreateLogger<DaytimeWorker>(),
            client,
            Options.Create(options));
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task<string> SendRequestAsync(int port)
    {
        using var client = new TcpClient();
        await ConnectAsync(client, port);

        await using NetworkStream stream = client.GetStream();
        using var memory = new MemoryStream();
        var buffer = new byte[256];

        while (true)
        {
            int read = await stream.ReadAsync(buffer);
            if (read == 0)
            {
                break;
            }

            memory.Write(buffer, 0, read);
        }

        return Encoding.ASCII.GetString(memory.ToArray());
    }

    private static async Task ConnectAsync(
        TcpClient client,
        int port)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (true)
        {
            try
            {
                await client.ConnectAsync(IPAddress.Loopback, port);
                return;
            }
            catch (SocketException) when (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(25);
            }
        }
    }

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMilliseconds = 5000)
    {
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(timeoutMilliseconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException("Timed out while waiting for the worker to reach the expected state.");
    }

    private sealed class RecordingMissionControlClient : IMissionControlClient
    {
        private int _attemptCounter;

        public ConcurrentQueue<PublishCall> Attempts { get; } = new();

        public ConcurrentQueue<PublishCall> SuccessfulCalls { get; } = new();

        public ConcurrentQueue<BlockedRequestTelemetry> BlockedRequestTelemetry { get; } = new();

        public TaskCompletionSource RequestTelemetryStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource RequestTelemetryCancelled { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public bool BlockRequestTelemetry { get; init; }

        public bool CancelRequestTelemetry { get; init; }

        public bool ThrowOnRequestTelemetry { get; init; }

        public int ThrowOnAttemptNumber { get; init; }

        public Task<bool> TryPublishAsync<TPayload>(
            string eventType,
            TPayload payload,
            JsonTypeInfo<TPayload> payloadTypeInfo,
            DateTimeOffset occurredAt,
            string? correlationId,
            CancellationToken cancellationToken)
        {
            int attemptNumber = Interlocked.Increment(ref _attemptCounter);
            var call = new PublishCall(
                attemptNumber,
                eventType,
                payload,
                payload?.GetType() ?? typeof(TPayload),
                payloadTypeInfo.Type,
                occurredAt,
                correlationId);

            Attempts.Enqueue(call);

            if (eventType == DaytimeRequestCompletedEvent.EventName)
            {
                RequestTelemetryStarted.TrySetResult();

                if (ThrowOnRequestTelemetry)
                {
                    throw new InvalidOperationException("Planned request telemetry failure.");
                }

                if (CancelRequestTelemetry)
                {
                    return WaitForCancellationAsync(cancellationToken);
                }

                if (BlockRequestTelemetry)
                {
                    return BlockRequestTelemetryAsync(call);
                }
            }

            if (attemptNumber == ThrowOnAttemptNumber)
            {
                throw new InvalidOperationException($"Planned publish failure for attempt {attemptNumber}.");
            }

            SuccessfulCalls.Enqueue(call);
            return Task.FromResult(true);
        }

        public void ReleaseAllBlockedRequestTelemetry()
        {
            foreach (var blocked in BlockedRequestTelemetry)
            {
                blocked.Release.TrySetResult();
            }
        }

        private async Task<bool> WaitForCancellationAsync(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return true;
            }
            catch (OperationCanceledException)
            {
                RequestTelemetryCancelled.TrySetResult();
                throw;
            }
        }

        private async Task<bool> BlockRequestTelemetryAsync(PublishCall call)
        {
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var blocked = new BlockedRequestTelemetry(call, release);

            BlockedRequestTelemetry.Enqueue(blocked);

            await release.Task;

            SuccessfulCalls.Enqueue(call);
            return true;
        }
    }

    private sealed record PublishCall(
        int AttemptNumber,
        string EventType,
        object? Payload,
        Type PayloadRuntimeType,
        Type PayloadDeclaredType,
        DateTimeOffset OccurredAt,
        string? CorrelationId);

    private sealed record BlockedRequestTelemetry(
        PublishCall Call,
        TaskCompletionSource Release);
}
