﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using Dapper;
using Dapper.Database.Extensions;
using DapperQueryBuilder;
using EMQ.Server.Db.Entities;
using EMQ.Shared.Core;
using EMQ.Shared.Quiz.Entities.Concrete;
using Npgsql;

namespace EMQ.Server.Db.Imports.EGS;

public static class EgsImporter
{
    public static readonly Dictionary<int, List<string>> CreaterNameOverrideDict = new();

    // todo use xrefs (or just import xrefs to the db and use those tbh)
    public static async Task ImportEgsData(DateTime dateTime, bool calledFromApi)
    {
        if (ConnectionHelper.GetConnectionString().Contains("AUTH"))
        {
            throw new Exception("wrong db");
        }

        string date = dateTime.ToString("yyyy-MM-dd");
        string folderPrelude;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            folderPrelude = "C:/emq/egs/";
        }
        else
        {
            folderPrelude = "egsimporter/";
            Console.WriteLine($"{Directory.GetCurrentDirectory()}/{folderPrelude}");
        }

        string folder = $"{folderPrelude}{date}";
        var createrNameOverrideDictFile =
            JsonSerializer.Deserialize<Dictionary<int, List<string>>>(
                await File.ReadAllTextAsync($"{folderPrelude}createrNameOverrideDict.json"), Utils.JsoIndented)!;
        foreach ((int key, List<string>? value) in createrNameOverrideDictFile)
        {
            if (!CreaterNameOverrideDict.TryAdd(key, value))
            {
                throw new Exception($"creater id {key} is repeated in the override dict");
            }
        }

        var egsDataList = new List<EgsData>();
        string[] rows = await File.ReadAllLinesAsync($"{folder}/egs.tsv");
        for (int i = 1; i < rows.Length; i++)
        {
            string row = rows[i];
            string[] split = row.Split("\t");

            SongSourceSongType gameMusicCategory;
            if (split[4].Contains("OP") || split[4].Contains("主題歌") || split[4].Contains("テーマ"))
            {
                gameMusicCategory = SongSourceSongType.OP;
            }
            else if (split[4].Contains("ED"))
            {
                gameMusicCategory = SongSourceSongType.ED;
            }
            else if (split[4].Contains("挿入歌") || split[4].Contains("イメージソング") || split[4].Contains("キャラソン") ||
                     split[4].Contains("イメーシーソング") || split[4].Contains("イメージ") || split[4].Contains("イメ－ジソング") ||
                     split[4].Contains("IM"))
            {
                gameMusicCategory = SongSourceSongType.Insert;
            }
            else if (split[4].Contains("BGM"))
            {
                gameMusicCategory = SongSourceSongType.BGM;
            }
            else if (split[4].Contains("Vo楽曲") || split[4].Contains("game size") || split[4].Contains("インストゥルメンタル") ||
                     split[4].Contains("アレンジ") || split[4].Contains("収録曲") || split[4].Contains("ボーカルソング") ||
                     split[4].Contains("特別曲") || split[4].Contains("本編未使用曲") || split[4].Contains("スペシャルソング") ||
                     split[4].Contains("関連曲") || split[4].Contains("ファーストソング") || split[4].Contains("歌曲") ||
                     split[4].Contains("ワールドソング") || split[4].Contains("ユアソング") || split[4].Contains("劇中歌") ||
                     split[4].Contains("リミックス") || split[4].Contains("新ミックス") || split[4].Contains("新録") ||
                     split[4].Contains("カバー"))
            {
                gameMusicCategory = SongSourceSongType.Unknown;
            }
            else
            {
                throw new ArgumentOutOfRangeException("", $"mId {Convert.ToInt32(split[0])} " + split[4]);
            }

            if (gameMusicCategory == SongSourceSongType.Unknown)
            {
                // Console.WriteLine("Skipping row with Unknown GameMusicCategory");
                continue;
            }

            string gameVndbUrl = split[6];
            if (string.IsNullOrWhiteSpace(gameVndbUrl))
            {
                // todo try using the EGS links attached to VNDB releases
                // Console.WriteLine("Skipping row with no GameVndbUrl");
                continue;
            }

            gameVndbUrl = gameVndbUrl.ToVndbUrl();
            if (Blacklists.EgsImporterBlacklist.Contains(gameVndbUrl))
            {
                continue;
            }

            int createrId = Convert.ToInt32(split[13]);

            List<string> createrName = new() { split[14] };
            if (CreaterNameOverrideDict.ContainsKey(createrId))
            {
                createrName = CreaterNameOverrideDict[createrId];
            }

            var egsData = new EgsData
            {
                MusicId = Convert.ToInt32(split[0]),
                MusicName = split[1],
                MusicFurigana = split[2],
                MusicPlaytime = split[3],
                GameMusicCategory = gameMusicCategory,
                GameName = split[5],
                GameVndbUrl = gameVndbUrl,
                SingerCharacterName = split[9],
                SingerFeaturing = split[10] == "t",
                CreaterId = createrId,
                CreaterNames = createrName,
                CreaterFurigana = split[15],
                Composer = JsonSerializer.Deserialize<int?[]>(split[16])!,
                Arranger = JsonSerializer.Deserialize<int?[]>(split[17])!,
                Lyricist = JsonSerializer.Deserialize<int?[]>(split[18])!,
            };

            egsDataList.Add(egsData);
        }

        // remove duplicate items caused by EGS storing releases as separate games
        List<EgsData> toRemove = new();
        for (int i = 0; i < egsDataList.Count; i++)
        {
            EgsData egsData = egsDataList[i];

            var match = egsDataList.Skip(i + 1).Where(x =>
                x.MusicName == egsData.MusicName &&
                x.GameVndbUrl == egsData.GameVndbUrl &&
                x.GameMusicCategory == egsData.GameMusicCategory &&
                x.CreaterNames.Any(y => egsData.CreaterNames.Contains(y)));

            toRemove.AddRange(match);
        }

        Console.WriteLine("toRemove count: " + toRemove.Count);
        foreach (EgsData egsData in toRemove)
        {
            egsDataList.Remove(egsData);
        }

        string serialized = JsonSerializer.Serialize(egsDataList, Utils.JsoIndented);
        await File.WriteAllTextAsync($"{folder}/egsData.json", serialized);
        // Console.WriteLine(serialized);

        // todo important batch
        // Attempt to match EGS songs with VNDB songs
        var egsImporterInnerResults = new ConcurrentBag<EgsImporterInnerResult>();
        var opt = new ParallelOptions { MaxDegreeOfParallelism = calledFromApi ? 1 : Environment.ProcessorCount };
        await Parallel.ForEachAsync(egsDataList, opt, async (egsData, _) =>
        {
            var innerResult = new EgsImporterInnerResult { EgsData = egsData };
            egsImporterInnerResults.Add(innerResult);

            await using (var connection = new NpgsqlConnection(ConnectionHelper.GetConnectionString()))
            {
                var queryMusic = connection
                    .QueryBuilder($@"SELECT m.id
FROM music m
LEFT JOIN music_source_music msm ON msm.music_id = m.id
LEFT JOIN music_source ms ON ms.id = msm.music_source_id
LEFT JOIN music_source_external_link msel ON msel.music_source_id = ms.id
LEFT JOIN artist_music am ON am.music_id = m.id
LEFT JOIN artist_alias aa ON aa.id = am.artist_alias_id
LEFT JOIN artist a ON a.id = aa.artist_id
/**where**/");

                // egs stores names without spaces, vndb stores them with spaces; brute-force conversion
                HashSet<int> aIds = (await DbManager.FindArtistIdsByArtistNames(egsData.CreaterNames))
                    .Select(x => x.Item1).ToHashSet();
                if (!aIds.Any())
                {
                    if (CreaterNameOverrideDict.ContainsKey(egsData.CreaterId))
                    {
                        Console.WriteLine(
                            $"ERROR: invalid creater name override for {egsData.CreaterId}{JsonSerializer.Serialize(egsData.CreaterNames, Utils.Jso)}");
                    }

                    innerResult.ResultKind = EgsImporterInnerResultKind.NoAids;
                    return;
                }

                innerResult.aIds = aIds.ToList();

                queryMusic.Where($"m.data_source={(int)DataSourceKind.VNDB}");
                queryMusic.Where($"msel.url={egsData.GameVndbUrl}");
                queryMusic.Where($"msm.type={(int)egsData.GameMusicCategory}");
                queryMusic.Where($"a.id = ANY({aIds.ToArray()})");
                queryMusic.Where($"am.role = {(int)SongArtistRole.Vocals})");

                var mIds = (await queryMusic.QueryAsync<int?>()).ToHashSet();
                if (mIds.Count > 1)
                {
                    innerResult.ResultKind = EgsImporterInnerResultKind.MultipleMids;
                    foreach (int? mid1 in mIds)
                    {
                        if (mid1 != null)
                        {
                            innerResult.mIds.Add(mid1.Value);
                        }
                    }

                    // todo try automatically romanizing the title somehow and matching by it
                    return;
                }

                var mid = mIds.SingleOrDefault();
                if (mid != null)
                {
                    innerResult.ResultKind = EgsImporterInnerResultKind.Matched;
                    innerResult.mIds.Add(mid.Value);
                }
                else
                {
                    innerResult.ResultKind = EgsImporterInnerResultKind.NoMids;
                    return;
                }
            }
        });

        foreach (EgsImporterInnerResult egsImporterInnerResult in egsImporterInnerResults)
        {
            bool needsPrint;
            switch (egsImporterInnerResult.ResultKind)
            {
                case EgsImporterInnerResultKind.Matched:
                    // Console.WriteLine("matched: ");
                    needsPrint = false;
                    break;
                case EgsImporterInnerResultKind.MultipleMids:
                    Console.WriteLine("multiple music matches: ");
                    needsPrint = true;
                    break;
                case EgsImporterInnerResultKind.NoMids:
                    Console.WriteLine("no music matches: ");
                    needsPrint = true;
                    break;
                case EgsImporterInnerResultKind.NoAids:
                    Console.WriteLine("no artist id matches: ");
                    needsPrint = true;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            if (needsPrint)
            {
                Console.WriteLine(JsonSerializer.Serialize(egsImporterInnerResult.EgsData.MusicName, Utils.Jso));
                Console.WriteLine(egsImporterInnerResult.EgsData.CreaterId +
                                  JsonSerializer.Serialize(egsImporterInnerResult.EgsData.CreaterNames, Utils.Jso));
                Console.WriteLine(JsonSerializer.Serialize(egsImporterInnerResult.EgsData.GameVndbUrl,
                    Utils.Jso));
                Console.WriteLine(JsonSerializer.Serialize((int)egsImporterInnerResult.EgsData.GameMusicCategory,
                    Utils.Jso));
                Console.WriteLine("--------------");
            }
        }

        Console.WriteLine("total count: " + egsDataList.Count);
        Console.WriteLine("matchedMids count: " +
                          egsImporterInnerResults.Count(x => x.ResultKind == EgsImporterInnerResultKind.Matched));
        Console.WriteLine("multipleMids count: " +
                          egsImporterInnerResults.Count(x => x.ResultKind == EgsImporterInnerResultKind.MultipleMids));
        Console.WriteLine("noMids count: " +
                          egsImporterInnerResults.Count(x => x.ResultKind == EgsImporterInnerResultKind.NoMids));
        Console.WriteLine("noAids count: " +
                          egsImporterInnerResults.Count(x => x.ResultKind == EgsImporterInnerResultKind.NoAids));

        await File.WriteAllTextAsync($"{folder}/matched.json",
            JsonSerializer.Serialize(
                egsImporterInnerResults.Where(x => x.ResultKind == EgsImporterInnerResultKind.Matched),
                Utils.JsoIndented));

        await File.WriteAllTextAsync($"{folder}/noMids.json",
            JsonSerializer.Serialize(
                egsImporterInnerResults.Where(x => x.ResultKind == EgsImporterInnerResultKind.NoMids),
                Utils.JsoIndented));

        await File.WriteAllTextAsync($"{folder}/noMids_noins.json",
            JsonSerializer.Serialize(
                egsImporterInnerResults.Where(x =>
                    x.ResultKind == EgsImporterInnerResultKind.NoMids &&
                    x.EgsData.GameMusicCategory != SongSourceSongType.Insert),
                Utils.JsoIndented));

        await File.WriteAllTextAsync($"{folder}/noAids.json",
            JsonSerializer.Serialize(
                egsImporterInnerResults.Where(x => x.ResultKind == EgsImporterInnerResultKind.NoAids),
                Utils.JsoIndented));

        await File.WriteAllTextAsync($"{folder}/noAids_noins.json",
            JsonSerializer.Serialize(
                egsImporterInnerResults.Where(x =>
                    x.ResultKind == EgsImporterInnerResultKind.NoAids &&
                    x.EgsData.GameMusicCategory != SongSourceSongType.Insert),
                Utils.JsoIndented));

        // return; // todo
        // Insert the matched results
        var matchedResults = egsImporterInnerResults.Where(x => x.ResultKind == EgsImporterInnerResultKind.Matched)
            .ToArray();

        HashSet<int> mIdsWithExistingMels =
            (await new NpgsqlConnection(ConnectionHelper.GetConnectionString()).QueryAsync<int>(
                $"select music_id from music_external_link where type = {(int)SongLinkType.ErogameScapeMusic} AND music_id = ANY(@mIds)",
                new { mIds = matchedResults.Select(x => x.mIds.Single()).ToArray() })).ToHashSet();

        await using var connection = new NpgsqlConnection(ConnectionHelper.GetConnectionString());
        foreach (EgsImporterInnerResult innerResult in matchedResults)
        {
            int mId = innerResult.mIds.Single();
            // todo? don't insert if latin title is the same after normalization
            int rowsUpdate = await connection.ExecuteAsync(
                @"UPDATE music_title mt SET non_latin_title = @mtNonLatinTitle
WHERE (mt.non_latin_title IS NULL OR mt.non_latin_title = '') AND mt.music_id = @mId AND mt.language='ja' AND mt.is_main_title=true",
                new { mtNonLatinTitle = innerResult.EgsData.MusicName, mId });
            if (rowsUpdate <= 0)
            {
                // Console.WriteLine($"Error updating music_title for mId {mId} {innerResult.EgsData.MusicName}");
            }
            else
            {
                Console.WriteLine($"updated music_title for mId {mId} {innerResult.EgsData.MusicName}");
            }

            if (!mIdsWithExistingMels.Contains(mId))
            {
                bool success = await connection.InsertAsync(new MusicExternalLink
                {
                    music_id = mId,
                    url =
                        $"https://erogamescape.dyndns.org/~ap2/ero/toukei_kaiseki/music.php?music={innerResult.EgsData.MusicId}",
                    type = SongLinkType.ErogameScapeMusic,
                    is_video = false,
                    duration = default,
                    submitted_by = "Cookie4IS",
                    sha256 = "",
                    analysis_raw = null
                });
                if (!success)
                {
                    Console.WriteLine(
                        $"Error inserting music_external_link for mId {mId} https://erogamescape.dyndns.org/~ap2/ero/toukei_kaiseki/music.php?music={innerResult.EgsData.MusicId}");
                }
                else
                {
                    Console.WriteLine(
                        $"inserted music_external_link for mId {mId} https://erogamescape.dyndns.org/~ap2/ero/toukei_kaiseki/music.php?music={innerResult.EgsData.MusicId}");
                    mIdsWithExistingMels.Add(mId);
                }
            }

            // todo? insert egs game links
        }
    }
}

public class EgsImporterInnerResult
{
    public EgsData EgsData { get; set; }

    public List<int> aIds { get; set; } = new();

    public List<int> mIds { get; set; } = new();

    public EgsImporterInnerResultKind ResultKind { get; set; }
}

public enum EgsImporterInnerResultKind
{
    NoAids,
    MultipleMids,
    NoMids,
    Matched,
}
