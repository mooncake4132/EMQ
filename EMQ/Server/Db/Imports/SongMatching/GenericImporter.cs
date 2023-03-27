﻿using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using EMQ.Server.Db.Imports.SongMatching.Common;

namespace EMQ.Server.Db.Imports.SongMatching;

public static class GenericImporter
{
    public static async Task ImportGeneric()
    {
        string dir = "L:\\olil355 - Copy\\FolderA";
        // dir = "M:\\a";
        var regex = new Regex("\\. ()(.*) - \\((.*)\\).mp3", RegexOptions.Compiled);
        string extension = "*";

        var songMatches = SongMatcher.ParseSongFile(dir, regex, extension, false, false);
        await SongMatcher.Match(songMatches, "C:\\emq\\matching\\generic\\olil355_F_1", false);
    }

    // public static async Task ImportGeneric()
    // {
    //     string dir = "L:\\8b\\TOSORT\\agm";
    //     // dir = "M:\\a";
    //     var regex = new Regex("", RegexOptions.Compiled);
    //     string extension = "*";
    //
    //     var songMatches = SongMatcher.ParseSongFile(dir, regex, extension, true);
    //     await SongMatcher.Match(songMatches, "C:\\emq\\matching\\generic\\gi_2-agm2", false);
    // }

    // public static async Task ImportGeneric()
    // {
    //     string dir = "G:\\Music";
    //     // dir = "M:\\a";
    //     var regex = new Regex("", RegexOptions.Compiled);
    //     string extension = "*";
    //
    //     var songMatches = SongMatcher.ParseSongFile(dir, regex, extension, true);
    //     await SongMatcher.Match(songMatches, "C:\\emq\\matching\\generic\\gi_1-gmusic2", false);
    // }

    public static async Task ImportGenericWithDir(string dir, int num)
    {
        string artistDirName = Path.GetFileName(dir);
        // dir = "M:\\a";
        var regex = new Regex("", RegexOptions.Compiled);
        string extension = "*";

        var songMatches = SongMatcher.ParseSongFile(dir, regex, extension, true);
        await SongMatcher.Match(songMatches, $"C:\\emq\\matching\\generic\\{artistDirName}_{num}", false);
    }
}
