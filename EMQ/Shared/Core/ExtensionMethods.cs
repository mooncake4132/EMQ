﻿using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using EMQ.Shared.Quiz.Entities.Concrete;

namespace EMQ.Shared.Core;

public static class ExtensionMethods
{
    public static string Base64Encode(this string plainText)
    {
        byte[] plainTextBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
        return System.Convert.ToBase64String(plainTextBytes);
    }

    public static string Base64Decode(this string base64EncodedData)
    {
        byte[] base64EncodedBytes = System.Convert.FromBase64String(base64EncodedData);
        return System.Text.Encoding.UTF8.GetString(base64EncodedBytes);
    }

    public static bool IsVideoLink(this string s)
    {
        return s.EndsWith(".webm") || s.EndsWith(".mp4");
    }

    public static string ToVndbUrl(this string? vndbId)
    {
        return "https://vndb.org/" + vndbId;
    }

    public static string ToVndbId(this string vndbUrl)
    {
        return vndbUrl.Replace("https://vndb.org/", "");
    }

    public static string SanitizeVndbAdvsearchStr(this string vndbAdvsearchStr)
    {
        try
        {
            // accept both full urls and just the f param
            var match = Regex.Match(vndbAdvsearchStr, "f=(.+)");
            return match.Success
                ? match.Groups[1].Value.Split('&')[0]
                : vndbAdvsearchStr;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return vndbAdvsearchStr;
        }
    }

    public static bool IsReachableFromCoords(this Point point, int x, int y,
        int maxDistance = LootingConstants.MaxDistance)
    {
        if (Math.Abs(point.X - x) < maxDistance &&
            Math.Abs(point.Y - y) < maxDistance)
        {
            return true;
        }

        return false;
    }

    // Falls back to name
    public static string? GetDescription(this Enum value)
    {
        Type type = value.GetType();
        string? name = Enum.GetName(type, value);
        if (name != null)
        {
            FieldInfo? field = type.GetField(name);
            if (field != null)
            {
                if (Attribute.GetCustomAttribute(field,
                        typeof(DescriptionAttribute)) is DescriptionAttribute attr)
                {
                    return attr.Description;
                }
                else
                {
                    return name;
                }
            }
        }

        return null;
    }

    // Falls back to name
    public static string? GetDisplayName(this Enum value)
    {
        Type type = value.GetType();
        string? name = Enum.GetName(type, value);
        if (name != null)
        {
            FieldInfo? field = type.GetField(name);
            if (field != null)
            {
                if (Attribute.GetCustomAttribute(field,
                        typeof(DisplayAttribute)) is DisplayAttribute attr)
                {
                    return attr.Name;
                }
                else
                {
                    return name;
                }
            }
        }

        return null;
    }

    public static T GetEnum<T>(this string description) where T : Enum
    {
        foreach (T enumItem in Enum.GetValues(typeof(T)))
        {
            if (enumItem.GetDescription() == description)
            {
                return enumItem;
            }
        }

        throw new ArgumentException("Not found.", nameof(description));
        // return default;
    }

    public static async Task<bool> DownloadFile(this HttpClient client, string dest, Uri uri)
    {
        try
        {
            Console.WriteLine($"DownloadFile {uri}");
            await using (var stream = await client.GetStreamAsync(uri))
            await using (var fs = new FileStream(dest, FileMode.OpenOrCreate))
            {
                await stream.CopyToAsync(fs);
                return true;
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return false;
        }
    }

    public static async Task<bool> DownloadFile2(string dest, Uri uri)
    {
        try
        {
            Console.WriteLine($"DownloadFile2 {uri}");
            using (var client = new HttpClient())
            await using (var stream = await client.GetStreamAsync(uri))
            await using (var fs = new FileStream(dest, FileMode.OpenOrCreate))
            {
                await stream.CopyToAsync(fs);
                return true;
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return false;
        }
    }

    public static string LastSegment(this string input)
    {
        return new Uri(input).Segments.Last();
    }

    public static SongLite ToSongLite(this Song song)
    {
        var songLite = new SongLite(
            song.Titles,
            song.Links,
            song.Sources.SelectMany(songSource =>
                songSource.Links.Where(songSourceLink => songSourceLink.Type == SongSourceLinkType.VNDB)
                    .Select(songSourceLink => songSourceLink.Url.ToVndbId())).ToList(),
            song.Artists.Select(artist => artist.VndbId ?? "").ToList(),
            song.Stats);
        return songLite;
    }

    public static string NormalizeForAutocomplete(this string input)
    {
        return new string(input
                .Trim()
                .ToLowerInvariant()
                .Normalize(NormalizationForm.FormD)
                .Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                .Where(y => (char.IsLetterOrDigit(y) || char.IsWhiteSpace(y)))
                .ToArray())
            .Normalize(NormalizationForm.FormC);
    }
}
