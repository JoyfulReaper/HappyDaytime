# Happy Daytime

Happy Daytime is a lightweight TCP daytime server built with .NET 10. When a
client connects, the server returns the current UTC time as an ISO 8601 value
and closes the connection.

```text
2026-07-17T21:30:45.1234567+00:00
```

The service follows the simple request-free model of the
[Daytime Protocol (RFC 867)](https://www.rfc-editor.org/rfc/rfc867), while using
a machine-friendly timestamp format.

## Features

- Listens on a configurable address and TCP port
- Limits the number of concurrent connections
- Applies a timeout to each request
- Shuts down gracefully while active connections finish
- Runs as a console application, Windows Service, or Docker container
- Publishes best-effort request telemetry through JoyfulReaper Mission Control
- Can exclude a health-check address from telemetry

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), or
- [Docker](https://docs.docker.com/get-docker/)

## Run locally

Clone the repository and start the worker:

```powershell
git clone https://github.com/JoyfulReaper/HappyDaytime.git
cd HappyDaytime
$env:Daytime__Port = "1313"
dotnet run --project .\HappyDaytime\HappyDaytime.csproj
```

Port 13 is the protocol's assigned port, but ports below 1024 commonly require
elevated privileges on Linux and macOS. The example uses port 1313 to avoid
that requirement.

Connect with a TCP client such as Netcat:

```bash
nc 127.0.0.1 1313
```

The server immediately writes the current UTC timestamp and closes the
connection; the client does not need to send a request.

## Run with Docker

Build and run the image from the repository root:

```bash
docker build -t happy-daytime .
docker run --rm -p 1313:1313 -e Daytime__Port=1313 happy-daytime
```

Then connect to `localhost:1313` with any TCP client.

## Configuration

Settings live in the `Daytime` section of
[`appsettings.json`](HappyDaytime/appsettings.json). They can also be supplied
through standard .NET configuration providers. In environment variables, use
two underscores (`__`) to represent a section separator.

| Setting | Environment variable | Default | Description |
| --- | --- | ---: | --- |
| `ListenAddress` | `Daytime__ListenAddress` | `0.0.0.0` | IP address on which the server listens |
| `Port` | `Daytime__Port` | `13` | TCP listening port (1-65535) |
| `MaxConcurrentConnections` | `Daytime__MaxConcurrentConnections` | `64` | Maximum number of connections handled at once |
| `RequestTimeoutSeconds` | `Daytime__RequestTimeoutSeconds` | `15` | Timeout for writing a response |
| `TelemetryIgnoredRemoteAddress` | `Daytime__TelemetryIgnoredRemoteAddress` | `null` | Client IP address excluded from telemetry, useful for health checks |

For example:

```bash
Daytime__ListenAddress=127.0.0.1 \
Daytime__Port=1313 \
Daytime__MaxConcurrentConnections=100 \
dotnet run --project HappyDaytime/HappyDaytime.csproj
```

Invalid port, connection-limit, or timeout values are rejected when the
application starts.

## Build and publish

```bash
dotnet restore HappyDaytime.slnx
dotnet build HappyDaytime.slnx --configuration Release --no-restore
dotnet publish HappyDaytime/HappyDaytime.csproj --configuration Release
```

The application includes Windows Service integration. A published executable
can be registered with the Windows Service Control Manager and runs under the
service name **Happy Daytime Server**.

## Telemetry

After a connection completes, Happy Daytime attempts to publish a
`happydaytime.request.completed` event. The event includes the remote endpoint,
response, duration, outcome, and success state. Telemetry is best-effort: a
publishing failure is logged but does not interrupt the daytime service.

## License

Happy Daytime is available under the [MIT License](LICENSE).
