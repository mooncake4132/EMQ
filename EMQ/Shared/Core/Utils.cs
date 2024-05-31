﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace EMQ.Shared.Core;

public static class Utils
{
    public static JsonSerializerOptions Jso { get; } = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, Converters = { new JsonStringEnumConverter() }
    };

    public static JsonSerializerOptions JsoIndented { get; } = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = true
    };

    public static JsonSerializerOptions JsoNotNull { get; } = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string PercentageStr(int dividend, int divisor)
    {
        return $"{(((double)dividend / divisor) * 100):N2}%";
    }

    public static string PercentageStr(double dividend, double divisor)
    {
        return $"{((dividend / divisor) * 100):N2}%";
    }

    public static string FixFileName(string name)
    {
        return string.Join(" ", name.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
    }

    public static async Task WaitWhile(Func<bool> condition, int frequency = 25, int timeout = -1)
    {
        var waitTask = Task.Run(async () =>
        {
            while (condition()) await Task.Delay(frequency);
        });

        await Task.WhenAny(waitTask, Task.Delay(timeout));
    }

    public static (string modStr, int number) ParseVndbScreenshotStr(string screenshot)
    {
        int number = Convert.ToInt32(screenshot.Substring(2, screenshot.Length - 2));
        int mod = number % 100;
        string modStr = mod > 9 ? mod.ToString() : $"0{mod}";
        return (modStr, number);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string UserIdToUsername(Dictionary<int, string> dict, int userId)
    {
        return dict.TryGetValue(userId, out string? username) ? username : $"Guest-{userId}";
    }
}
