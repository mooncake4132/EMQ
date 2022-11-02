﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using EMQ.Shared.Quiz.Entities.Concrete;
using Dapper;
using Dapper.Contrib.Extensions;
using EMQ.Server.Db.Entities;
using EMQ.Shared.Core;
using Npgsql;

namespace EMQ.Server.Db;

public static class DbManager
{
    public static async Task<Song> SelectSong(int mId)
    {
        const string sqlMusic =
            @"SELECT *
            FROM music m
            LEFT JOIN music_title mt ON mt.music_id = m.id
            LEFT JOIN music_external_link mel ON mel.music_id = m.id
            where m.id = @mId
            ";

        const string sqlMusicSource =
            @"SELECT *
            FROM music_source_music msm
            LEFT JOIN music_source ms ON ms.id = msm.music_source_id
            LEFT JOIN music_source_title mst ON mst.music_source_id = ms.id
            LEFT JOIN music_source_external_link msel ON msel.music_source_id = ms.id
            LEFT JOIN music_source_category msc ON msc.music_source_id = ms.id
            LEFT JOIN category c ON c.id = msc.category_id
            where msm.music_id = @mId
            ";

        const string sqlArtist =
            @"SELECT *
            FROM artist_music am
            LEFT JOIN artist_alias aa ON aa.id = am.artist_alias_id
            LEFT JOIN artist a ON a.id = aa.artist_id
            where am.music_id = @mId
            ";

        await using (var connection = new NpgsqlConnection(ConnectionHelper.GetConnectionString()))
        {
            var song = new Song();
            var songTitles = new List<Title>();
            var songLinks = new List<SongLink>();
            var songSources = new List<SongSource>();
            // var songSourceTitles = new List<SongSourceTitle>();
            // var songSourceLinks = new List<SongSourceLink>();
            // var songSourceCategories = new List<SongSourceCategory>();
            var songArtists = new List<SongArtist>();
            // var songArtistAliases = new List<SongArtistAlias>();

            await connection.QueryAsync(sqlMusic,
                new[] { typeof(Music), typeof(MusicTitle), typeof(MusicExternalLink), }, (objects) =>
                {
                    // Console.WriteLine(JsonSerializer.Serialize(objects, Utils.Jso));
                    var music = (Music)objects[0];
                    var musicTitle = (MusicTitle)objects[1];
                    var musicExternalLink = (MusicExternalLink?)objects[2];

                    song.Id = music.id;
                    song.Type = (SongType)music.type;

                    if (music.length.HasValue)
                    {
                        song.Length = music.length.Value;
                    }

                    songTitles.Add(new Title()
                    {
                        LatinTitle = musicTitle.latin_title,
                        NonLatinTitle = musicTitle.non_latin_title,
                        Language = musicTitle.language,
                        IsMainTitle = musicTitle.is_main_title
                    });

                    if (musicExternalLink is not null)
                    {
                        songLinks.Add(new SongLink()
                        {
                            Url = musicExternalLink.url,
                            IsVideo = musicExternalLink.is_video,
                            Type = (SongLinkType)musicExternalLink.type
                        });
                    }

                    return 0;
                },
                splitOn:
                "music_id,music_id", param: new { mId });
            song.Titles = songTitles.DistinctBy(x => x.LatinTitle).ToList();
            song.Links = songLinks.DistinctBy(x => x.Url).ToList();

            await connection.QueryAsync(sqlMusicSource,
                new[]
                {
                    typeof(MusicSourceMusic), typeof(MusicSource), typeof(MusicSourceTitle),
                    typeof(MusicSourceExternalLink), typeof(MusicSourceCategory), typeof(Category)
                }, (objects) =>
                {
                    // Console.WriteLine(JsonSerializer.Serialize(objects, Utils.Jso));
                    var musicSourceMusic = (MusicSourceMusic)objects[0];
                    var musicSource = (MusicSource)objects[1];
                    var musicSourceTitle = (MusicSourceTitle)objects[2];
                    var musicSourceExternalLink = (MusicSourceExternalLink?)objects[3];
                    var category = (Category?)objects[5];

                    var existingSongSource = songSources.Where(x => x.Id == musicSource.id).ToList().SingleOrDefault();
                    if (existingSongSource is null)
                    {
                        songSources.Add(new SongSource()
                        {
                            Id = musicSource.id,
                            Type = (SongSourceType)musicSource.type,
                            AirDateStart = musicSource.air_date_start,
                            AirDateEnd = musicSource.air_date_end,
                            LanguageOriginal = musicSource.language_original,
                            RatingAverage = musicSource.rating_average,
                            SongType = (SongSourceSongType)musicSourceMusic.type,
                            Titles = new List<Title>
                            {
                                new Title()
                                {
                                    LatinTitle = musicSourceTitle.latin_title,
                                    NonLatinTitle = musicSourceTitle.non_latin_title,
                                    Language = musicSourceTitle.language,
                                    IsMainTitle = musicSourceTitle.is_main_title
                                }
                            },
                            Links = new List<SongSourceLink>() { },
                            Categories = new List<SongSourceCategory>() { }
                        });

                        if (musicSourceExternalLink is not null)
                        {
                            songSources.Last().Links.Add(new SongSourceLink()
                            {
                                Url = musicSourceExternalLink.url,
                                Type = (SongSourceLinkType)musicSourceExternalLink.type
                            });
                        }

                        if (category is not null)
                        {
                            songSources.Last().Categories.Add(new SongSourceCategory()
                            {
                                Name = category.name, Type = (SongSourceCategoryType)category.type
                            });
                        }
                    }
                    else
                    {
                        if (!existingSongSource.Titles.Any(x => x.LatinTitle == musicSourceTitle.latin_title))
                        {
                            existingSongSource.Titles.Add(new Title()
                            {
                                LatinTitle = musicSourceTitle.latin_title,
                                NonLatinTitle = musicSourceTitle.non_latin_title,
                                Language = musicSourceTitle.language
                            });
                        }

                        if (!existingSongSource.Categories.Any(x => x.Name == category.name))
                        {
                            existingSongSource.Categories.Add(new SongSourceCategory()
                            {
                                Name = category.name, Type = (SongSourceCategoryType)category.type
                            });
                        }
                    }

                    return 0;
                },
                splitOn:
                "id,music_source_id,music_source_id,music_source_id,id",
                param: new { mId });
            song.Sources = songSources;

            await connection.QueryAsync(sqlArtist,
                new[] { typeof(ArtistMusic), typeof(ArtistAlias), typeof(Artist), }, (objects) =>
                {
                    // Console.WriteLine(JsonSerializer.Serialize(objects, Utils.Jso));
                    var artistMusic = (ArtistMusic)objects[0];
                    var artistAlias = (ArtistAlias)objects[1];
                    var artist = (Artist)objects[2];

                    var existingArtist = songArtists.Where(x => x.Id == artist.id).ToList().SingleOrDefault();
                    if (existingArtist is null)
                    {
                        var songArtist = new SongArtist()
                        {
                            Id = artist.id,
                            PrimaryLanguage = artist.primary_language,
                            VndbId = artist.vndb_id,
                            Titles = new List<Title>()
                            {
                                new Title()
                                {
                                    LatinTitle = artistAlias.latin_alias,
                                    NonLatinTitle = artistAlias.non_latin_alias,
                                    IsMainTitle = artistAlias.is_main_name
                                },
                            }
                        };

                        if (artist.sex.HasValue)
                        {
                            songArtist.Sex = (Sex)artist.sex;
                        }

                        songArtist.Role = (SongArtistRole)artistMusic.role;

                        songArtists.Add(songArtist);
                    }
                    else
                    {
                        if (!existingArtist.Titles.Any(x => x.LatinTitle == artistAlias.latin_alias))
                        {
                            existingArtist.Titles.Add(new Title()
                            {
                                LatinTitle = artistAlias.latin_alias, NonLatinTitle = artistAlias.non_latin_alias,
                            });
                        }
                    }

                    return 0;
                },
                splitOn:
                "id,id", param: new { mId });
            song.Artists = songArtists.ToList();

            Console.WriteLine("song: " + JsonSerializer.Serialize(song,
                new JsonSerializerOptions()
                {
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                    WriteIndented = true,
                    Converters = { new JsonStringEnumConverter() }
                }));
            return song;
        }
    }

    public static async Task<int> InsertSong(Song song)
    {
        // Console.WriteLine(JsonSerializer.Serialize(song, Utils.Jso));
        await using var connection = new NpgsqlConnection(ConnectionHelper.GetConnectionString());
        await connection.OpenAsync();
        await using (var transaction = await connection.BeginTransactionAsync())
        {
            var music = new Music() { type = (int)song.Type };
            if (song.Length > 0)
            {
                music.length = song.Length;
            }

            int mId = await connection.InsertAsync(music);

            foreach (Title songTitle in song.Titles)
            {
                int mtId = await connection.InsertAsync(new MusicTitle()
                {
                    music_id = mId,
                    latin_title = songTitle.LatinTitle,
                    non_latin_title = songTitle.NonLatinTitle,
                    language = songTitle.Language,
                    is_main_title = songTitle.IsMainTitle
                });
            }

            foreach (SongLink songLink in song.Links)
            {
                int melId = await connection.InsertAsync(new MusicExternalLink()
                {
                    music_id = mId, url = songLink.Url, type = (int)songLink.Type, is_video = songLink.IsVideo
                });
            }


            foreach (SongSource songSource in song.Sources)
            {
                string msVndbUrl = "";
                try
                {
                    msVndbUrl = song.Sources.Select(x =>
                        x.Links.Select(y => y.Url).Single(z => z.Contains("vndb"))).Single();
                }
                catch (Exception)
                {
                    // ignored
                }

                int msId = 0;
                if (!string.IsNullOrEmpty(msVndbUrl))
                {
                    msId = (await connection.QueryAsync<int>(
                        "select ms.id from music_source_external_link msel join music_source ms on ms.id = msel.music_source_id where msel.url=@mselUrl",
                        new { mselUrl = msVndbUrl })).ToList().SingleOrDefault();
                }

                if (msId > 0)
                {
                }
                else
                {
                    msId = await connection.InsertAsync(new MusicSource()
                    {
                        air_date_start = songSource.AirDateStart,
                        air_date_end = songSource.AirDateEnd,
                        language_original = songSource.LanguageOriginal,
                        rating_average = songSource.RatingAverage,
                        type = (int)songSource.Type
                    });

                    foreach (Title songSourceAlias in songSource.Titles)
                    {
                        int mstId = await connection.InsertAsync(new MusicSourceTitle()
                        {
                            music_source_id = msId,
                            latin_title = songSourceAlias.LatinTitle,
                            non_latin_title = songSourceAlias.NonLatinTitle,
                            language = songSourceAlias.Language,
                            is_main_title = songSourceAlias.IsMainTitle
                        });
                    }

                    foreach (SongSourceLink songSourceLink in songSource.Links)
                    {
                        int mselId = await connection.InsertAsync(new MusicSourceExternalLink()
                        {
                            music_source_id = msId, url = songSourceLink.Url, type = (int)songSourceLink.Type
                        });
                    }

                    foreach (SongSourceCategory songSourceCategory in songSource.Categories)
                    {
                        // todo uniq check
                        int cId = await connection.InsertAsync(new Category()
                        {
                            name = songSourceCategory.Name, type = (int)songSourceCategory.Type
                        });

                        int mscId = await connection.InsertAsync(
                            new MusicSourceCategory() { category_id = cId, music_source_id = msId });
                    }
                }

                int msmId = await connection.InsertAsync(new MusicSourceMusic()
                {
                    music_id = mId, music_source_id = msId, type = (int)songSource.SongType
                });
            }


            foreach (SongArtist songArtist in song.Artists)
            {
                if (songArtist.Titles.Count > 1)
                {
                    throw new Exception("Artists can only have one artist_alias per song");
                }

                int aId = 0;
                int aaId = 0;

                if (!string.IsNullOrEmpty(songArtist.VndbId))
                {
                    aId = (await connection.QueryAsync<int>(
                        "select a.id from artist a where a.vndb_id=@aVndbId",
                        new { aVndbId = songArtist.VndbId })).ToList().SingleOrDefault();
                }

                if (aId > 0)
                {
                    aaId = (await connection.QueryAsync<int>(
                            "select aa.id,aa.latin_alias from artist_alias aa join artist a on a.id = aa.artist_id where a.vndb_id=@aVndbId AND aa.latin_alias=@latinAlias",
                            new { aVndbId = songArtist.VndbId, latinAlias = songArtist.Titles.First().LatinTitle }))
                        .ToList().SingleOrDefault();
                }
                else
                {
                    aId = await connection.InsertAsync(new Artist()
                    {
                        primary_language = songArtist.PrimaryLanguage,
                        sex = (int)songArtist.Sex,
                        vndb_id = songArtist.VndbId
                    });
                }

                if (aaId < 1)
                {
                    foreach (Title songArtistAlias in songArtist.Titles)
                    {
                        aaId = await connection.InsertAsync(new ArtistAlias()
                        {
                            artist_id = aId,
                            latin_alias = songArtistAlias.LatinTitle,
                            non_latin_alias = songArtistAlias.NonLatinTitle,
                            is_main_name = songArtistAlias.IsMainTitle
                        });
                    }
                }

                if (mId < 1)
                {
                    throw new Exception("mId is invalid");
                }

                if (aaId < 1)
                {
                    throw new Exception("aaId is invalid");
                }

                int amId = await connection.InsertAsync(
                    new ArtistMusic()
                    {
                        music_id = mId, artist_id = aId, artist_alias_id = aaId, role = (int)songArtist.Role
                    });
            }

            await transaction.CommitAsync();
            return mId;
        }
    }

    public static async Task<List<Song>> GetRandomSongs(int numSongs)
    {
        // todo get ids from db & only get songs that have external links
        var songs = new List<Song>();
        var ids = new HashSet<int>();
        var rand = new Random();
        int numSongsInDb = 561; // todo

        while (ids.Count < numSongs)
        {
            ids.Add(rand.Next(1, numSongsInDb));
        }

        Console.WriteLine(JsonSerializer.Serialize(ids));
        foreach (int id in ids)
        {
            var song = await SelectSong(id);
            // if (!song.Links.Any())
            // {
            //     continue;
            // }

            song.StartTime = rand.Next(0, song.Length);
            songs.Add(song);
        }

        return songs;
    }

    public static async Task<string> SelectAutocomplete()
    {
        const string sqlAutocomplete =
            @"SELECT json_agg(mst.latin_title)
            FROM music_source_title mst
            ";

        await using (var connection = new NpgsqlConnection(ConnectionHelper.GetConnectionString()))
        {
            IEnumerable<string>? res = await connection.QueryAsync<string>(sqlAutocomplete);
            var deserialized = JsonSerializer.Deserialize<List<string>>(res.Single())!.Distinct().OrderBy(x => x);

            string autocomplete = JsonSerializer.Serialize(deserialized,
                new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            return autocomplete;
        }
    }
}
