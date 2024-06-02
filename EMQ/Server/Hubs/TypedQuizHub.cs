using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using EMQ.Shared.Quiz.Entities.Concrete;
using Microsoft.AspNetCore.SignalR.Protocol;

namespace EMQ.Server.Hubs;

public static class TypedQuizHub
{
    private static void EnqueuePumpMessage(int playerId, string target, object?[] arguments)
    {
        const int maxQueueSizePerPlayer = 200;

        if (!ServerState.PumpMessages.TryGetValue(playerId, out var queue))
        {
            // todo initialize this immediately after a session is created by sending a simple message
            while (!ServerState.PumpMessages.ContainsKey(playerId))
            {
                queue = new ConcurrentQueue<InvocationMessage>();
                ServerState.PumpMessages.TryAdd(playerId, queue);
            }
        }

        // todo clear stale messages
        string? invocationId = null; // todo?
        queue!.Enqueue(new InvocationMessage(invocationId, target, arguments));

        if (queue.Count > maxQueueSizePerPlayer)
        {
            Console.WriteLine($"PumpMessages queue count for p{playerId}: {queue.Count}; clearing");
            queue.Clear();
        }
    }

    // ================================ QuizManager =================================
    public static void ReceiveUpdateRoom(IEnumerable<int> playerIds, Room room, bool forcePhaseChange)
    {
        foreach (int playerId in playerIds)
        {
            EnqueuePumpMessage(playerId, "ReceiveUpdateRoom", new object[] { room, forcePhaseChange, DateTime.UtcNow });
        }
    }

    public static void ReceivePlayerGuesses(IEnumerable<int> playerIds, Dictionary<int, PlayerGuess?> dict)
    {
        foreach (int playerId in playerIds)
        {
            EnqueuePumpMessage(playerId, "ReceivePlayerGuesses", new object[] { dict });
        }
    }

    public static void ReceiveQuizStarted(IEnumerable<int> playerIds)
    {
        foreach (int playerId in playerIds)
        {
            EnqueuePumpMessage(playerId, "ReceiveQuizStarted", Array.Empty<object>());
        }
    }

    public static void ReceiveUpdatePlayerLootingInfo(IEnumerable<int> playerIds, int id,
        PlayerLootingInfo playerLootingInfo, bool shouldUpdatePosition)
    {
        foreach (int playerId in playerIds)
        {
            EnqueuePumpMessage(playerId, "ReceiveUpdatePlayerLootingInfo",
                new object[] { id, playerLootingInfo, shouldUpdatePosition });
        }
    }

    public static void ReceiveUpdateRemainingMs(IEnumerable<int> playerIds, float remainingMs)
    {
        foreach (int playerId in playerIds)
        {
            EnqueuePumpMessage(playerId, "ReceiveUpdateRemainingMs", new object[] { remainingMs });
        }
    }

    public static void ReceiveUpdateTreasureRoom(IEnumerable<int> playerIds, TreasureRoom treasureRoom)
    {
        foreach (int playerId in playerIds)
        {
            EnqueuePumpMessage(playerId, "ReceiveUpdateTreasureRoom", new object[] { treasureRoom });
        }
    }

    public static void ReceivePyramidEntered(IEnumerable<int> playerIds)
    {
        foreach (int playerId in playerIds)
        {
            EnqueuePumpMessage(playerId, "ReceivePyramidEntered", Array.Empty<object>());
        }
    }

    public static void ReceiveQuizCanceled(IEnumerable<int> playerIds)
    {
        foreach (int playerId in playerIds)
        {
            EnqueuePumpMessage(playerId, "ReceiveQuizCanceled", Array.Empty<object>());
        }
    }

    public static void ReceiveQuizEnded(IEnumerable<int> playerIds)
    {
        foreach (int playerId in playerIds)
        {
            EnqueuePumpMessage(playerId, "ReceiveQuizEnded", Array.Empty<object>());
        }
    }

    public static void ReceiveQuizEntered(IEnumerable<int> playerIds)
    {
        foreach (int playerId in playerIds)
        {
            EnqueuePumpMessage(playerId, "ReceiveQuizEntered", Array.Empty<object>());
        }
    }

    public static void ReceiveCorrectAnswer(IEnumerable<int> playerIds, Song song,
        Dictionary<int, List<Label>> playerLabels, Dictionary<int, PlayerGuess?> playerGuesses)
    {
        foreach (int playerId in playerIds)
        {
            EnqueuePumpMessage(playerId, "ReceiveCorrectAnswer", new object[] { song, playerLabels, playerGuesses });
        }
    }

    // ================================ Other =================================
    public static void ReceivePlayerJoinedRoom(IEnumerable<int> playerIds)
    {
        foreach (int playerId in playerIds)
        {
            EnqueuePumpMessage(playerId, "ReceivePlayerJoinedRoom", Array.Empty<object>());
        }
    }

    // todo figure out a way to merge this with ReceiveUpdateRoom (requires client-side changes)
    public static void ReceiveUpdateRoomForRoom(IEnumerable<int> playerIds, Room room)
    {
        foreach (int playerId in playerIds)
        {
            EnqueuePumpMessage(playerId, "ReceiveUpdateRoomForRoom", new object[] { room });
        }
    }

    public static void ReceiveKickedFromRoom(IEnumerable<int> playerIds)
    {
        foreach (int playerId in playerIds)
        {
            EnqueuePumpMessage(playerId, "ReceiveKickedFromRoom", Array.Empty<object>());
        }
    }
}
