/*
 * Happy Daytime Service
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License.
 */

using System.Globalization;
using System.Text;

namespace HappyDaytime;

internal static class DaytimeResponseFormatter
{
    public static string Format(DateTimeOffset timestamp) =>
        timestamp
            .ToUniversalTime()
            .ToString("O", CultureInfo.InvariantCulture);

    public static byte[] Encode(string response) =>
        Encoding.ASCII.GetBytes(response + "\r\n");
}