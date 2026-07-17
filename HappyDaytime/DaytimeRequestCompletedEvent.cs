namespace HappyDaytime;

public sealed record DaytimeRequestCompletedEvent(
    string Remote,
    string Response,
    long DurationMilliseconds,
    string Outcome,
    bool Succeeded);