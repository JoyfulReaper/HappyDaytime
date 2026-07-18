/*
 * Happy Daytime Server
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License.
 */

namespace HappyDaytime;

public sealed record DaytimeRequestCompletedEvent(
    string Remote,
    string Response,
    long DurationMilliseconds,
    string Outcome,
    bool Succeeded);