/*
 * Happy Daytime Server
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License.
 */

namespace HappyDaytime;

public sealed class HappyDaytimeOptions
{
    public const string SectionName = "Daytime";
    public string ListenAddress { get; init; } = "127.0.0.1";
    public int Port { get; init; } = 13;
    public int MaxConcurrentConnections { get; init; } = 64;
    public int RequestTimeoutSeconds { get; init; } = 15;
    public string? TelemetryIgnoredRemoteAddress { get; init; }
}
