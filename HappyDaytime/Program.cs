/*
 * Happy Daytime Server
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License.
 */

using HappyDaytime;
using JoyfulReaperLib.MissionControl;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "Happy Daytime Server";
});

builder.Services
    .AddOptions<HappyDaytimeOptions>()
    .Bind(builder.Configuration.GetSection(HappyDaytimeOptions.SectionName))
    .Validate(options => options.Port is > 0 and <= 65535, "Daytime:Port must be between 1 and 65535.")
    .Validate(options => options.MaxConcurrentConnections > 0, "Daytime:MaxConcurrentConnections must be positive.")
    .Validate(options => options.RequestTimeoutSeconds > 0, "Daytime:RequestTimeoutSeconds must be positive.")
    .ValidateOnStart();

builder.Services.AddMissionControlClient(
    builder.Configuration.GetSection(
        MissionControlClientOptions.SectionName));

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
