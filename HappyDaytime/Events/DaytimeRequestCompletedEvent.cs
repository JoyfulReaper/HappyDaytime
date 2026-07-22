/*
 * Happy Daytime Service
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License.
 */

namespace HappyDaytime.Events;

public sealed record DaytimeRequestCompletedEvent(
    string Remote,
    string Response,
    long DurationMilliseconds,
    string Outcome,
    bool Succeeded)
{
    public const string EventName = "happydaytime.request.completed";
}