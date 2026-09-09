# Happy Daytime

Happy Daytime is a lightweight RFC 867 Daytime server built with .NET 10 and
published as a Native AOT executable.

It supports both TCP and UDP over IPv4 and IPv6.

A response contains exactly one UTC timestamp line:

```text
2026-07-17T21:30:45.1234567+00:00
```

Happy Daytime implements the TCP and UDP services described by
[RFC 867](https://www.rfc-editor.org/rfc/rfc867).

## Features

- RFC 867 Daytime over TCP and UDP
- IPv4 and IPv6 support
- IPv4/IPv6 dual-stack operation
- Configurable TCP and UDP listen addresses and ports
- One ISO 8601 / round-trip UTC timestamp per request
- TCP request contents are ignored
- UDP datagram contents are ignored
- Graceful shutdown
- Best-effort Mission Control telemetry
- Native AOT publishing with full trimming and size optimization
- Self-contained Alpine native executable
- Non-root container process

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), or
- [Docker](https://docs.docker.com/get-docker/)

## Run Locally

```powershell
git clone https://github.com/JoyfulReaper/HappyDaytime.git
cd HappyDaytime

$env:Daytime__Port = "1313"
$env:Daytime__UdpPort = "1313"

dotnet run --project .\HappyDaytime\HappyDaytime.csproj
```

### TCP

Connect with a TCP client:

```bash
nc 127.0.0.1 1313
```

The client does not need to send any data. Happy Daytime writes one timestamp
line and closes the connection.

Request data, if sent, is ignored.

### UDP

Send any UDP datagram:

```bash
echo hello | nc -u -w 1 127.0.0.1 1313
```

The contents of the datagram are ignored. Happy Daytime replies with one
timestamp datagram.

### IPv6

TCP:

```bash
nc -6 ::1 1313
```

UDP:

```bash
echo hello | nc -6 -u -w 1 ::1 1313
```

## Docker

Build the image:

```bash
docker build -t happy-daytime .
```

Run TCP and UDP on the unprivileged container port:

```bash
docker run --rm \
  -p 1313:1313/tcp \
  -p 1313:1313/udp \
  happy-daytime
```

Publish the canonical RFC 867 Daytime port while keeping the container process
non-root:

```bash
docker run --rm \
  -p 13:1313/tcp \
  -p 13:1313/udp \
  happy-daytime
```

Binding host port 13 may require root or appropriate host-level privileges on
Linux and macOS. The container process itself remains non-root and does not need
additional Linux capabilities.

The Docker image defaults to:

```dockerfile
ENV Daytime__ListenAddress=::
ENV Daytime__DualMode=true
ENV Daytime__Port=1313
ENV Daytime__UdpEnabled=true
ENV Daytime__MaxConcurrentConnections=100
```

`UdpListenAddress` and `UdpPort` are not overridden in the image, so UDP inherits
the TCP listen address and port.

The final image uses `mcr.microsoft.com/dotnet/runtime-deps:10.0-alpine`. It
contains the Native AOT executable and native runtime dependencies only; it does
not contain the full managed .NET runtime.

## Configuration

Settings live in the `Daytime` section of
[`appsettings.json`](HappyDaytime/appsettings.json).

Environment variables use two underscores (`__`) as section separators.

| Setting | Environment variable | Default | Description |
| --- | --- | --- | --- |
| `ListenAddress` | `Daytime__ListenAddress` | `::` | TCP listen address |
| `DualMode` | `Daytime__DualMode` | `true` | Enables IPv4/IPv6 dual-stack operation when listening on `::` |
| `Port` | `Daytime__Port` | `13` | TCP listening port |
| `UdpEnabled` | `Daytime__UdpEnabled` | `true` | Enables the UDP Daytime listener |
| `UdpListenAddress` | `Daytime__UdpListenAddress` | `null` | UDP listen address; `null` inherits `ListenAddress` |
| `UdpPort` | `Daytime__UdpPort` | `null` | UDP port; `null` inherits `Port` |
| `MaxConcurrentConnections` | `Daytime__MaxConcurrentConnections` | `64` | Maximum concurrent TCP connections |
| `RequestTimeoutSeconds` | `Daytime__RequestTimeoutSeconds` | `15` | Timeout for writing a TCP response |
| `TelemetryIgnoredRemoteAddresses` | `Daytime__TelemetryIgnoredRemoteAddresses__0` | `[]` | Client IP addresses excluded from request telemetry |

The default configuration listens on the IPv6 wildcard address (`::`) with
dual mode enabled, allowing both IPv4 and IPv6 clients on systems that support
dual-stack sockets.

Example IPv4-only configuration:

```bash
Daytime__ListenAddress=0.0.0.0 \
Daytime__DualMode=false \
Daytime__Port=1313 \
Daytime__UdpEnabled=true \
dotnet run --project HappyDaytime/HappyDaytime.csproj
```

Example with UDP on a different port:

```bash
Daytime__Port=1313 \
Daytime__UdpPort=1314 \
dotnet run --project HappyDaytime/HappyDaytime.csproj
```

Example excluding multiple client addresses from TCP and UDP request telemetry:

```bash
Daytime__TelemetryIgnoredRemoteAddresses__0=127.0.0.1 \
Daytime__TelemetryIgnoredRemoteAddresses__1=192.0.2.10 \
dotnet run --project HappyDaytime/HappyDaytime.csproj
```

Ignored clients still receive normal Daytime responses; only request telemetry
is suppressed.

Invalid port, connection-limit, timeout, or incompatible dual-mode values are
rejected when the application starts.

## Build And Publish

```bash
dotnet restore HappyDaytime.slnx

dotnet build HappyDaytime.slnx \
  --configuration Release \
  --no-restore

dotnet test HappyDaytime.slnx \
  --configuration Release \
  --no-build

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

Request telemetry includes a `Protocol` value of either `tcp` or `udp`.

Telemetry failures never prevent Happy Daytime from serving protocol requests.

For TCP, request telemetry is registered after the connection closes so slow
telemetry does not hold the client connection or consume a connection slot.

For UDP, the response is sent before telemetry publication and telemetry is not
awaited by the datagram receive loop, so slow Mission Control publishing does
not prevent subsequent datagrams from being served.

Telemetry publication uses a bounded timeout and handles Mission Control
failures as best-effort observability failures rather than protocol failures.

## License

Happy Daytime is available under the [MIT License](LICENSE).
