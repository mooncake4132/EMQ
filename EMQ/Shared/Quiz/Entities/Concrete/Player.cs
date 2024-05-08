﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace EMQ.Shared.Quiz.Entities.Concrete;

public class Player
{
    public Player(int id, string username, Avatar avatar)
    {
        Id = id;
        Username = username;
        Avatar = avatar;
    }

    public int Id { get; }

    public string Username { get; }

    // public string DisplayName { get; }

    [JsonIgnore]
    public PlayerGuess? Guess { get; set; }

    // todo: do we want last guess or first guess here?
    public int FirstGuessMs { get; set; }

    public int Score { get; set; }

    public Avatar Avatar { get; set; }

    public PlayerStatus PlayerStatus { get; set; }

    public int TeamId { get; set; } = 1;

    public int Lives { get; set; }

    public bool IsBuffered { get; set; }

    public PlayerLootingInfo LootingInfo { get; set; } = new();

    public bool IsSkipping { get; set; }

    public PlayerPreferences Preferences { get; set; } = new();

    // do not rename to IsReady
    public bool IsReadiedUp { get; set; }

    public DateTime LastHeartbeatTimestamp { get; set; }

    public bool HasActiveConnection => (DateTime.UtcNow - LastHeartbeatTimestamp) < TimeSpan.FromSeconds(30);

    public int NGMCGuessesInitial { get; set; }

    public float NGMCGuessesCurrent { get; set; }

    public bool NGMCCanBurn { get; set; }

    public bool NGMCCanBePicked { get; set; }

    public bool NGMCMustPick { get; set; }

    public bool NGMCMustBurn { get; set; }

    public Dictionary<GuessKind, bool?>? IsGuessKindCorrectDict { get; set; }
}

public class PlayerGuess
{
    public string? Mst { get; set; }

    public string? A { get; set; }

    public string? Mt { get; set; }

    public override string ToString()
    {
        string ret = Mst ?? "";

        if (A is not null)
        {
            ret += $" A: {A}";
        }

        if (Mt is not null)
        {
            ret += $" S: {Mt}";
        }

        return ret;
    }
}

public record PlayerLootingInfo
{
    public int X { get; set; }

    public int Y { get; set; }

    public Point TreasureRoomCoords { get; set; } = new();

    public List<Treasure> Inventory { get; set; } = new();
}

public class PlayerPreferences
{
    [Required]
    public bool WantsVideo { get; set; } = true;

    [Required]
    public SongLinkType LinkHost { get; set; } = SongLinkType.Self;

    [Required]
    public int VolumeMaster { get; set; } = 70;

    // todo make these skip preferences not cause unskip
    [Required]
    public bool AutoSkipGuessPhase { get; set; } = false;

    [Required]
    public bool AutoSkipResultsPhase { get; set; } = false;

    [Required]
    public bool RestartSongsOnResultsPhase { get; set; } = false;

    [Required]
    public bool HideVideo { get; set; } = false;

    [Required]
    public bool WantsEnglish { get; set; } = false;

    [Required]
    public bool ShowVndbCovers { get; set; } = true;

    [Required]
    public bool ShowSpacedRepetitionInfo { get; set; } = true;

    [Required]
    public bool HideSpoilers { get; set; } = true;

    [Required]
    public bool SwapArtistNameAndSongTitleDropdowns { get; set; } = false;

    [Required]
    public bool ForceDefaultAvatar { get; set; } = false;
}
