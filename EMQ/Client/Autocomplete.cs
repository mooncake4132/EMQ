﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EMQ.Shared.Quiz.Entities.Concrete;

namespace EMQ.Client;

public static class Autocomplete
{
    public static IEnumerable<string> SearchAutocompleteMst(string[] data, string arg)
    {
        // todo prefer Japanese latin titles
        //var exactMatch = data.Where(x => string.Equals(x, arg, StringComparison.OrdinalIgnoreCase)).OrderBy(x => x);
        var startsWith = data.Where(x => x.ToLowerInvariant().StartsWith(arg.ToLowerInvariant())).OrderBy(x => x);
        var contains = data.Where(x => x.ToLowerInvariant().Contains(arg.ToLowerInvariant())).OrderBy(x => x);

        string[] final = (startsWith.Concat(contains)).Distinct().ToArray();
        // _logger.LogInformation(JsonSerializer.Serialize(final));
        return final.Any() ? final.Take(25) : Array.Empty<string>();
    }

    public static IEnumerable<SongSourceCategory> SearchAutocompleteC(SongSourceCategory[] data, string arg)
    {
        // todo prefer Japanese latin titles
        var startsWith = data.Where(x => x.Name.ToLowerInvariant().StartsWith(arg.ToLowerInvariant())).OrderBy(x => x);
        var contains = data.Where(x => x.Name.ToLowerInvariant().Contains(arg.ToLowerInvariant())).OrderBy(x => x);

        var startsWith1 = data.Where(x => x.VndbId!.ToLowerInvariant().StartsWith(arg.ToLowerInvariant()))
            .OrderBy(x => x);
        var contains1 = data.Where(x => x.VndbId!.ToLowerInvariant().Contains(arg.ToLowerInvariant())).OrderBy(x => x);

        var final = startsWith.Concat(startsWith1).Concat(contains).Concat(contains1).Distinct().ToArray();
        // _logger.LogInformation(JsonSerializer.Serialize(final));
        return final.Any() ? final.Take(25) : Array.Empty<SongSourceCategory>();
    }
}
