/*
 * Happy Daytime Service
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License.
 */

namespace HappyDaytime.Events;

public sealed record DaytimeServiceStartedEvent(string ListenAddress)
{
    public const string EventName = "happydaytime.service.started";
}