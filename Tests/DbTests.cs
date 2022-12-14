using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Dapper.Contrib.Extensions;
using EMQ.Server;
using EMQ.Server.Db;
using EMQ.Server.Db.Entities;
using EMQ.Server.Db.Imports;
using EMQ.Shared.Quiz.Entities.Concrete;
using Newtonsoft.Json;
using Npgsql;
using NUnit.Framework;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Tests;

public class DbTests
{
    [SetUp]
    public void Setup()
    {
    }

    [Test]
    public async Task Test_SelectSongs_ByTitles()
    {
        var songs = await DbManager.SelectSongs(new Song()
        {
            // Id = 210,
            Titles =new List<Title>(){new Title(){LatinTitle = "Restoration ~Chinmoku no Sora~"}, new Title(){LatinTitle = "SHOOTING STAR"}}
        });
        Assert.That(songs.Count == 3);
        foreach (var song in songs)
        {
            Assert.That(song.Id > 0);

            Assert.That(song.Titles.First().LatinTitle.Any());
            Assert.That(song.Titles.First().Language.Any());

            // Assert.That(song.Links.First().Url.Any());

            Assert.That(song.Sources.First().Id > 0);
            Assert.That(song.Sources.First().Titles.First().LatinTitle.Any());
            Assert.That(song.Sources.First().Titles.First().Language.Any());
            Assert.That(song.Sources.First().Links.First().Url.Any());
            // Assert.That(song.Sources.First().Categories.First().Name.Any());

            Assert.That(song.Artists.First().Id > 0);
            Assert.That(song.Artists.First().Titles.First().LatinTitle.Any());
        }
    }


    [Test]
    public async Task Test_FindSongsBySongSourceTitle_MultipleSongSourceTypes()
    {
        var songs = await DbManager.FindSongsBySongSourceTitle("Yoake Mae yori Ruri Iro na");
        var song = songs.First(x => x.Titles.Any(y => y.LatinTitle == "WAX & WANE"));
        Assert.That(song.Id > 0);

        Assert.That(song.Titles.First().LatinTitle.Any());
        Assert.That(song.Titles.First().Language.Any());

        // Assert.That(song.Links.First().Url.Any());

        Assert.That(song.Sources.First().Id > 0);
        Assert.That(song.Sources.First().Titles.First().LatinTitle.Any());
        Assert.That(song.Sources.First().Titles.First().Language.Any());
        Assert.That(song.Sources.First().Links.First().Url.Any());
        Assert.That(song.Sources.First().SongTypes.Count > 1);
        // Assert.That(song.Sources.First().Categories.First().Name.Any());

        Assert.That(song.Artists.First().Id > 0);
        Assert.That(song.Artists.First().Titles.First().LatinTitle.Any());
    }

    [Test]
    public async Task Test_FindSongsByArtistTitle_KOTOKO()
    {
        var songs = await DbManager.FindSongsByArtistTitle("KOTOKO");

        foreach (Song song in songs)
        {
            Assert.That(song.Id > 0);

            Assert.That(song.Titles.First().LatinTitle.Any());
            Assert.That(song.Titles.First().Language.Any());

            // Assert.That(song.Links.First().Url.Any());

            Assert.That(song.Sources.First().Id > 0);
            Assert.That(song.Sources.First().Titles.First().LatinTitle.Any());
            Assert.That(song.Sources.First().Titles.First().Language.Any());
            Assert.That(song.Sources.First().Links.First().Url.Any());
            Assert.That(song.Sources.First().SongTypes.Any());
            // Assert.That(song.Sources.First().Categories.First().Name.Any());

            Assert.That(song.Artists.First().Id > 0);
            Assert.That(song.Artists.First().Titles.First().LatinTitle.Any());
        }
    }

    [Test]
    public async Task Test_GetRandomSongs_100()
    {
        var songs = await DbManager.GetRandomSongs(100);

        // Assert.That(songs.Count > 0);
        foreach (Song song in songs)
        {
            Assert.That(song.Id > 0);
            Assert.That(song.Titles.First().LatinTitle.Any());
            // Assert.That(song.Links.First().Url.Any());
            Assert.That(song.Sources.First().Titles.First().LatinTitle.Any());
            // Assert.That(song.Sources.First().Categories.First().Name.Any());
            Assert.That(song.Artists.First().Titles.First().LatinTitle.Any());
        }
    }

    [Test, Explicit]
    public async Task Test_InsertSong()
    {
        var song = new Song()
        {
            Length = 266,
            Titles =
                new List<Title>()
                {
                    new Title() { LatinTitle = "Desire", Language = "en", IsMainTitle = true },
                    new Title() { LatinTitle = "Desire2", Language = "ja" }
                },
            Artists = new List<SongArtist>()
            {
                new SongArtist()
                {
                    Role = SongArtistRole.Vocals,
                    VndbId = "s1440",
                    PrimaryLanguage = "ja",
                    Titles =
                        new List<Title>()
                        {
                            new Title()
                            {
                                LatinTitle = "Misato Aki",
                                Language = "ja",
                                NonLatinTitle = "美郷あき",
                                IsMainTitle = true
                            }
                        },
                    Sex = Sex.Female
                }
            },
            Links =
                new List<SongLink>()
                {
                    new SongLink()
                    {
                        Url = "https://files.catbox.moe/3dep3s.mp3", IsVideo = false, Type = SongLinkType.Catbox
                    }
                },
            Sources = new List<SongSource>()
            {
                new SongSource()
                {
                    AirDateStart = DateTime.Now,
                    SongTypes = new List<SongSourceSongType> { SongSourceSongType.OP },
                    LanguageOriginal = "ja",
                    Type = SongSourceType.VN,
                    Links = new List<SongSourceLink>()
                    {
                        new SongSourceLink() { Type = SongSourceLinkType.VNDB, Url = "https://vndb.org/v10680" }
                    },
                    Titles =
                        new List<Title>()
                        {
                            new Title()
                            {
                                LatinTitle = "Tsuki ni Yorisou Otome no Sahou",
                                Language = "ja",
                                NonLatinTitle = "月に寄りそう乙女の作法",
                                IsMainTitle = true
                            },
                            new Title() { LatinTitle = "Tsuriotsu", Language = "en" }
                        },
                    Categories = new List<SongSourceCategory>()
                    {
                        new SongSourceCategory() { Name = "cat1", Type = SongSourceCategoryType.Tag },
                        new SongSourceCategory() { Name = "cat2", Type = SongSourceCategoryType.Genre }
                    }
                },
            }
        };

        int mId = await DbManager.InsertSong(song);
        Console.WriteLine($"Inserted mId {mId}");
    }

    [Test, Explicit]
    public async Task GenerateAutocompleteJson()
    {
        await File.WriteAllTextAsync("autocomplete.json", await DbManager.SelectAutocomplete());
    }

    [Test, Explicit]
    public async Task ImportVndbData()
    {
        await VndbImporter.ImportVndbData();
    }

    [Test, Explicit]
    public async Task GenerateSong()
    {
        await File.WriteAllTextAsync("Song.json", await DbManager.ExportSong());
    }

    [Test, Explicit]
    public async Task GenerateSongLite()
    {
        await File.WriteAllTextAsync("SongLite.json", await DbManager.ExportSongLite());
    }

    [Test, Explicit]
    public async Task ImportSongLite()
    {
        var deserialized =
            JsonConvert.DeserializeObject<List<SongLite>>(
                await File.ReadAllTextAsync("C:\\emq\\emqsongsmetadata\\SongLite.json"));
        await DbManager.ImportSongLite(deserialized!);
    }
}
