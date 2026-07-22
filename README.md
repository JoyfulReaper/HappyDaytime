# Happy Daytime

Happy Daytime is a lightweight TCP Daytime server built with .NET 10 and
published as a Native AOT executable.

When a client connects, the server writes exactly one UTC timestamp line and
closes the TCP connection:

```text
2026-07-17T21:30:45.1234567+00:00
```

Happy Daytime supports the TCP side of
[RFC 867](https://www.rfc-editor.org/rfc/rfc867). RFC 867 also defines UDP, but
Happy Daytime intentionally does not implement UDP.

## Features

- TCP-only RFC 867 daytime responses
- Configurable listen address, port, timeout, and connection limit
- One ISO 8601 / round-trip UTC timestamp line per connection
- Graceful shutdown while active connections finish
- Best-effort Mission Control startup and request telemetry
- Native AOT publishing with full trimming and size optimization
- Self-contained Alpine native executable in a small `runtime-deps` final image
- Non-root container process

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), or
- [Docker](https://docs.docker.com/get-docker/)

## Run Locally

```powershell
git clone https://github.com/JoyfulReaper/HappyDaytime.git
cd HappyDaytime
$env:Daytime__Port = "1313"
dotnet run --project .\HappyDaytime\HappyDaytime.csproj
```

Connect with a TCP client:

```bash
nc 127.0.0.1 1313
```

The client does not need to send data. The server writes one timestamp line,
flushes it, and closes the connection.

## Docker

Build and run on the unprivileged internal container port:

```bash
docker build -t happy-daytime .
docker run --rm -p 1313:1313 happy-daytime
```

Publish the canonical public Daytime port while keeping the container process
non-root:

```bash
docker run --rm -p 13:1313 happy-daytime
```

Binding host port 13 may require root or appropriate host-level privileges on
Linux and macOS. The container process itself still runs as a non-root user and
does not need added Linux capabilities.

The Docker image defaults to:

```dockerfile
ENV Daytime__ListenAddress=0.0.0.0
ENV Daytime__Port=1313
ENV Daytime__MaxConcurrentConnections=100
```

The final image uses `mcr.microsoft.com/dotnet/runtime-deps:10.0-alpine`. It
contains the Native AOT executable and native runtime dependencies only; it does
not contain the full managed .NET runtime.

## Configuration

Settings live in the `Daytime` section of
[`appsettings.json`](HappyDaytime/appsettings.json). Environment variables use
two underscores (`__`) as section separators.

| Setting | Environment variable | Default | Description |
| --- | --- | ---: | --- |
| `ListenAddress` | `Daytime__ListenAddress` | `127.0.0.1` | IP address on which the TCP listener binds |
| `Port` | `Daytime__Port` | `13` | TCP listening port |
| `MaxConcurrentConnections` | `Daytime__MaxConcurrentConnections` | `64` | Maximum concurrent accepted connections |
| `RequestTimeoutSeconds` | `Daytime__RequestTimeoutSeconds` | `15` | Timeout for writing and flushing one response |
| `TelemetryIgnoredRemoteAddress` | `Daytime__TelemetryIgnoredRemoteAddress` | `null` | Client IP excluded from request telemetry |

Example:

```bash
Daytime__ListenAddress=127.0.0.1 \
Daytime__Port=1313 \
Daytime__MaxConcurrentConnections=100 \
dotnet run --project HappyDaytime/HappyDaytime.csproj
```

Invalid port, connection-limit, or timeout values are rejected when the
application starts.

## Build And Publish

```bash
dotnet restore HappyDaytime.slnx
dotnet build HappyDaytime.slnx --configuration Release --no-restore
dotnet test HappyDaytime.slnx --configuration Release --no-build
dotnet publish HappyDaytime/HappyDaytime.csproj \
  --configuration Release \
  --runtime linux-musl-x64 \
  --self-contained true \
  /p:PublishAot=true \
  --no-restore
```

Native AOT, full trimming, and size optimization are enabled in the project.

## Telemetry

Happy Daytime publishes best-effort Mission Control telemetry:

- `happydaytime.service.started`
- `happydaytime.request.completed`

Startup telemetry and request telemetry each use an independent two-second
bounded timeout. If Mission Control is unavailable, returns `false`, times out,
or throws, the server logs the condition and keeps serving.

For request telemetry, the response is written and flushed first. Then the
stream and `TcpClient` are disposed, the connection semaphore slot is released,
and only afterward is request telemetry awaited with its bounded timeout.

## License

Happy Daytime is available under the [MIT License](LICENSE).
