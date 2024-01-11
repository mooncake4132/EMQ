﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace EMQ.Shared.Quiz.Entities.Concrete;

// Anything that's in this class that's not JsonIgnore'd will be visible to ALL players in a room,
// so be careful not to leak player-specific information.
public sealed class Room : IDisposable
{
    public Room(Guid id, string name, Player owner)
    {
        Id = id;
        Name = name;
        Owner = owner;
        CreatedAt = DateTime.UtcNow;
    }

    private readonly object _lock = new();

    public Guid Id { get; }

    public string Name { get; set; }

    [JsonIgnore]
    public string Password { get; set; } = "";

    public QuizSettings QuizSettings { get; set; } = new();

    public Quiz? Quiz { get; set; }

    // TODO: would be better if this was a ConcurrentDictionary
    public ConcurrentQueue<Player> Players { get; set; } = new();

    // TODO: would be better if this was a ConcurrentDictionary
    public ConcurrentQueue<Player> Spectators { get; set; } = new();

    public ConcurrentQueue<Player> HotjoinQueue { get; set; } = new();

    public Player Owner { get; set; }

    public DateTime CreatedAt { get; }

    [JsonIgnore]
    public ConcurrentDictionary<int, string> AllConnectionIds { get; set; } = new();

    public TreasureRoom[][] TreasureRooms { get; set; } = Array.Empty<TreasureRoom[]>();

    public ConcurrentQueue<ChatMessage> Chat { get; set; } = new();

    [JsonIgnore]
    public ConcurrentQueue<RoomLog> RoomLog { get; set; } = new();

    public bool CanJoinDirectly => Quiz == null || Quiz.QuizState.QuizStatus != QuizStatus.Playing;

    [JsonIgnore]
    public Dictionary<int, string> PlayerGuesses => Players.ToDictionary(x => x.Id, x => x.Guess);

    public void Dispose()
    {
        Quiz?.Dispose();
    }

    public void Log(string message, int playerId = -1, bool writeToChat = false)
    {
        var roomLog = new RoomLog(Id, Quiz?.Id ?? Guid.Empty, QuizSettings, Quiz?.QuizState ?? null, playerId, message);
        RoomLog.Enqueue(roomLog);

        Console.WriteLine(roomLog.ToString());

        if (writeToChat)
        {
            Chat.Enqueue(new ChatMessage(message));
        }
    }

    public void RemovePlayer(Player toRemove)
    {
        lock (_lock)
        {
            int oldPlayersCount = Players.Count;
            Players = new ConcurrentQueue<Player>(Players.Where(x => x != toRemove));
            int newPlayersCount = Players.Count;

            if (oldPlayersCount <= newPlayersCount)
            {
                throw new Exception();
            }
        }
    }

    public void RemoveSpectator(Player toRemove)
    {
        lock (_lock)
        {
            int oldSpectatorsCount = Spectators.Count;
            Spectators = new ConcurrentQueue<Player>(Spectators.Where(x => x != toRemove));
            int newSpectatorsCount = Spectators.Count;

            if (oldSpectatorsCount <= newSpectatorsCount)
            {
                throw new Exception();
            }

            // toRemove may or may not be here
            HotjoinQueue = new ConcurrentQueue<Player>(HotjoinQueue.Where(x => x != toRemove));
        }
    }

    // TODO: AddPlayer etc.
}
