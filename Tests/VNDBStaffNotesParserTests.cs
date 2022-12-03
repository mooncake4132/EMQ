﻿using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using VNDBStaffNotesParser;

namespace Tests;

public class VNDBStaffNotesParserTests
{
    [SetUp]
    public void Setup()
    {
    }

    [Test]
    public void Test_Single()
    {
        string input = "ED \"Twinkle Snow\"";

        var expected = new List<Song>
        {
            new()
            {
                BeforeType = "",
                Type = new List<SongType> { SongType.ED },
                Title = "Twinkle Snow",
                AfterTitle = ""
            }
        };

        var actual = VNDBStaffNotesParser.Program.Parse(input);
        Assert.AreEqual(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
    }

    [Test]
    public void Test_WeirdShitThatIDontKnowIfWeShouldAccept()
    {
        string input = "\"Kuroi Hitomi no Aria\", \"Sie Null\", ED \"DON'T LET GO\" [Remake]";

        var expected = new List<Song>
        {
            new()
            {
                BeforeType = "\"Kuroi Hitomi no Aria\", \"Sie Null\", ",
                Type = new List<SongType> { SongType.ED },
                Title = "DON'T LET GO",
                AfterTitle = " [Remake]"
            }
        };

        var actual = VNDBStaffNotesParser.Program.Parse(input);
        Assert.AreEqual(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
    }

    [Test]
    public void Test_PartialSuccess()
    {
        string input = "Nene ED \"Re:Start ~Kimi to Mata Deaete~\",Band performance \"Without You\"";

        var expected = new List<Song>
        {
            new()
            {
                BeforeType = "Nene ",
                Type = new List<SongType> { SongType.ED },
                Title = "Re:Start ~Kimi to Mata Deaete~",
                AfterTitle = ""
            }
        };

        var actual = VNDBStaffNotesParser.Program.Parse(input);
        Assert.AreEqual(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
    }

    [Test]
    public void Test_SingleWithBeforeSongTypeAndAfterTitle()
    {
        string input = "Shuusuke ED \"Hohoemi Genocide\" (credited as Alex3)";

        var expected = new List<Song>
        {
            new()
            {
                BeforeType = "Shuusuke ",
                Type = new List<SongType> { SongType.ED },
                Title = "Hohoemi Genocide",
                AfterTitle = " (credited as Alex3)"
            },
        };

        var actual = VNDBStaffNotesParser.Program.Parse(input);
        Assert.AreEqual(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
    }

    [Test]
    public void Test_MultipleWithDifferentSongTypesSpaceDelimiter()
    {
        string input = "ED1 \"Saya no Uta\" ED2 \"Garasu no Kutsu\"";

        var expected = new List<Song>
        {
            new()
            {
                BeforeType = "",
                Type = new List<SongType> { SongType.ED },
                Title = "Saya no Uta",
                AfterTitle = ""
            },
            new()
            {
                BeforeType = "",
                Type = new List<SongType> { SongType.ED },
                Title = "Garasu no Kutsu",
                AfterTitle = ""
            },
        };

        var actual = VNDBStaffNotesParser.Program.Parse(input);
        Assert.AreEqual(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
    }

    [Test]
    public void Test_MultipleWithDifferentSongTypesCommaDelimiter()
    {
        string input = "OP \"Todokanai Koi\", Insert Song \"After All ~Tsuzuru Omoi~\"";

        var expected = new List<Song>
        {
            new()
            {
                BeforeType = "",
                Type = new List<SongType> { SongType.OP },
                Title = "Todokanai Koi",
                AfterTitle = ""
            },
            new()
            {
                BeforeType = "",
                Type = new List<SongType> { SongType.Insert },
                Title = "After All ~Tsuzuru Omoi~",
                AfterTitle = ""
            },
        };

        var actual = VNDBStaffNotesParser.Program.Parse(input);
        Assert.AreEqual(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
    }

    [Test]
    public void Test_MultipleWithDifferentSongTypesCommaAndAndDelimiters()
    {
        string input =
            "OP \"Momiji\", Insert song \"Fuji no Tobari to Yoru no Uta\" and ED \"Ashita wo Egaku Omoi no Iro\"";

        var expected = new List<Song>
        {
            new() { BeforeType = "", Type = new List<SongType> { SongType.OP }, Title = "Momiji", AfterTitle = "" },
            new()
            {
                BeforeType = "",
                Type = new List<SongType> { SongType.Insert },
                Title = "Fuji no Tobari to Yoru no Uta",
                AfterTitle = ""
            },
            new()
            {
                BeforeType = "",
                Type = new List<SongType> { SongType.ED },
                Title = "Ashita wo Egaku Omoi no Iro",
                AfterTitle = ""
            },
        };

        var actual = VNDBStaffNotesParser.Program.Parse(input);
        Assert.AreEqual(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
    }

    [Test]
    public void Test_MultipleWithSameSongTypesAndDelimiter()
    {
        string input =
            "PC OP \"HOLY WORLD\" and PS2 insert songs \"Kishin Houkou! Demonbane!\" and \"Evil Shine\"";

        var expected = new List<Song>
        {
            new()
            {
                BeforeType = "PC ",
                Type = new List<SongType> { SongType.OP },
                Title = "HOLY WORLD",
                AfterTitle = ""
            },
            new()
            {
                BeforeType = "PS2 ",
                Type = new List<SongType> { SongType.Insert },
                Title = "Kishin Houkou! Demonbane!",
                AfterTitle = ""
            },
            new()
            {
                BeforeType = "",
                Type = new List<SongType> { SongType.Insert },
                Title = "Evil Shine",
                AfterTitle = ""
            },
        };

        var actual = VNDBStaffNotesParser.Program.Parse(input);
        Assert.AreEqual(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
    }

    [Test]
    public void Test_MultipleWithSameSongTypesCommaDelimiter()
    {
        string input =
            "Insert Songs \"Shin'ai\", \"WHITE ALBUM\", \"SOUND OF DESTINY\", \"Todokanai Koi -live at campus Fes-\"";

        var expected = new List<Song>
        {
            new()
            {
                BeforeType = "", Type = new List<SongType> { SongType.Insert }, Title = "Shin'ai", AfterTitle = ""
            },
            new()
            {
                BeforeType = "",
                Type = new List<SongType> { SongType.Insert },
                Title = "WHITE ALBUM",
                AfterTitle = ""
            },
            new()
            {
                BeforeType = "",
                Type = new List<SongType> { SongType.Insert },
                Title = "SOUND OF DESTINY",
                AfterTitle = ""
            },
            new()
            {
                BeforeType = "",
                Type = new List<SongType> { SongType.Insert },
                Title = "Todokanai Koi -live at campus Fes-",
                AfterTitle = ""
            },
        };

        var actual = VNDBStaffNotesParser.Program.Parse(input);
        Assert.AreEqual(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
    }

    [Test]
    public void Test_SingleWithMultipleSongTypesAmpersandDelimiter()
    {
        string input = "OP & ED \"Present\"";

        var expected = new List<Song>
        {
            new()
            {
                BeforeType = "",
                Type = new List<SongType> { SongType.OP, SongType.ED },
                Title = "Present",
                AfterTitle = ""
            },
        };

        var actual = VNDBStaffNotesParser.Program.Parse(input);
        Assert.AreEqual(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
    }

    [Test]
    public void Test_MultipleWithMultipleSongTypesAmpersandDelimiter()
    {
        string input = "Shizuru route ED \"Koibumi\", Akane route ED & Lucia route Insert song \"Itsuwaranai Kimi e\"";

        var expected = new List<Song>
        {
            new()
            {
                BeforeType = "Shizuru route ",
                Type = new List<SongType> { SongType.ED },
                Title = "Koibumi",
                AfterTitle = ""
            },
            new()
            {
                // todo? CAVEAT: Only the last BeforeType is kept
                BeforeType = "Lucia route ",
                Type = new List<SongType> { SongType.ED, SongType.Insert },
                Title = "Itsuwaranai Kimi e",
                AfterTitle = ""
            },
        };

        var actual = VNDBStaffNotesParser.Program.Parse(input);
        Assert.AreEqual(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
    }

    [Test]
    public void Test_ComplexShit1()
    {
        string input =
            "PC OP \"Muv-Luv\", Extra ED \"I will.\", Unlimited ED \"Harukanaru Chikyuu (Furusato) no Uta\", All-Ages ver. OP \"divergence\", Xbox 360 OP \"first pain\", PS3 OP \"LOVE STEP\"";

        var expected = new List<Song>
        {
            new() { BeforeType = "PC ", Type = new List<SongType> { SongType.OP }, Title = "Muv-Luv", AfterTitle = "" },
            new()
            {
                BeforeType = "Extra ", Type = new List<SongType> { SongType.ED }, Title = "I will.", AfterTitle = ""
            },
            new()
            {
                BeforeType = "Unlimited ",
                Type = new List<SongType> { SongType.ED },
                Title = "Harukanaru Chikyuu (Furusato) no Uta",
                AfterTitle = ""
            },
            new()
            {
                BeforeType = "All-Ages ver. ",
                Type = new List<SongType> { SongType.OP },
                Title = "divergence",
                AfterTitle = ""
            },
            new()
            {
                BeforeType = "Xbox 360 ",
                Type = new List<SongType> { SongType.OP },
                Title = "first pain",
                AfterTitle = ""
            },
            new()
            {
                BeforeType = "PS3 ", Type = new List<SongType> { SongType.OP }, Title = "LOVE STEP", AfterTitle = ""
            },
        };

        var actual = VNDBStaffNotesParser.Program.Parse(input);
        Assert.AreEqual(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
    }

    [Test]
    public void Test_ComplexShit2WithNoSpaceAfterComma()
    {
        string input =
            "ED \"To aru Ryuu no Koi no Uta\", Insert songs \"No Answer\",\"Are You Happy?\",\"To Aru Ryuu no Kami no Shi\" (Chorus)";

        var expected = new List<Song>
        {
            new()
            {
                BeforeType = "",
                Type = new List<SongType> { SongType.ED },
                Title = "To aru Ryuu no Koi no Uta",
                AfterTitle = ""
            },
            new()
            {
                BeforeType = "", Type = new List<SongType> { SongType.Insert }, Title = "No Answer", AfterTitle = ""
            },
            new()
            {
                BeforeType = "",
                Type = new List<SongType> { SongType.Insert },
                Title = "Are You Happy?",
                AfterTitle = ""
            },
            new()
            {
                BeforeType = "",
                Type = new List<SongType> { SongType.Insert },
                Title = "To Aru Ryuu no Kami no Shi",
                AfterTitle = " (Chorus)"
            },
        };

        var actual = VNDBStaffNotesParser.Program.Parse(input);
        Assert.AreEqual(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
    }

    [Test]
    public void Test_ComplexShit3()
    {
        string input =
            "Acta est Fabula OP \"Gregorio\" & ED \"Über den Himmel\", Amantes amentes OP \"Jubilus\" & ED \"Sanctus\", Switch version OP \"Einsatz -zugabe-\"";

        var expected = new List<Song>
        {
            new()
            {
                BeforeType = "Acta est Fabula ",
                Type = new List<SongType> { SongType.OP },
                Title = "Gregorio",
                AfterTitle = ""
            },
            new()
            {
                BeforeType = "",
                Type = new List<SongType> { SongType.ED },
                Title = "Über den Himmel",
                AfterTitle = ""
            },
            new()
            {
                BeforeType = "Amantes amentes ",
                Type = new List<SongType> { SongType.OP },
                Title = "Jubilus",
                AfterTitle = ""
            },
            new() { BeforeType = "", Type = new List<SongType> { SongType.ED }, Title = "Sanctus", AfterTitle = "" },
            new()
            {
                BeforeType = "Switch version ",
                Type = new List<SongType> { SongType.OP },
                Title = "Einsatz -zugabe-",
                AfterTitle = ""
            },
        };

        var actual = VNDBStaffNotesParser.Program.Parse(input);
        Assert.AreEqual(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
    }
}
