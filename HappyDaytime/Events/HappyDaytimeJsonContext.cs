/*
 * Happy Daytime Service
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License.
 */

using HappyDaytime.Events;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HappyDaytime;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(DaytimeServiceStartedEvent))]
[JsonSerializable(typeof(DaytimeRequestCompletedEvent))]
internal sealed partial class HappyDaytimeJsonContext
    : JsonSerializerContext;