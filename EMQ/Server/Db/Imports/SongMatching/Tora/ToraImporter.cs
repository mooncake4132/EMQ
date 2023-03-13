﻿using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace EMQ.Server.Db.Imports.SongMatching.Tora;

public static class ToraImporter
{
    public static async Task ImportTora()
    {
        string dir = "M:\\[サントラ] ゲーム系曲集1-288 [MP3合集]";
        // dir = "M:\\a";
        var regex = new Regex("\\((.+)\\)(.+)().mp3", RegexOptions.Compiled);
        string extension = "mp3";

        var songMatches = SongMatcher.ParseSongFile(dir, regex, extension);
        await SongMatcher.Match(songMatches, "C:\\emq\\matching\\tora\\tora_3");
    }
}
