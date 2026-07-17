namespace HappyDaytime;

public sealed record FingerRequestCompletedEvent(
    bool RequestReceived,
    int RequestLength,
    string Remote,
    string Response,
    long DurationMilliseconds,
    string Outcome,
    bool Succeeded);