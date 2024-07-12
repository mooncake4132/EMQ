using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using EMQ.Client;
using EMQ.Server.Db;
using EMQ.Server.Db.Entities;
using EMQ.Shared.Quiz.Entities.Concrete;
using EMQ.Server.Hubs;
using EMQ.Shared.Auth.Entities.Concrete;
using EMQ.Shared.Core;
using EMQ.Shared.Core.SharedDbEntities;
using EMQ.Shared.VNDB.Business;
using Microsoft.AspNetCore.SignalR;

namespace EMQ.Server.Business;

public class QuizManager
{
    public QuizManager(Quiz quiz)
    {
        Quiz = quiz;
    }

    public Quiz Quiz { get; }

    private DateTime LastUpdate { get; set; }

    private Dictionary<int, List<string>> CorrectAnswersDictMst { get; set; } = new();

    private Dictionary<int, List<string>> CorrectAnswersDictA { get; set; } = new();

    private Dictionary<int, List<string>> CorrectAnswersDictMt { get; set; } = new();

    private Dictionary<int, List<string>> CorrectAnswersDictRigger { get; set; } = new();

    private Dictionary<int, List<string>> CorrectAnswersDictDeveloper { get; set; } = new();

    private Dictionary<int, string[]>? ArtistAliasesDict { get; set; }

    private async Task SetTimer()
    {
        if (!Quiz.IsDisposed && !Quiz.IsTimerRunning)
        {
            Quiz.Timer?.Dispose();
            Quiz.Timer = new PeriodicTimer(TimeSpan.FromMilliseconds(Quiz.TickRate));
            Quiz.IsTimerRunning = true;
            while (await Quiz.Timer.WaitForNextTickAsync())
            {
                await OnTimedEvent();
            }
        }
    }

    private async Task OnTimedEvent()
    {
        if (!Quiz.IsTimerRunning)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100));
            return;
        }

        if (Quiz.QuizState.QuizStatus == QuizStatus.Playing)
        {
            // var tickStart = DateTime.UtcNow; // todo? this might not be precise enough
            if (Quiz.QuizState.Phase != QuizPhaseKind.Looting && DateTime.UtcNow - LastUpdate > TimeSpan.FromSeconds(1))
            {
                // Console.WriteLine($"sending update at {DateTime.UtcNow}");
                LastUpdate = DateTime.UtcNow;
                TypedQuizHub.ReceiveUpdateRoom(Quiz.Room.Players.Concat(Quiz.Room.Spectators).Select(x => x.Id),
                    Quiz.Room, false);
            }

            if (Quiz.QuizState.RemainingMs >= 0)
            {
                Quiz.QuizState.RemainingMs -= Quiz.TickRate;
            }

            if (Quiz.QuizState.RemainingMs <= 0)
            {
                if (!Quiz.IsDisposed)
                {
                    Quiz.IsTimerRunning = false;
                }

                await WaitForLaggingPlayers();
                switch (Quiz.QuizState.Phase)
                {
                    case QuizPhaseKind.Guess:
                        await EnterJudgementPhase();
                        break;
                    case QuizPhaseKind.Judgement:
                        await EnterResultsPhase();
                        break;
                    case QuizPhaseKind.Results:
                        await EnterGuessingPhase();
                        break;
                    case QuizPhaseKind.Looting:
                        bool lootingSuccess = await SetLootedSongs();
                        await EnterQuiz();

                        if (!lootingSuccess)
                        {
                            Quiz.Room.Log("Canceling quiz due to looting failure", writeToChat: true);
                            await CancelQuiz();
                        }

                        await EnterGuessingPhase();
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                if (!Quiz.IsDisposed)
                {
                    Quiz.IsTimerRunning = true;
                }
            }

            // var tickEnd = DateTime.UtcNow;
            // double tickMs = (tickEnd - tickStart).TotalMilliseconds;
            // if (Quiz.QuizState.QuizStatus == QuizStatus.Playing && Quiz.QuizState.sp > 0 && tickMs > Quiz.TickRate)
            // {
            //     // Console.WriteLine($"Can't keep up! {tickMs} ms");
            // }
        }
    }

    public async Task CancelQuiz()
    {
        Quiz.QuizState.QuizStatus = QuizStatus.Canceled;

        if (!Quiz.IsDisposed)
        {
            Quiz.IsTimerRunning = false;
            Quiz.Timer?.Dispose();
        }

        TypedQuizHub.ReceiveQuizCanceled(Quiz.Room.Players.Concat(Quiz.Room.Spectators).Select(x => x.Id));
    }

    private async Task EnterGuessingPhase()
    {
        while (Quiz.QuizState.IsPaused)
        {
            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        while (Quiz.Room.QuizSettings.GamemodeKind == GamemodeKind.NGMC &&
               Quiz.Room.Players.Any(x => x.NGMCMustPick || x.NGMCMustBurn))
        {
            Quiz.QuizState.ExtraInfo = "Waiting for NGMC decisions...";

            TypedQuizHub.ReceiveUpdateRoom(Quiz.Room.Players.Concat(Quiz.Room.Spectators).Select(x => x.Id), Quiz.Room,
                false);
            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        int isBufferedCount = Quiz.Room.Players.Count(x => x.IsBuffered);
        int timeoutMs = Quiz.Room.QuizSettings.TimeoutMs;
        // Console.WriteLine("ibc " + isBufferedCount);

        int activePlayersCount = ServerState.Sessions.Where(x => Quiz.Room.Players.Any(y => y.Id == x.Player.Id))
            .Count(x => x.Player.HasActiveConnection);
        // Room.Log($"activePlayers: {activePlayers}/{Quiz.Room.Players.Count}");

        float waitNumber = (float)Math.Round(
            activePlayersCount * ((float)Quiz.Room.QuizSettings.WaitPercentage / 100),
            MidpointRounding.AwayFromZero);

        while (isBufferedCount < waitNumber &&
               timeoutMs > 0)
        {
            // Console.WriteLine("in while " + isBufferedCount + "/" + waitNumber);
            await Task.Delay(1000);
            timeoutMs -= 1000;

            isBufferedCount = Quiz.Room.Players.Count(x => x.IsBuffered);

            activePlayersCount = ServerState.Sessions.Where(x => Quiz.Room.Players.Any(y => y.Id == x.Player.Id))
                .Count(x => x.Player.HasActiveConnection);

            waitNumber = (float)Math.Round(
                activePlayersCount * ((float)Quiz.Room.QuizSettings.WaitPercentage / 100),
                MidpointRounding.AwayFromZero);

            Quiz.QuizState.ExtraInfo =
                $"Waiting buffering... {isBufferedCount}/{waitNumber} timeout in {timeoutMs / 1000}s";
            // Console.WriteLine("ei: " + Quiz.QuizState.ExtraInfo);

            TypedQuizHub.ReceiveUpdateRoom(Quiz.Room.Players.Concat(Quiz.Room.Spectators).Select(x => x.Id), Quiz.Room,
                false);
        }

        Quiz.QuizState.Phase = QuizPhaseKind.Guess;
        Quiz.QuizState.RemainingMs = Quiz.Room.QuizSettings.GuessMs;
        Quiz.QuizState.sp += 1;
        Quiz.Songs[Quiz.QuizState.sp].PlayedAt = DateTime.UtcNow;
        Quiz.QuizState.ExtraInfo = "";
        Quiz.QuizState.TeamGuessesHaveBeenDetermined = false;

        foreach (var player in Quiz.Room.Players)
        {
            player.Guess = null;
            player.IsGuessKindCorrectDict = null;
            player.FirstGuessMs = 0;
            player.PlayerStatus = PlayerStatus.Thinking;
            player.IsBuffered = false;
            player.IsSkipping = false;
            player.IsReadiedUp = false;
        }

        // reset the guesses
        TypedQuizHub.ReceivePlayerGuesses(Quiz.Room.Players.Concat(Quiz.Room.Spectators).Select(x => x.Id),
            Quiz.Room.PlayerGuesses);

        TypedQuizHub.ReceiveUpdateRoom(Quiz.Room.Players.Concat(Quiz.Room.Spectators).Select(x => x.Id), Quiz.Room,
            true);
    }

    private async Task EnterJudgementPhase()
    {
        Quiz.QuizState.Phase = QuizPhaseKind.Judgement;
        Quiz.QuizState.ExtraInfo = "";

        TypedQuizHub.ReceiveUpdateRoom(Quiz.Room.Players.Concat(Quiz.Room.Spectators).Select(x => x.Id), Quiz.Room,
            true);

        if (Quiz.Room.QuizSettings.IsSharedGuessesTeams)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500)); // wait for late guesses (especially non-Entered guesses)
            DetermineTeamGuesses();
        }

        // need to do this AFTER the team guesses have been determined
        TypedQuizHub.ReceivePlayerGuesses(Quiz.Room.Players.Concat(Quiz.Room.Spectators).Select(x => x.Id),
            Quiz.Room.PlayerGuesses);

        await JudgeGuesses();
    }

    private void DetermineTeamGuesses()
    {
        Quiz.QuizState.TeamGuessesHaveBeenDetermined = true;
        // todo this is inefficient, do it in a batched manner
        HashSet<int> processedTeamIdsMst = new();
        HashSet<int> processedTeamIdsA = new();
        HashSet<int> processedTeamIdsMt = new();
        HashSet<int> processedTeamIdsRigger = new();
        HashSet<int> processedTeamIdsDeveloper = new();
        foreach (Player player in Quiz.Room.Players)
        {
            if (player.Guess is null)
            {
                continue;
            }

            if (!processedTeamIdsMst.Contains(player.TeamId) && IsGuessCorrectMst(player.Guess.Mst))
            {
                processedTeamIdsMst.Add(player.TeamId);
                foreach (Player possibleTeammate in Quiz.Room.Players)
                {
                    possibleTeammate.Guess ??= new PlayerGuess();
                    if (possibleTeammate.TeamId == player.TeamId)
                    {
                        // Console.WriteLine($"setting {possibleTeammate.Username}'s guess.Mst ({possibleTeammate.Guess.Mst} to {player.Username}'s guess.Mst ({player.Guess.Mst})");
                        possibleTeammate.Guess.Mst = player.Guess.Mst;
                    }
                }
            }

            if (!processedTeamIdsA.Contains(player.TeamId) && IsGuessCorrectA(player.Guess.A))
            {
                processedTeamIdsA.Add(player.TeamId);
                foreach (Player possibleTeammate in Quiz.Room.Players)
                {
                    possibleTeammate.Guess ??= new PlayerGuess();
                    if (possibleTeammate.TeamId == player.TeamId)
                    {
                        // Console.WriteLine($"setting {possibleTeammate.Username}'s guess.A ({possibleTeammate.Guess.A} to {player.Username}'s guess.A ({player.Guess.A})");
                        possibleTeammate.Guess.A = player.Guess.A;
                    }
                }
            }

            if (!processedTeamIdsMt.Contains(player.TeamId) && IsGuessCorrectMt(player.Guess.Mt))
            {
                processedTeamIdsMt.Add(player.TeamId);
                foreach (Player possibleTeammate in Quiz.Room.Players)
                {
                    possibleTeammate.Guess ??= new PlayerGuess();
                    if (possibleTeammate.TeamId == player.TeamId)
                    {
                        // Console.WriteLine($"setting {possibleTeammate.Username}'s guess.Mt ({possibleTeammate.Guess.Mt} to {player.Username}'s guess.Mt ({player.Guess.Mt})");
                        possibleTeammate.Guess.Mt = player.Guess.Mt;
                    }
                }
            }

            if (!processedTeamIdsRigger.Contains(player.TeamId) && IsGuessCorrectRigger(player.Guess.Rigger))
            {
                processedTeamIdsRigger.Add(player.TeamId);
                foreach (Player possibleTeammate in Quiz.Room.Players)
                {
                    possibleTeammate.Guess ??= new PlayerGuess();
                    if (possibleTeammate.TeamId == player.TeamId)
                    {
                        possibleTeammate.Guess.Rigger = player.Guess.Rigger;
                    }
                }
            }

            if (!processedTeamIdsDeveloper.Contains(player.TeamId) && IsGuessCorrectDeveloper(player.Guess.Developer))
            {
                processedTeamIdsDeveloper.Add(player.TeamId);
                foreach (Player possibleTeammate in Quiz.Room.Players)
                {
                    possibleTeammate.Guess ??= new PlayerGuess();
                    if (possibleTeammate.TeamId == player.TeamId)
                    {
                        possibleTeammate.Guess.Developer = player.Guess.Developer;
                    }
                }
            }
        }

        TypedQuizHub.ReceiveUpdateRoom(Quiz.Room.Players.Concat(Quiz.Room.Spectators).Select(x => x.Id), Quiz.Room,
            false);
    }

    private bool IsGuessCorrectMst(string? guess)
    {
        bool correct = false;
        if (!string.IsNullOrWhiteSpace(guess))
        {
            if (!CorrectAnswersDictMst.TryGetValue(Quiz.QuizState.sp, out var correctAnswers))
            {
                correctAnswers = Quiz.Songs[Quiz.QuizState.sp].Sources.SelectMany(x => x.Titles)
                    .Select(x => x.LatinTitle).ToList();
                correctAnswers.AddRange(Quiz.Songs[Quiz.QuizState.sp].Sources.SelectMany(x => x.Titles)
                    .Select(x => x.NonLatinTitle).Where(x => x != null)!);
                correctAnswers = correctAnswers.Select(x => x.Replace(" ", "")).Distinct().ToList();

                CorrectAnswersDictMst.Add(Quiz.QuizState.sp, correctAnswers);
                Quiz.Room.Log("cA: " + JsonSerializer.Serialize(correctAnswers, Utils.Jso));
            }

            foreach (string correctAnswer in correctAnswers)
            {
                if (string.Equals(guess.Replace(" ", ""), correctAnswer, StringComparison.OrdinalIgnoreCase))
                {
                    correct = true;
                    break;
                }
            }
        }

        return correct;
    }

    private bool IsGuessCorrectA(string? guess)
    {
        bool correct = false;
        if (!string.IsNullOrWhiteSpace(guess))
        {
            if (!CorrectAnswersDictA.TryGetValue(Quiz.QuizState.sp, out var correctAnswers))
            {
                if (ArtistAliasesDict != null)
                {
                    correctAnswers = new List<string>();
                    var aIds = Quiz.Songs[Quiz.QuizState.sp].Artists.Select(x => x.Id);
                    foreach (int aId in aIds)
                    {
                        correctAnswers.AddRange(ArtistAliasesDict[aId].Select(x => x.NormalizeForAutocomplete()));
                    }
                }
                else
                {
                    correctAnswers = Quiz.Songs[Quiz.QuizState.sp].Artists.SelectMany(x => x.Titles)
                        .Select(x => x.LatinTitle.NormalizeForAutocomplete()).ToList();
                    correctAnswers.AddRange(Quiz.Songs[Quiz.QuizState.sp].Artists.SelectMany(x => x.Titles)
                        .Select(x => x.NonLatinTitle?.NormalizeForAutocomplete()).Where(x => x != null)!);
                }

                correctAnswers = correctAnswers.Distinct().ToList();
                CorrectAnswersDictA.Add(Quiz.QuizState.sp, correctAnswers);
                Quiz.Room.Log("cA-a: " + JsonSerializer.Serialize(correctAnswers, Utils.Jso));
            }

            foreach (string correctAnswer in correctAnswers)
            {
                if (guess.NormalizeForAutocomplete() == correctAnswer)
                {
                    correct = true;
                    break;
                }
            }
        }

        return correct;
    }

    private bool IsGuessCorrectMt(string? guess)
    {
        bool correct = false;
        if (!string.IsNullOrWhiteSpace(guess))
        {
            if (!CorrectAnswersDictMt.TryGetValue(Quiz.QuizState.sp, out var correctAnswers))
            {
                correctAnswers = Quiz.Songs[Quiz.QuizState.sp].Titles
                    .Select(x => x.LatinTitle).ToList();
                correctAnswers.AddRange(Quiz.Songs[Quiz.QuizState.sp].Titles
                    .Select(x => x.NonLatinTitle).Where(x => x != null)!);
                correctAnswers = correctAnswers.Distinct().ToList();

                CorrectAnswersDictMt.Add(Quiz.QuizState.sp, correctAnswers);
                Quiz.Room.Log("cA-mt: " + JsonSerializer.Serialize(correctAnswers, Utils.Jso));
            }

            foreach (string correctAnswer in correctAnswers)
            {
                if (string.Equals(guess, correctAnswer, StringComparison.OrdinalIgnoreCase))
                {
                    correct = true;
                    break;
                }
            }
        }

        return correct;
    }

    private bool IsGuessCorrectRigger(string? guess)
    {
        bool correct = false;
        if (!string.IsNullOrWhiteSpace(guess))
        {
            if (!CorrectAnswersDictRigger.TryGetValue(Quiz.QuizState.sp, out var correctAnswers))
            {
                var riggerIds = Quiz.Songs[Quiz.QuizState.sp].PlayerLabels
                    .Where(x => x.Value.Any(y => y.Kind == LabelKind.Include)).Select(z => z.Key);
                correctAnswers = Quiz.Room.Players.Where(x => riggerIds.Contains(x.Id)).Select(y => y.Username)
                    .ToList();
                // correctAnswers = correctAnswers.Distinct().ToList();

                CorrectAnswersDictRigger.Add(Quiz.QuizState.sp, correctAnswers);
                Quiz.Room.Log("cA-rigger: " + JsonSerializer.Serialize(correctAnswers, Utils.Jso));
            }

            foreach (string correctAnswer in correctAnswers)
            {
                if (string.Equals(guess, correctAnswer, StringComparison.OrdinalIgnoreCase))
                {
                    correct = true;
                    break;
                }
            }
        }

        return correct;
    }

    private bool IsGuessCorrectDeveloper(string? guess)
    {
        bool correct = false;
        if (!string.IsNullOrWhiteSpace(guess))
        {
            if (!CorrectAnswersDictDeveloper.TryGetValue(Quiz.QuizState.sp, out var correctAnswers))
            {
                correctAnswers = new List<string>();
                foreach (string vndbId in Quiz.Songs[Quiz.QuizState.sp].Sources.SelectMany(x =>
                             x.Links.Where(y => y.Type == SongSourceLinkType.VNDB).Select(z => z.Url.ToVndbId())))
                {
                    if (DbManager.VnDevelopers.TryGetValue(vndbId, out var developers))
                    {
                        correctAnswers.AddRange(developers.Select(x => x.latin)
                            .Concat(developers.Select(x => x.name)).Where(x => x != null)!);
                    }
                }

                CorrectAnswersDictDeveloper.Add(Quiz.QuizState.sp, correctAnswers);
                Quiz.Room.Log("cA-developer: " + JsonSerializer.Serialize(correctAnswers, Utils.Jso));
            }

            foreach (string correctAnswer in correctAnswers)
            {
                if (string.Equals(guess, correctAnswer, StringComparison.OrdinalIgnoreCase))
                {
                    correct = true;
                    break;
                }
            }
        }

        return correct;
    }

    private async Task WaitForLaggingPlayers()
    {
        await Utils.WaitWhile(() =>
        {
            bool needToWait = false;
            var playerIds = Quiz.Room.Players.Where(x => x.HasActiveConnection).Select(x => x.Id);
            foreach (int playerId in playerIds)
            {
                if (ServerState.PumpMessages.TryGetValue(playerId, out var queue))
                {
                    // Console.WriteLine(queue.Count);
                    if (queue.SendingTasks.Count > 1)
                    {
                        // Console.WriteLine($"needToWait for p{playerId}");
                        Quiz.QuizState.ExtraInfo = "Waiting for lagging players...";
                        needToWait = true;
                    }
                }
            }

            return Task.FromResult(needToWait);
        }, 160, Quiz.Room.QuizSettings.MaxWaitForLaggingPlayersMs);

        Quiz.QuizState.ExtraInfo = "";
    }

    private async Task EnterResultsPhase()
    {
        TypedQuizHub.ReceiveCorrectAnswer(Quiz.Room.Players.Concat(Quiz.Room.Spectators).Select(x => x.Id),
            Quiz.Songs[Quiz.QuizState.sp],
            Quiz.Songs[Quiz.QuizState.sp].PlayerLabels,
            Quiz.Room.PlayerGuesses);

        // wait a little for these messages to reach players, otherwise it looks really janky
        await Task.Delay(TimeSpan.FromMilliseconds(160));

        // send answers again to make sure everyone receives late answers
        TypedQuizHub.ReceivePlayerGuesses(Quiz.Room.Players.Concat(Quiz.Room.Spectators).Select(x => x.Id),
            Quiz.Room.PlayerGuesses);

        // wait a little for these messages to reach players, otherwise it looks really janky
        await Task.Delay(TimeSpan.FromMilliseconds(160));

        Quiz.QuizState.Phase = QuizPhaseKind.Results;
        Quiz.QuizState.RemainingMs = Quiz.Room.QuizSettings.ResultsMs;

        foreach (var player in Quiz.Room.Players)
        {
            player.IsBuffered = false;
        }

        TypedQuizHub.ReceiveUpdateRoom(Quiz.Room.Players.Concat(Quiz.Room.Spectators).Select(x => x.Id), Quiz.Room,
            true);

        if (Quiz.QuizState.sp + 1 == Quiz.Songs.Count)
        {
            await EndQuiz();
        }
        else if (Quiz.Room.QuizSettings.MaxLives > 0)
        {
            var teams = Quiz.Room.Players.GroupBy(x => x.TeamId).ToArray();
            bool isOneTeamGame = teams.Length == 1;
            if (isOneTeamGame)
            {
                if (teams.Single().First().Lives <= 0)
                {
                    await EndQuiz();
                }
            }
            else
            {
                var teamsWithLives = teams.Where(x => x.Any(y => y.Lives > 0)).ToArray();
                bool onlyOneTeamWithLivesLeft = teamsWithLives.Length == 1;
                if (onlyOneTeamWithLivesLeft)
                {
                    Quiz.Room.Log($"Team {teamsWithLives.Single().First().TeamId} won!", writeToChat: true);
                    await EndQuiz();
                }
                else if (teamsWithLives.Length == 0)
                {
                    await EndQuiz();
                }
            }
        }

        if (Quiz.Room.HotjoinQueue.Any())
        {
            while (Quiz.Room.HotjoinQueue.Any())
            {
                Quiz.Room.HotjoinQueue.TryDequeue(out Player? player);
                if (player != null)
                {
                    player.Lives = Quiz.Room.QuizSettings.MaxLives;
                    player.Score = 0;
                    player.Guess = null;
                    Quiz.Room.Players.Enqueue(player);
                    Quiz.Room.RemoveSpectator(player);
                    Quiz.Room.Log($"{player.Username} hotjoined.", player.Id, true);
                }
            }

            TypedQuizHub.ReceiveUpdateRoom(Quiz.Room.Players.Concat(Quiz.Room.Spectators).Select(x => x.Id), Quiz.Room,
                false);
        }
    }

    private async Task JudgeGuesses()
    {
        // don't make this delay configurable (at least not for regular users)
        await Task.Delay(TimeSpan.FromMilliseconds(900)); // add suspense & wait for late guesses

        var song = Quiz.Songs[Quiz.QuizState.sp];
        var songHistory = new SongHistory { Song = song };
        var userSongStatsDict =
            await DbManager.GetSHPlayerSongStats(song.Id, Quiz.Room.Players.Select(x => x.Id).ToList());

        foreach (var player in Quiz.Room.Players)
        {
            if (player.PlayerStatus == PlayerStatus.Dead)
            {
                continue;
            }

            Quiz.Room.Log("pG: " + player.Guess, player.Id);

            int correctCount = 0;
            player.IsGuessKindCorrectDict = new Dictionary<GuessKind, bool?>();

            // todo? only check enabled types
            bool correctMst = IsGuessCorrectMst(player.Guess?.Mst);
            player.IsGuessKindCorrectDict[GuessKind.Mst] = correctMst;
            if (correctMst)
            {
                correctCount += 1;
            }

            bool correctA = IsGuessCorrectA(player.Guess?.A);
            player.IsGuessKindCorrectDict[GuessKind.A] = correctA;
            if (correctA)
            {
                correctCount += 1;
            }

            bool correctMt = IsGuessCorrectMt(player.Guess?.Mt);
            player.IsGuessKindCorrectDict[GuessKind.Mt] = correctMt;
            if (correctMt)
            {
                correctCount += 1;
            }

            bool correctRigger = IsGuessCorrectRigger(player.Guess?.Rigger);
            player.IsGuessKindCorrectDict[GuessKind.Rigger] = correctRigger;
            if (correctRigger)
            {
                correctCount += 1;
            }

            bool correctDeveloper = IsGuessCorrectDeveloper(player.Guess?.Developer);
            player.IsGuessKindCorrectDict[GuessKind.Developer] = correctDeveloper;
            if (correctDeveloper)
            {
                correctCount += 1;
            }

            if (correctCount > 0)
            {
                player.Score += correctCount;
                player.PlayerStatus = PlayerStatus.Correct;
            }
            else
            {
                player.PlayerStatus = PlayerStatus.Wrong;

                if (Quiz.Room.QuizSettings.MaxLives > 0 && player.Lives >= 0)
                {
                    if (Quiz.Room.QuizSettings.GamemodeKind != GamemodeKind.NGMC)
                    {
                        player.Lives -= 1; // todo? reduce by wrongCount
                    }

                    if (player.Lives <= 0)
                    {
                        player.PlayerStatus = PlayerStatus.Dead;
                    }
                }
            }

            if (player.HasActiveConnection)
            {
                UserSpacedRepetition? previous = null;
                UserSpacedRepetition? current = null;
                try
                {
                    (previous, current) = await DoSpacedRepetition(player.Id, song.Id, correctMst); // todo?
                }
                catch (Exception e)
                {
                    Console.WriteLine("Failed to DoSpacedRepetition");
                    Console.WriteLine(e);
                }

                _ = song.PlayerLabels.TryGetValue(player.Id, out var labels);
                _ = userSongStatsDict.TryGetValue(player.Id, out var userSongStats);
                var guessInfo = new GuessInfo
                {
                    Username = player.Username,
                    Guess = player.Guess?.Mst ?? "", // todo
                    FirstGuessMs = player.FirstGuessMs,
                    IsGuessCorrect = correctMst, // todo
                    Labels = labels,
                    IsOnList = labels?.Any() ?? false,
                    PreviousUserSpacedRepetition = previous,
                    CurrentUserSpacedRepetition = current,
                    PlayerSongStats = userSongStats,
                };
                songHistory.PlayerGuessInfos[player.Id] = guessInfo;
            }
        }

        Quiz.SongsHistory[Quiz.QuizState.sp] = songHistory;

        if (Quiz.Room.QuizSettings.GamemodeKind == GamemodeKind.NGMC)
        {
            var teams = Quiz.Room.Players.GroupBy(x => x.TeamId).ToArray();
            var team1 = teams.ElementAt(0);
            var team2 = teams.ElementAt(1);

            var team1CorrectPlayers = team1
                .Where(x => x.NGMCGuessesCurrent >= 1f && x.PlayerStatus == PlayerStatus.Correct)
                .ToArray();
            var team2CorrectPlayers = team2
                .Where(x => x.NGMCGuessesCurrent >= 1f && x.PlayerStatus == PlayerStatus.Correct)
                .ToArray();

            foreach (Player correctPlayer in team1CorrectPlayers.Concat(team2CorrectPlayers))
            {
                correctPlayer.NGMCCanBePicked = true;
            }

            int team1CorrectPlayersCount = team1CorrectPlayers.Length;
            int team2CorrectPlayersCount = team2CorrectPlayers.Length;
            var team1Captain = team1.First();
            var team2Captain = team2.First();

            if (team1CorrectPlayersCount > 0)
            {
                if (!team2CorrectPlayers.Any())
                {
                    foreach (Player player in team2)
                    {
                        player.Lives -= 1;
                    }
                }

                if (Quiz.Room.QuizSettings.NGMCAutoPickOnlyCorrectPlayerInTeam && team1CorrectPlayersCount == 1)
                {
                    await NGMCPickPlayer(team1CorrectPlayers.Single(), team1Captain, true);
                }
                else
                {
                    team1Captain.NGMCMustPick = true;
                }
            }

            if (team2CorrectPlayersCount > 0)
            {
                if (!team1CorrectPlayers.Any())
                {
                    foreach (Player player in team1)
                    {
                        player.Lives -= 1;
                    }
                }

                if (Quiz.Room.QuizSettings.NGMCAutoPickOnlyCorrectPlayerInTeam && team2CorrectPlayersCount == 1)
                {
                    await NGMCPickPlayer(team2CorrectPlayers.Single(), team2Captain, true);
                }
                else
                {
                    team2Captain.NGMCMustPick = true;
                }
            }

            team1Captain.NGMCCanBurn = Quiz.Room.QuizSettings.NGMCAllowBurning && !team1CorrectPlayers.Any();
            team2Captain.NGMCCanBurn = Quiz.Room.QuizSettings.NGMCAllowBurning && !team2CorrectPlayers.Any();
            team1Captain.NGMCMustBurn = team1Captain.NGMCCanBurn;
            team2Captain.NGMCMustBurn = team2Captain.NGMCCanBurn;

            string team1GuessesStr = string.Join(";", team1.Select(x => x.NGMCGuessesCurrent));
            string team2GuessesStr = string.Join(";", team2.Select(x => x.NGMCGuessesCurrent));
            Quiz.Room.Log($"{team1GuessesStr} | {team2GuessesStr} {team1.First().Lives}-{team2.First().Lives}",
                writeToChat: true);
        }
    }

    public async Task NGMCBurnPlayer(Player burnedPlayer, Player requestingPlayer)
    {
        if (Quiz.QuizState.Phase is QuizPhaseKind.Judgement or QuizPhaseKind.Looting)
        {
            return;
        }

        if (Quiz.QuizState.Phase is QuizPhaseKind.Guess)
        {
            bool firstSec = (Quiz.Room.QuizSettings.GuessMs - Quiz.QuizState.RemainingMs) < 1000;
            if (!firstSec)
            {
                return;
            }
        }

        if (Quiz.QuizState.QuizStatus != QuizStatus.Playing)
        {
            return;
        }

        if (burnedPlayer.TeamId != requestingPlayer.TeamId)
        {
            return;
        }

        var teams = Quiz.Room.Players.GroupBy(x => x.TeamId).ToArray();
        var team1 = teams.ElementAt(0);
        var team2 = teams.ElementAt(1);

        var burnedPlayerTeam = burnedPlayer.TeamId == 1 ? team1 : team2;
        var burnedPlayerTeamFirstPlayer = burnedPlayerTeam.First();
        if (burnedPlayerTeamFirstPlayer.NGMCCanBurn && burnedPlayer.NGMCGuessesCurrent > 0)
        {
            burnedPlayerTeamFirstPlayer.NGMCCanBurn = false;
            burnedPlayerTeamFirstPlayer.NGMCMustBurn = false;
            burnedPlayer.NGMCGuessesCurrent -= 0.5f;
            Quiz.Room.Log($"{requestingPlayer.Username} burned {burnedPlayer.Username}.", writeToChat: true);

            if (burnedPlayerTeam.All(x => x.NGMCGuessesCurrent == 0))
            {
                foreach (Player player in burnedPlayerTeam)
                {
                    player.NGMCGuessesCurrent = player.NGMCGuessesInitial;
                }

                Quiz.Room.Log($"Resetting guesses for team {burnedPlayer.TeamId}.", writeToChat: true);
            }

            string team1GuessesStr = string.Join(";", team1.Select(x => x.NGMCGuessesCurrent));
            string team2GuessesStr = string.Join(";", team2.Select(x => x.NGMCGuessesCurrent));
            Quiz.Room.Log($"{team1GuessesStr} | {team2GuessesStr} {team1.First().Lives}-{team2.First().Lives}",
                writeToChat: true);

            TypedQuizHub.ReceiveUpdateRoom(Quiz.Room.Players.Concat(Quiz.Room.Spectators).Select(x => x.Id), Quiz.Room,
                false);
        }
    }

    public async Task NGMCDontBurn(Player requestingPlayer)
    {
        if (Quiz.QuizState.Phase is QuizPhaseKind.Judgement or QuizPhaseKind.Looting)
        {
            return;
        }

        if (Quiz.QuizState.Phase is QuizPhaseKind.Guess)
        {
            bool firstSec = (Quiz.Room.QuizSettings.GuessMs - Quiz.QuizState.RemainingMs) < 1000;
            if (!firstSec)
            {
                return;
            }
        }

        if (Quiz.QuizState.QuizStatus != QuizStatus.Playing)
        {
            return;
        }

        var teams = Quiz.Room.Players.GroupBy(x => x.TeamId).ToArray();
        var burnedPlayerTeam = teams.ElementAt(requestingPlayer.TeamId - 1);
        var burnedPlayerTeamFirstPlayer = burnedPlayerTeam.First();
        if (burnedPlayerTeamFirstPlayer.NGMCCanBurn)
        {
            burnedPlayerTeamFirstPlayer.NGMCCanBurn = false;
            burnedPlayerTeamFirstPlayer.NGMCMustBurn = false;

            Quiz.Room.Log($"{requestingPlayer.Username} skipped burning.", writeToChat: true);
            TypedQuizHub.ReceiveUpdateRoom(Quiz.Room.Players.Concat(Quiz.Room.Spectators).Select(x => x.Id), Quiz.Room,
                false);
        }
    }

    public async Task NGMCPickPlayer(Player pickedPlayer, Player requestingPlayer, bool isAutoPick)
    {
        if (!isAutoPick && Quiz.QuizState.Phase is QuizPhaseKind.Judgement or QuizPhaseKind.Looting)
        {
            return;
        }

        if (Quiz.QuizState.Phase is QuizPhaseKind.Guess)
        {
            bool firstSec = (Quiz.Room.QuizSettings.GuessMs - Quiz.QuizState.RemainingMs) < 1000;
            if (!firstSec)
            {
                return;
            }
        }

        if (Quiz.QuizState.QuizStatus != QuizStatus.Playing)
        {
            return;
        }

        if (pickedPlayer.TeamId != requestingPlayer.TeamId)
        {
            return;
        }

        var teams = Quiz.Room.Players.GroupBy(x => x.TeamId).ToArray();
        var team1 = teams.ElementAt(0);
        var team2 = teams.ElementAt(1);

        var pickedPlayerTeam = pickedPlayer.TeamId == 1 ? team1 : team2;
        var pickedPlayerTeamFirstPlayer = pickedPlayerTeam.First();
        if (pickedPlayer.NGMCCanBePicked)
        {
            foreach (Player player in pickedPlayerTeam)
            {
                player.NGMCCanBePicked = false;
            }

            pickedPlayerTeamFirstPlayer.NGMCMustPick = false;
            pickedPlayer.NGMCGuessesCurrent -= 1;
            Quiz.Room.Log($"{requestingPlayer.Username} picked {pickedPlayer.Username}.", writeToChat: true);

            if (pickedPlayerTeam.All(x => x.NGMCGuessesCurrent == 0))
            {
                foreach (Player player in pickedPlayerTeam)
                {
                    player.NGMCGuessesCurrent = player.NGMCGuessesInitial;
                }

                Quiz.Room.Log($"Resetting guesses for team {pickedPlayer.TeamId}.", writeToChat: true);
            }

            if (!isAutoPick)
            {
                string team1GuessesStr = string.Join(";", team1.Select(x => x.NGMCGuessesCurrent));
                string team2GuessesStr = string.Join(";", team2.Select(x => x.NGMCGuessesCurrent));
                Quiz.Room.Log($"{team1GuessesStr} | {team2GuessesStr} {team1.First().Lives}-{team2.First().Lives}",
                    writeToChat: true);
            }

            TypedQuizHub.ReceiveUpdateRoom(Quiz.Room.Players.Concat(Quiz.Room.Spectators).Select(x => x.Id), Quiz.Room,
                false);
        }
    }

    public async Task EndQuiz()
    {
        if (Quiz.QuizState.QuizStatus == QuizStatus.Ended)
        {
            return;
        }

        Quiz.QuizState.QuizStatus = QuizStatus.Ended;
        Quiz.Room.Log("Ended");

        if (!Quiz.IsDisposed)
        {
            Quiz.IsTimerRunning = false;
            Quiz.Timer?.Dispose();
        }

        Quiz.QuizState.ExtraInfo = "Quiz ended. Returning to room...";
        TypedQuizHub.ReceiveQuizEnded(Quiz.Room.Players.Concat(Quiz.Room.Spectators).Select(x => x.Id));

        Directory.CreateDirectory("RoomLog");
        await File.WriteAllTextAsync($"RoomLog/r{Quiz.Room.Id}q{Quiz.Id}.json",
            JsonSerializer.Serialize(Quiz.Room.RoomLog, Utils.JsoIndented));

        if (!Quiz.SongsHistory.Any())
        {
            return;
        }

        Directory.CreateDirectory("SongHistory");
        await File.WriteAllTextAsync(
            $"SongHistory/SongHistory_{Utils.FixFileName(Quiz.Room.Name)}_r{Quiz.Room.Id}q{Quiz.Id}.json",
            JsonSerializer.Serialize(Quiz.SongsHistory, Utils.JsoIndented));

        var entityQuiz = new EntityQuiz
        {
            id = Quiz.Id,
            room_id = Quiz.Room.Id,
            settings_b64 = Quiz.Room.QuizSettings.SerializeToBase64String_PB(),
            should_update_stats = Quiz.Room.QuizSettings.ShouldUpdateStats,
            created_at = Quiz.CreatedAt,
        };

        try
        {
            long _ = await DbManager.InsertEntity(entityQuiz);
        }
        catch (Exception e)
        {
            Console.WriteLine($"Failed to insert Quiz: {JsonSerializer.Serialize(entityQuiz, Utils.JsoIndented)}");
            Console.WriteLine(e);
        }

        var quizSongHistories = new List<QuizSongHistory>();
        foreach ((int sp, SongHistory? songHistory) in Quiz.SongsHistory)
        {
            foreach ((int userId, GuessInfo guessInfo) in songHistory.PlayerGuessInfos)
            {
                // todo?
                // bool isGuest = userId >= 1_000_000;
                // if (isGuest)
                // {
                //     continue;
                // }

                var quizSongHistory = new QuizSongHistory
                {
                    quiz_id = Quiz.Id,
                    sp = sp,
                    music_id = songHistory.Song.Id,
                    user_id = userId,
                    guess = guessInfo.Guess,
                    first_guess_ms = guessInfo.FirstGuessMs,
                    is_correct = guessInfo.IsGuessCorrect,
                    is_on_list = guessInfo.IsOnList,
                    played_at = songHistory.Song.PlayedAt,
                };

                quizSongHistories.Add(quizSongHistory);
            }
        }

        try
        {
            bool success = await DbManager.InsertEntityBulk(quizSongHistories);
            if (!success)
            {
                throw new Exception();
            }
        }
        catch (Exception e)
        {
            Console.WriteLine("Failed to insert QuizSongHistory");
            Console.WriteLine(e);
        }

        if (Quiz.Room.QuizSettings.ShouldUpdateStats)
        {
            try
            {
                // If we don't create a new dictionary,
                // when a player uses 'Return to room' right before the correct answer is revealed, we can get a Collection was modified exception
                // might be better to just disallow returning to room except on results phase
                await DbManager.RecalculateSongStats(Quiz.SongsHistory.Select(x => x.Value.Song.Id).ToHashSet());
            }
            catch (Exception e)
            {
                Console.WriteLine("Failed to RecalculateSongStats");
                Console.WriteLine(e);
            }
        }
        else
        {
            Quiz.Room.Log("Not updating stats");
        }
    }

    public static async Task<Dictionary<int, List<Title>>> GenerateMultipleChoiceOptions(List<Song> songs,
        List<Session> sessions, QuizSettings quizSettings, TreasureRoom[][] treasureRooms)
    {
        var ret = new Dictionary<int, List<Title>>();
        Dictionary<int, Title> allTitles = new();
        HashSet<int> addedSourceIds = new();

        switch (quizSettings.SongSelectionKind)
        {
            case SongSelectionKind.Random:
            case SongSelectionKind.SpacedRepetition:
            case SongSelectionKind.LocalMusicLibrary:
                var allVndbInfos = await ServerUtils.GetAllVndbInfos(sessions);
                if (quizSettings.OnlyFromLists &&
                    allVndbInfos.Any(x => x.Labels != null && x.Labels.Any(y => y.VNs.Any())))
                {
                    // generate wrong multiple choice options from player vndb lists if there are any,
                    // and OnlyFromLists is enabled
                    int[] mIds = await DbManager.FindMusicIdsByLabels(allVndbInfos
                        .Where(x => x.Labels != null).SelectMany(x => x.Labels!), SongSourceSongTypeMode.Vocals);
                    var allPlayerVnTitles = await DbManager.SelectSongsMIds(mIds, false);

                    foreach (Song song in allPlayerVnTitles)
                    {
                        foreach (SongSource songSource in song.Sources)
                        {
                            if (addedSourceIds.Add(songSource.Id))
                            {
                                allTitles.Add(songSource.Id, Converters.GetSingleTitle(songSource.Titles));
                                break;
                            }
                        }
                    }
                }
                else
                {
                    // or from a combination of the selected songs and completely random songs if not
                    var selectedMids = songs.Select(x => x.Id).ToHashSet();

                    var randomSongs =
                        await DbManager.GetRandomSongs(songs.Count * 2, false, null, quizSettings.Filters);
                    var randomSongsFiltered = randomSongs.Where(x => !selectedMids.Contains(x.Id)).ToList();

                    foreach (Song song in songs.Concat(randomSongsFiltered))
                    {
                        foreach (SongSource songSource in song.Sources)
                        {
                            if (addedSourceIds.Add(songSource.Id))
                            {
                                allTitles.Add(songSource.Id, Converters.GetSingleTitle(songSource.Titles));
                                break;
                            }
                        }
                    }
                }

                break;
            case SongSelectionKind.Looting:
                // generate wrong multiple choice options from the VNs on the ground while looting
                List<KeyValuePair<string, List<Title>>> validSources = treasureRooms
                    .SelectMany(x => x.SelectMany(y => y.Treasures.Select(z => z.ValidSource))).ToList();

                foreach (Session session in sessions)
                {
                    foreach (var treasure in session.Player.LootingInfo.Inventory)
                    {
                        validSources.Add(treasure.ValidSource);
                    }
                }

                validSources = validSources.DistinctBy(x => x.Key).ToList();
                foreach ((string key, List<Title> value) in validSources)
                {
                    if (addedSourceIds.Add(key.GetHashCode()))
                    {
                        allTitles.Add(key.GetHashCode(), Converters.GetSingleTitle(value));
                    }
                }

                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        // Console.WriteLine(JsonSerializer.Serialize(addedSourceIds, Utils.Jso));
        // Console.WriteLine(JsonSerializer.Serialize(allTitles, Utils.Jso));

        for (int index = 0; index < songs.Count; index++)
        {
            Song dbSong = songs[index];

            var correctAnswer = dbSong.Sources.First();
            var correctAnswerTitle = Converters.GetSingleTitle(correctAnswer.Titles);

            List<int> randomIndexes = new();
            foreach ((int key, Title? value) in allTitles)
            {
                if (value.LatinTitle != correctAnswerTitle.LatinTitle)
                {
                    randomIndexes.Add(key);
                }
            }

            randomIndexes = randomIndexes.OrderBy(_ => Random.Shared.Next())
                .Take(quizSettings.NumMultipleChoiceOptions - 1).ToList();
            // Console.WriteLine(JsonSerializer.Serialize(availableIndexes, Utils.Jso));

            List<Title> list = new() { correctAnswerTitle };
            foreach (int randomIndex in randomIndexes)
            {
                list.Add(allTitles[randomIndex]);
            }

            list = list.OrderBy(_ => Random.Shared.Next()).ToList();
            ret[index] = list;

            int count = 0;
            foreach (Title wrongAnswerTitle in list)
            {
                if (wrongAnswerTitle.LatinTitle == correctAnswerTitle.LatinTitle)
                {
                    count++;
                }
            }

            if (count > 1)
            {
                throw new Exception("duplicate title detected when generating multiple choice options");
            }
        }

        return ret;
    }

    private async Task EnterQuiz()
    {
        // we have to do these here instead of PrimeQuiz because songs won't be determined until here if it's looting
        switch (Quiz.Room.QuizSettings.AnsweringKind)
        {
            case AnsweringKind.Typing:
                break;
            case AnsweringKind.MultipleChoice:
                var playerSessions =
                    ServerState.Sessions.Where(x => Quiz.Room.Players.Any(y => y.Id == x.Player.Id)).ToList();
                Quiz.MultipleChoiceOptions =
                    await GenerateMultipleChoiceOptions(Quiz.Songs, playerSessions,
                        Quiz.Room.QuizSettings, Quiz.Room.TreasureRooms);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        if (Quiz.Room.QuizSettings.EnabledGuessKinds.TryGetValue(GuessKind.A, out bool a) && a &&
            Quiz.Room.QuizSettings.IsMergeArtistAliases)
        {
            ArtistAliasesDict =
                await DbManager.SelectArtistAliases(Quiz.Songs.SelectMany(x => x.Artists.Select(y => y.Id)).ToArray());
        }

        // reduce serialized Room size
        Quiz.Room.TreasureRooms = Array.Empty<TreasureRoom[]>();

        if (!Quiz.Room.QuizSettings.AllowViewingInventoryDuringQuiz)
        {
            foreach (var player in Quiz.Room.Players)
            {
                // reduce serialized Room size & prevent Inventory leak
                player.LootingInfo = new PlayerLootingInfo();
            }
        }

        TypedQuizHub.ReceiveQuizEntered(Quiz.Room.Players.Concat(Quiz.Room.Spectators).Select(x => x.Id));
        await Task.Delay(TimeSpan.FromSeconds(1));
    }

    // todo Exclude does nothing on its own
    public async Task<bool> PrimeQuiz()
    {
        var teams = Quiz.Room.Players.GroupBy(x => x.TeamId).ToList();
        if (Quiz.Room.QuizSettings.TeamSize > 1)
        {
            var fullTeam = teams.FirstOrDefault(x => x.Count() > Quiz.Room.QuizSettings.TeamSize);
            if (fullTeam != null)
            {
                Quiz.Room.Log($"Team {fullTeam.Key} has too many players.", writeToChat: true);
                return false;
            }

            // todo? figure out how to handle hotjoining players for non-one-team teamed games
            if (teams.Count > 1 || teams.Single().First().TeamId != 1)
            {
                Quiz.Room.QuizSettings.IsHotjoinEnabled = false;
            }
        }

        if (Quiz.Room.QuizSettings.GamemodeKind == GamemodeKind.NGMC)
        {
            if (teams.Count < 2)
            {
                Quiz.Room.Log($"NGMC: There must be at least two teams.", writeToChat: true);
                return false;
            }

            if (Quiz.Room.Players.Any(x => x.TeamId is < 1 or > 2))
            {
                Quiz.Room.Log($"NGMC: The teams must use the team ids 1 and 2.", writeToChat: true);
                return false;
            }

            bool saw2 = false;
            foreach (Player player in Quiz.Room.Players)
            {
                if (player.TeamId == 2)
                {
                    saw2 = true;
                }

                if (player.TeamId == 1 && saw2)
                {
                    Quiz.Room.Log($"NGMC: The teams must be in sequential order.", writeToChat: true);
                    return false;
                }
            }

            if (Quiz.Room.QuizSettings.MaxLives < 1)
            {
                Quiz.Room.Log($"NGMC: The Lives setting must be greater than 0.", writeToChat: true);
                return false;
            }

            if (Quiz.Room.Players.Any(x => x.NGMCGuessesInitial < 1))
            {
                Quiz.Room.Log($"NGMC: Every player must have at least 1 guess.", writeToChat: true);
                return false;
            }

            Quiz.Room.QuizSettings.IsHotjoinEnabled = false;
        }

        if (!Quiz.Room.QuizSettings.EnabledGuessKinds.Any(x => x.Value))
        {
            Quiz.Room.Log("At least one Guess type must be enabled.", writeToChat: true);
            return false;
        }

        if (!Quiz.Room.QuizSettings.IsOnlyMstGuessTypeEnabled)
        {
            if (Quiz.Room.QuizSettings.AnsweringKind == AnsweringKind.MultipleChoice)
            {
                Quiz.Room.Log(
                    $"Only the \"{GuessKind.Mst.GetDescription()}\" Guess type may be enabled for the \"{AnsweringKind.MultipleChoice.GetDescription()}\" Answering method.",
                    writeToChat: true);
                return false;
            }

            // if ((Quiz.Room.QuizSettings.Filters.SongSourceSongTypeRandomEnabledSongTypes.TryGetValue(
            //         SongSourceSongType.BGM, out bool bgmIsEnabledForRandom) && bgmIsEnabledForRandom) ||
            //     Quiz.Room.QuizSettings.Filters.SongSourceSongTypeFilters.TryGetValue(
            //         SongSourceSongType.BGM, out IntWrapper? bgmCount) && bgmCount.Value > 0)
            // {
            //     Quiz.Room.Log(
            //         $"Only the \"{GuessKind.Mst.GetDescription()}\" Guess type may be enabled if the song type \"{SongSourceSongType.BGM}\" is enabled.",
            //         writeToChat: true);
            //     return false;
            // }
        }

        if (Quiz.Room.QuizSettings.IsNoSoundMode)
        {
            Quiz.Room.QuizSettings.TimeoutMs = 5000;
        }

        CorrectAnswersDictMst = new Dictionary<int, List<string>>();
        CorrectAnswersDictA = new Dictionary<int, List<string>>();
        CorrectAnswersDictMt = new Dictionary<int, List<string>>();
        CorrectAnswersDictRigger = new Dictionary<int, List<string>>();
        ArtistAliasesDict = null;
        Dictionary<int, List<string>> validSourcesDict = new();

        var playerSessions = ServerState.Sessions.Where(x => Quiz.Room.Players.Any(y => y.Id == x.Player.Id))
            .ToDictionary(x => x.Player.Id, x => x);
        foreach (Player player in Quiz.Room.Players)
        {
            player.Lives = Quiz.Room.QuizSettings.MaxLives;
            player.Score = 0;
            player.Guess = null;
            player.IsGuessKindCorrectDict = null;
            player.FirstGuessMs = 0;
            player.IsBuffered = false;
            player.IsSkipping = false;
            // do not set player.IsReadiedUp to false here, because it would be annoying to ready up again if we return false
            player.PlayerStatus = PlayerStatus.Default;
            player.LootingInfo = new PlayerLootingInfo();
            player.NGMCGuessesCurrent = player.NGMCGuessesInitial;
            player.NGMCCanBurn = false;
            player.NGMCMustPick = false;
            player.NGMCCanBePicked = false;
            player.NGMCMustBurn = false;

            if (Quiz.Room.QuizSettings.OnlyFromLists)
            {
                // var stopWatch = new Stopwatch();
                // stopWatch.Start();

                var session = playerSessions[player.Id];
                var vndbInfo = await ServerUtils.GetVndbInfo_Inner(player.Id, session.ActiveUserLabelPresetName);
                if (string.IsNullOrWhiteSpace(vndbInfo.VndbId) ||
                    string.IsNullOrEmpty(session.ActiveUserLabelPresetName))
                {
                    continue;
                }

                if (vndbInfo.Labels != null)
                {
                    var userLabels =
                        await DbManager.GetUserLabels(player.Id, vndbInfo.VndbId, session.ActiveUserLabelPresetName);
                    var include = userLabels.Where(x => (LabelKind)x.kind == LabelKind.Include).ToList();
                    var exclude = userLabels.Where(x => (LabelKind)x.kind == LabelKind.Exclude).ToList();

                    // todo Exclude does nothing on its own (don't break Balanced while fixing this)
                    if (include.Any())
                    {
                        validSourcesDict[player.Id] = new List<string>();
                        // todo batch
                        // todo method
                        foreach (UserLabel userLabel in include)
                        {
                            var userLabelVns = await DbManager.GetUserLabelVns(userLabel.id);
                            validSourcesDict[player.Id].AddRange(userLabelVns.Select(x => x.vnid));
                        }

                        if (exclude.Any())
                        {
                            var excluded = new List<string>();
                            foreach (UserLabel userLabel in exclude)
                            {
                                var userLabelVns = await DbManager.GetUserLabelVns(userLabel.id);
                                excluded.AddRange(userLabelVns.Select(x => x.vnid));
                            }

                            validSourcesDict[player.Id] = validSourcesDict[player.Id].Except(excluded).ToList();
                        }

                        validSourcesDict[player.Id] = validSourcesDict[player.Id].Distinct().ToList();
                    }
                }

                // stopWatch.Stop();
                // Console.WriteLine(
                //     $"OnlyFromLists took {Math.Round(((stopWatch.ElapsedTicks * 1000.0) / Stopwatch.Frequency) / 1000, 2)}s");
            }
        }

        List<string> validSources = validSourcesDict.SelectMany(x => x.Value).Distinct().ToList();

        Quiz.Room.QuizSettings.Filters.VndbAdvsearchFilter =
            Quiz.Room.QuizSettings.Filters.VndbAdvsearchFilter.SanitizeVndbAdvsearchStr();
        if (!string.IsNullOrWhiteSpace(Quiz.Room.QuizSettings.Filters.VndbAdvsearchFilter))
        {
            Quiz.Room.Log($"VNDB search filter is being processed.", -1, true);
            bool success = false;
            var cancellationTokenSource = new CancellationTokenSource();
            cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(5));

            var task = Task.Run(async () =>
            {
                string[]? vndbUrls =
                    await VndbMethods.GetVnUrlsMatchingAdvsearchStr(null,
                        Quiz.Room.QuizSettings.Filters.VndbAdvsearchFilter, cancellationTokenSource.Token);

                if (vndbUrls == null || !vndbUrls.Any())
                {
                    Quiz.Room.Log($"VNDB search filter returned no results.", -1, true);
                    success = false;
                    return;
                }

                validSources = vndbUrls.Distinct().ToList();
                Quiz.Room.Log($"VNDB search filter returned {validSources.Count} results.", -1, true);
                Quiz.Room.Log("validSources overridden by VndbAdvsearchFilter: " +
                              JsonSerializer.Serialize(validSources, Utils.Jso));

                success = true;
            }, cancellationTokenSource.Token);

            try
            {
                await task;
            }
            catch (Exception)
            {
                Quiz.Room.Log($"VNDB search took longer than 5 seconds - canceling.", -1, true);
            }

            if (!success)
            {
                return false;
            }
        }
        else
        {
            Quiz.Room.Log("validSources: " + JsonSerializer.Serialize(validSources, Utils.Jso), writeToConsole: false);
        }

        Quiz.Room.Log($"validSourcesCount: {validSources.Count}");

        // var validCategories = Quiz.Room.QuizSettings.Filters.CategoryFilters;
        // Quiz.Room.Log("validCategories: " + JsonSerializer.Serialize(validCategories, Utils.Jso));
        // Quiz.Room.Log($"validCategoriesCount: {validCategories.Count}");

        // var validArtists = Quiz.Room.QuizSettings.Filters.ArtistFilters;
        // Quiz.Room.Log("validArtists: " + JsonSerializer.Serialize(validArtists, Utils.Jso));
        // Quiz.Room.Log($"validArtistsCount: {validArtists.Count}");

        // todo handle hotjoining players
        var vndbInfos = new Dictionary<int, PlayerVndbInfo>();
        foreach ((int _, Session session) in playerSessions)
        {
            vndbInfos[session.Player.Id] =
                await ServerUtils.GetVndbInfo_Inner(session.Player.Id, session.ActiveUserLabelPresetName);
        }

        List<Song> dbSongs;
        switch (Quiz.Room.QuizSettings.SongSelectionKind)
        {
            case SongSelectionKind.Random:
            case SongSelectionKind.SpacedRepetition:
                List<int>? validMids = null;
                List<int>? invalidMids = null;
                if (Quiz.Room.QuizSettings.SongSelectionKind == SongSelectionKind.SpacedRepetition)
                {
                    switch (Quiz.Room.QuizSettings.SpacedRepetitionKind)
                    {
                        case SpacedRepetitionKind.Review:
                            validMids =
                                await DbManager.GetMidsWithReviewsDue(Quiz.Room.Players.Concat(Quiz.Room.Spectators)
                                    .Select(x => x.Id).ToList());
                            Quiz.Room.Log($"{validMids.Count} songs are due for review.", writeToChat: true);
                            break;
                        case SpacedRepetitionKind.NoIntervalOnly:
                            invalidMids =
                                await DbManager.GetMidsWithIntervals(Quiz.Room.Players.Concat(Quiz.Room.Spectators)
                                    .Select(x => x.Id).ToList());
                            Quiz.Room.Log($"Excluding {invalidMids.Count} songs with intervals.", writeToChat: true);
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }

                switch (Quiz.Room.QuizSettings.ListDistributionKind)
                {
                    case ListDistributionKind.Random:
                    case ListDistributionKind.Unread:
                        {
                            dbSongs = await DbManager.GetRandomSongs(Quiz.Room.QuizSettings.NumSongs,
                                Quiz.Room.QuizSettings.Duplicates, validSources,
                                filters: Quiz.Room.QuizSettings.Filters, players: Quiz.Room.Players.ToList(),
                                listDistributionKind: Quiz.Room.QuizSettings.ListDistributionKind,
                                validMids: validMids, invalidMids: invalidMids, ownerUserId: Quiz.Room.Owner.Id);
                            break;
                        }
                    case ListDistributionKind.Balanced:
                        {
                            // todo tests
                            if (validSourcesDict.Count < 2)
                            {
                                Quiz.Room.Log(
                                    $"Balanced mode requires there to be at least two players with a list active.",
                                    writeToChat: true);
                                return false;
                            }

                            var songTypesLeft = Quiz.Room.QuizSettings.Filters.SongSourceSongTypeFilters
                                .OrderByDescending(x => x.Key) // Random must be selected first
                                .Where(x => x.Value.Value > 0)
                                .ToDictionary(x => x.Key, x => x.Value.Value);

                            int sumOfSongTypesLeft = songTypesLeft.Sum(x => x.Value);
                            int targetNumSongsPerPlayer = Math.Min(
                                sumOfSongTypesLeft / validSourcesDict.Count,
                                validSourcesDict.MinBy(x => x.Value.Count).Value.Count);
                            Console.WriteLine($"targetNumSongsPerPlayer: {targetNumSongsPerPlayer}");

                            dbSongs = new List<Song>();

                            // we randomize the players here in order to make sure that the first player doesn't get all the EDs (etc.) if EDs are set to a low amount
                            foreach ((int pId, _) in validSourcesDict.OrderBy(x => Random.Shared.Next()).ToArray())
                            {
                                Console.WriteLine(
                                    $"selecting {targetNumSongsPerPlayer} songs for p{pId} {Quiz.Room.Players.Single(x => x.Id == pId).Username}");

                                dbSongs.AddRange(await DbManager.GetRandomSongs(
                                    targetNumSongsPerPlayer,
                                    Quiz.Room.QuizSettings.Duplicates,
                                    validSourcesDict[pId].OrderBy(x => Random.Shared.Next()).ToList(),
                                    filters: Quiz.Room.QuizSettings.Filters, players: Quiz.Room.Players.ToList(),
                                    validMids: validMids, invalidMids: invalidMids, songTypesLeft: songTypesLeft,
                                    ownerUserId: Quiz.Room.Owner.Id));
                            }

                            if (!Quiz.Room.QuizSettings.Duplicates)
                            {
                                Console.WriteLine($"dbSongs.Count before distinct1: {dbSongs.Count}");
                                var finalDbSongs = new List<Song>();
                                var seenSourceIds = new HashSet<int>();
                                foreach (Song dbSong in dbSongs)
                                {
                                    foreach (SongSource dbSongSource in dbSong.Sources)
                                    {
                                        if (seenSourceIds.Add(dbSongSource.Id))
                                        {
                                            finalDbSongs.Add(dbSong);
                                        }
                                    }
                                }

                                dbSongs = finalDbSongs;
                                Console.WriteLine($"dbSongs.Count after distinct1: {dbSongs.Count}");
                            }

                            Console.WriteLine($"dbSongs.Count before distinct2: {dbSongs.Count}");
                            dbSongs = dbSongs.DistinctBy(x => x.Id).ToList();
                            Console.WriteLine($"dbSongs.Count after distinct2: {dbSongs.Count}");

                            int diff = (targetNumSongsPerPlayer * validSourcesDict.Count) - dbSongs.Count;
                            Console.WriteLine($"NumSongs to actual diff: {diff}");

                            Quiz.Room.Log($"Balanced mode tried to select {targetNumSongsPerPlayer} songs per player.",
                                writeToChat: true);

                            dbSongs = dbSongs.OrderBy(_ => Random.Shared.Next()).ToList();
                            break;
                        }
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                if (dbSongs.Count == 0)
                {
                    return false;
                }

                foreach (Song dbSong in dbSongs)
                {
                    dbSong.PlayerLabels = GetPlayerLabelsForSong(dbSong, vndbInfos);
                }

                Quiz.Songs = dbSongs;
                Quiz.QuizState.NumSongs = Quiz.Songs.Count;
                break;
            case SongSelectionKind.Looting:
                dbSongs = new List<Song>();
                var cancellationTokenSource = new CancellationTokenSource();
                cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(5));

                // todo lots of selects are performed when NumSongs is really small
                int songsLeft =
                    Math.Max((int)((Quiz.Room.QuizSettings.NumSongs / 2f) * (((float)Quiz.Room.Players.Count + 3) / 2)),
                        100);
                while (songsLeft > 0 && !cancellationTokenSource.IsCancellationRequested)
                {
                    var selectedSongs = await DbManager.GetRandomSongs(songsLeft,
                        Quiz.Room.QuizSettings.Duplicates, validSources,
                        filters: Quiz.Room.QuizSettings.Filters, players: Quiz.Room.Players.ToList(),
                        ownerUserId: Quiz.Room.Owner.Id);

                    if (!selectedSongs.Any())
                    {
                        break;
                    }

                    songsLeft -= selectedSongs.Count;
                    dbSongs.AddRange(selectedSongs);
                }

                Console.WriteLine($"Looting dbSongs.Count: {dbSongs.Count}");
                dbSongs = dbSongs.DistinctBy(x => x.Id).ToList();
                Console.WriteLine($"Looting dbSongs.Count distinct: {dbSongs.Count}");

                if (dbSongs.Count == 0)
                {
                    return false;
                }

                var validSourcesLooting = new Dictionary<string, List<Title>>();
                foreach (Song dbSong in dbSongs)
                {
                    foreach (var dbSongSource in dbSong.Sources)
                    {
                        // todo songs with multiple vns overriding each other
                        validSourcesLooting[dbSongSource.Links.First(x => x.Type == SongSourceLinkType.VNDB).Url] =
                            dbSongSource.Titles;
                    }
                }

                Quiz.ValidSourcesForLooting = validSourcesLooting;
                break;
            case SongSelectionKind.LocalMusicLibrary:
                dbSongs = new List<Song>();
                string[] filePaths =
                    Directory.GetFiles(Constants.LocalMusicLibraryPath, "*.mp3", SearchOption.AllDirectories);
                for (int i = 0; i < Quiz.Room.QuizSettings.NumSongs; i++)
                {
                    var song = new Song() { Id = i };
                    dbSongs.Add(song);

                    string filePath = filePaths[Random.Shared.Next(filePaths.Length - 1)];
                    try
                    {
                        var tFile = TagLib.File.Create(filePath);
                        string? metadataSources = tFile.Tag.Album;
                        string? metadataTitle = tFile.Tag.Title;
                        string[] metadataArtists = tFile.Tag.Performers.Concat(tFile.Tag.AlbumArtists).ToArray();
                        if (!metadataArtists.Any())
                        {
                            metadataArtists = new[] { "" };
                        }

                        song.Sources.Add(new SongSource()
                        {
                            Titles = new List<Title>()
                            {
                                new Title() { LatinTitle = metadataSources ?? "", IsMainTitle = true }
                            }
                        });

                        song.Titles.Add(new Title() { LatinTitle = metadataTitle ?? "", IsMainTitle = true });

                        song.Artists.Add(new SongArtist()
                        {
                            Titles = new List<Title>(metadataArtists.Select(x =>
                                new Title() { LatinTitle = x, IsMainTitle = true })),
                        });

                        song.Links.Add(new SongLink()
                        {
                            Duration = TimeSpan.FromSeconds(60),
                            IsVideo = false,
                            Url = $"emqlocalmusiclibrary{filePath.Replace("G:/Music", "").Replace("G:\\Music", "")}"
                        });

                        song.StartTime = song.DetermineSongStartTime(Quiz.Room.QuizSettings.Filters);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"TagLib exception for {filePath}: " + e.Message);
                    }
                }

                if (dbSongs.Count == 0)
                {
                    return false;
                }

                foreach (Song dbSong in dbSongs)
                {
                    dbSong.PlayerLabels = GetPlayerLabelsForSong(dbSong, vndbInfos);
                }

                Quiz.Songs = dbSongs;
                Quiz.QuizState.NumSongs = Quiz.Songs.Count;
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        foreach (Song dbSong in dbSongs)
        {
            dbSong.Links = SongLink.FilterSongLinks(dbSong.Links);
        }

        // Console.WriteLine(JsonSerializer.Serialize(Quiz.Songs));
        Quiz.QuizState.ExtraInfo = "Waiting buffering...";

        return true;
    }

    public async Task StartQuiz()
    {
        Quiz.QuizState.QuizStatus = QuizStatus.Playing;

        // await EnterQuiz();
        // HubContext.Clients.Clients(Quiz.Room.AllPlayerConnectionIds.Values).SendAsync("ReceiveQuizStarted");

        switch (Quiz.Room.QuizSettings.SongSelectionKind)
        {
            case SongSelectionKind.Random:
            case SongSelectionKind.SpacedRepetition:
            case SongSelectionKind.LocalMusicLibrary:
                await EnterQuiz();
                await EnterGuessingPhase();
                TypedQuizHub.ReceiveQuizStarted(Quiz.Room.Players.Concat(Quiz.Room.Spectators).Select(x => x.Id));
                break;
            case SongSelectionKind.Looting:
                await EnterLootingPhase();
                TypedQuizHub.ReceivePyramidEntered(Quiz.Room.Players.Concat(Quiz.Room.Spectators).Select(x => x.Id));
                await Task.Delay(TimeSpan.FromSeconds(1));
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        await SetTimer();
    }

    public async Task OnSendPlayerIsBuffered(int playerId, string source)
    {
        Player? player = Quiz.Room.Players.SingleOrDefault(player => player.Id == playerId);
        if (player == null)
        {
            // early return if spectator
            return;
        }

        player.IsBuffered = true;
        int isBufferedCount = Quiz.Room.Players.Count(x => x.IsBuffered);
        Quiz.Room.Log($"isBufferedCount: {isBufferedCount} Source: {source}", playerId, writeToConsole: false);
    }

    public async Task OnSendPlayerJoinedQuiz(string connectionId, int playerId)
    {
        if (Quiz.QuizState.QuizStatus == QuizStatus.Playing)
        {
            TypedQuizHub.ReceiveQuizStarted(new[] { playerId });

            // todo player initialization logic shouldn't be here at all after the user + player separation
            var player = Quiz.Room.Players.SingleOrDefault(x => x.Id == playerId);
            if (player == null)
            {
                // early return if spectator
                return;
            }

            if (player.Score > 0 || player.Guess != null || (Quiz.Room.QuizSettings.MaxLives > 0 &&
                                                             player.Lives != Quiz.Room.QuizSettings.MaxLives))
            {
                return;
            }

            player.Lives = Quiz.Room.QuizSettings.MaxLives;
            player.Score = 0;
            player.Guess = null;
            player.IsGuessKindCorrectDict = null;
            player.FirstGuessMs = 0;
            player.IsBuffered = false;
            player.IsSkipping = false;
            player.IsReadiedUp = false;
            player.PlayerStatus = PlayerStatus.Default;
            player.NGMCGuessesCurrent = player.NGMCGuessesInitial;
            player.NGMCCanBurn = false;
            player.NGMCMustPick = false;
            player.NGMCCanBePicked = false;
            player.NGMCMustBurn = false;

            if (Quiz.Room.QuizSettings.TeamSize > 1)
            {
                var teammate = Quiz.Room.Players.FirstOrDefault(x => x.TeamId == player.TeamId);
                if (teammate != null)
                {
                    player.Lives = teammate.Lives;

                    if (Quiz.Room.QuizSettings.GamemodeKind != GamemodeKind.NGMC)
                    {
                        player.Score = teammate.Score;
                    }
                }
            }

            TypedQuizHub.ReceiveUpdateRoom(Quiz.Room.Players.Concat(Quiz.Room.Spectators).Select(x => x.Id), Quiz.Room,
                false);
        }
    }

    public async Task OnSendGuessChanged(int playerId, string? guess, GuessKind guessKind)
    {
        // don't allow players to change guesses in shared-guesses teamed games after team guesses have been determined
        if (Quiz.QuizState.Phase is QuizPhaseKind.Guess || (Quiz.QuizState.Phase is QuizPhaseKind.Judgement &&
                                                            !Quiz.QuizState.TeamGuessesHaveBeenDetermined))
        {
            var player = Quiz.Room.Players.SingleOrDefault(x => x.Id == playerId);
            if (player != null)
            {
                if (Quiz.QuizState.Phase is QuizPhaseKind.Judgement)
                {
                    if (player.PlayerStatus is PlayerStatus.Correct or PlayerStatus.Wrong)
                    {
                        // for lagging players
                        return;
                    }
                }

                guess = guess == null ? "" : guess[..Math.Min(guess.Length, Constants.MaxGuessLength)];
                player.Guess ??= new PlayerGuess();
                player.PlayerStatus = PlayerStatus.Guessed;

                switch (guessKind)
                {
                    case GuessKind.Mst:
                        player.Guess.Mst = guess;
                        break;
                    case GuessKind.A:
                        player.Guess.A = guess;
                        break;
                    case GuessKind.Mt:
                        player.Guess.Mt = guess;
                        break;
                    case GuessKind.Rigger:
                        player.Guess.Rigger = guess;
                        break;
                    case GuessKind.Developer:
                        player.Guess.Developer = guess;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(guessKind), guessKind, null);
                }

                if (player.FirstGuessMs <= 0)
                {
                    switch (Quiz.QuizState.Phase)
                    {
                        case QuizPhaseKind.Guess:
                            player.FirstGuessMs = Quiz.Room.QuizSettings.GuessMs - (int)Quiz.QuizState.RemainingMs;
                            break;
                        case QuizPhaseKind.Judgement:
                            player.FirstGuessMs = Quiz.Room.QuizSettings.GuessMs;
                            break;
                        case QuizPhaseKind.Results:
                        case QuizPhaseKind.Looting:
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }

                if (Quiz.Room.QuizSettings.IsSharedGuessesTeams)
                {
                    // also includes the player themselves
                    var teammates = Quiz.Room.Players.Where(x => x.TeamId == player.TeamId).ToArray();

                    var teammateIds = teammates.Select(x => x.Id);
                    var teammateGuesses = Quiz.Room.PlayerGuesses.Where(x => teammateIds.Contains(x.Key));
                    var dict = teammateGuesses.ToDictionary(x => x.Key, x => x.Value);

                    TypedQuizHub.ReceivePlayerGuesses(teammateIds, dict);
                }
                else
                {
                    TypedQuizHub.ReceivePlayerGuesses(new[] { playerId },
                        Quiz.Room.PlayerGuesses.Where(x => x.Key == playerId).ToDictionary(x => x.Key, x => x.Value));
                }

                TypedQuizHub.ReceiveUpdateRoom(Quiz.Room.Players.Concat(Quiz.Room.Spectators).Select(x => x.Id),
                    Quiz.Room, false);
                await TriggerSkipIfNecessary();
            }
            else
            {
                Quiz.Room.Log("invalid guess submitted", playerId);
            }
        }
    }

    public async Task OnSendTogglePause()
    {
        if (Quiz.QuizState.QuizStatus == QuizStatus.Playing &&
            !Quiz.QuizState.ExtraInfo.Contains("Waiting buffering") &&
            !Quiz.QuizState.ExtraInfo.Contains("Skipping")) // todo
        {
            if (Quiz.QuizState.IsPaused)
            {
                Quiz.QuizState.IsPaused = false;
                Quiz.Room.Log("Unpaused", -1, true);
            }
            else
            {
                Quiz.QuizState.IsPaused = true;
                Quiz.Room.Log("Paused", -1, true);
            }

            TypedQuizHub.ReceiveUpdateRoom(Quiz.Room.Players.Concat(Quiz.Room.Spectators).Select(x => x.Id), Quiz.Room,
                false);
        }
    }

    // public async Task OnSendPlayerLeaving(int playerId)
    // {
    //
    // }

    private async Task EnterLootingPhase()
    {
        var rng = Random.Shared;

        Quiz.QuizState.Phase = QuizPhaseKind.Looting;
        Quiz.QuizState.RemainingMs = Quiz.Room.QuizSettings.LootingMs;

        TreasureRoom[][] GenerateTreasureRooms(Dictionary<string, List<Title>> validSources)
        {
            int gridSize = Quiz.Room.Players.Count switch
            {
                <= 3 => 3,
                4 or 5 => 4,
                6 or 7 => 5,
                8 or 9 => 6,
                >= 10 => 7,
            };

            TreasureRoom[][] treasureRooms =
                new TreasureRoom[gridSize].Select(_ => new TreasureRoom[gridSize]).ToArray();
            for (int i = 0; i < gridSize; i++)
            {
                for (int j = 0; j < gridSize; j++)
                {
                    treasureRooms[i][j] =
                        new TreasureRoom() { Coords = new Point(i, j), Treasures = new List<Treasure>() };

                    if (j - 1 >= 0 && j - 1 < gridSize)
                    {
                        treasureRooms[i][j].Exits.Add(Direction.North, new Point(i, j - 1));
                    }

                    if (i + 1 < gridSize)
                    {
                        treasureRooms[i][j].Exits.Add(Direction.East, new Point(i + 1, j));
                    }

                    if (j + 1 < gridSize)
                    {
                        treasureRooms[i][j].Exits.Add(Direction.South, new Point(i, j + 1));
                    }

                    if (i - 1 >= 0 && i - 1 < gridSize)
                    {
                        treasureRooms[i][j].Exits.Add(Direction.West, new Point(i - 1, j));
                    }
                }
            }

            Quiz.QuizState.LootingGridSize = gridSize;

            foreach (var player in Quiz.Room.Players)
            {
                player.PlayerStatus = PlayerStatus.Looting;
                player.LootingInfo = new PlayerLootingInfo
                {
                    X = LootingConstants.TreasureRoomWidth / 2,
                    Y = LootingConstants.TreasureRoomHeight / 2,
                    Inventory = new List<Treasure>(),
                    TreasureRoomCoords =
                        new Point(rng.Next(Quiz.QuizState.LootingGridSize),
                            rng.Next(Quiz.QuizState.LootingGridSize)),
                };
            }

            const int treasureMaxX = LootingConstants.TreasureRoomWidth - LootingConstants.PlayerAvatarSize;
            const int treasureMaxY = LootingConstants.TreasureRoomHeight - LootingConstants.PlayerAvatarSize;
            foreach (var dbSong in validSources)
            {
                var treasure = new Treasure(
                    Guid.NewGuid(),
                    dbSong,
                    new Point(rng.Next(treasureMaxX), rng.Next(treasureMaxY)));

                // todo max treasures in one room?
                // todo better position randomization?
                var treasureRoomId = new Point(rng.Next(0, gridSize), rng.Next(0, gridSize));
                treasureRooms[treasureRoomId.X][treasureRoomId.Y].Treasures.Add(treasure);
            }

            // Console.WriteLine("treasureRooms: " + JsonSerializer.Serialize(treasureRooms)][ Utils.Jso);
            return treasureRooms;
        }

        Quiz.Room.TreasureRooms = GenerateTreasureRooms(Quiz.ValidSourcesForLooting);
        // HubContext.Clients.Clients(Quiz.Room.AllPlayerConnectionIds.Values).SendAsync("ReceivePyramidEntered");

        // todo
    }

    private async Task<bool> SetLootedSongs()
    {
        var validSources = new Dictionary<int, List<string>>();
        foreach (var player in Quiz.Room.Players)
        {
            validSources[player.Id] = new List<string>();
            foreach (var treasure in player.LootingInfo.Inventory)
            {
                validSources[player.Id].Add(treasure.ValidSource.Key);
            }
        }

        int distinctSourcesCount = validSources.SelectMany(x => x.Value).Distinct().Count();
        if (distinctSourcesCount == 0)
        {
            return false;
        }

        Quiz.Room.Log($"Players looted {distinctSourcesCount} distinct sources");

        var dbSongs = await DbManager.GetRandomSongs(
            Quiz.Room.QuizSettings.NumSongs,
            Quiz.Room.QuizSettings.Duplicates,
            validSources.SelectMany(x => x.Value).ToList(),
            Quiz.Room.QuizSettings.Filters,
            players: Quiz.Room.Players.ToList(),
            ownerUserId: Quiz.Room.Owner.Id);

        if (!dbSongs.Any())
        {
            return false;
        }

        Quiz.Room.Log($"Selected {dbSongs.Count} looted songs");

        Quiz.Songs = dbSongs;
        Quiz.QuizState.NumSongs = Quiz.Songs.Count;

        foreach (Song dbSong in dbSongs)
        {
            dbSong.Links = SongLink.FilterSongLinks(dbSong.Links);

            // todo merge this with ValidSourcesForLooting and get rid of this
            var currentSongSourceVndbUrls = dbSong.Sources
                .SelectMany(x => x.Links.Where(y => y.Type == SongSourceLinkType.VNDB))
                .Select(z => z.Url)
                .ToList();

            var lootedPlayers = validSources.Where(x => x.Value.Any(y => currentSongSourceVndbUrls.Contains(y)))
                .ToDictionary(x => x.Key, x => x.Value);

            var playerLabels = new Dictionary<int, List<Label>>();
            foreach (KeyValuePair<int, List<string>> lootedPlayer in lootedPlayers)
            {
                var newLabel = new Label
                {
                    Id = -1,
                    IsPrivate = false,
                    Name = "Looted",
                    VNs = new Dictionary<string, int> { { currentSongSourceVndbUrls.First(), -1 } },
                    Kind = LabelKind.Include
                };

                playerLabels.Add(lootedPlayer.Key, new List<Label> { newLabel });
            }

            dbSong.PlayerLabels = playerLabels;
        }

        return true;
    }

    public async Task OnSendPlayerMoved(Player player, int newX, int newY, DateTime dateTime,
        string connectionId)
    {
        // todo anti-cheat
        player.LootingInfo.X = newX;
        player.LootingInfo.Y = newY;

        TypedQuizHub.ReceiveUpdatePlayerLootingInfo(
            Quiz.Room.Players.Where(x => x.Id != player.Id).Select(y => y.Id),
            player.Id, player.LootingInfo with { Inventory = new List<Treasure>() }, true);
    }

    public async Task OnSendPickupTreasure(Session session, Guid treasureGuid)
    {
        if (!Quiz.Room.TreasureRooms.Any())
        {
            return;
        }

        var player = session.Player;
        if (player.LootingInfo.TreasureRoomCoords.X < Quiz.QuizState.LootingGridSize &&
            player.LootingInfo.TreasureRoomCoords.Y < Quiz.QuizState.LootingGridSize)
        {
            var treasureRoom = Quiz.Room.TreasureRooms[player.LootingInfo.TreasureRoomCoords.X][
                player.LootingInfo.TreasureRoomCoords.Y];
            var treasure = treasureRoom.Treasures.SingleOrDefault(x => x.Guid == treasureGuid);

            if (treasure != null)
            {
                if (treasure.Position.IsReachableFromCoords((int)player.LootingInfo.X, (int)player.LootingInfo.Y))
                {
                    if (player.LootingInfo.Inventory.Count < Quiz.Room.QuizSettings.InventorySize)
                    {
                        player.LootingInfo.Inventory.Add(treasure);
                        treasureRoom.Treasures.Remove(treasure);

                        TypedQuizHub.ReceiveUpdateTreasureRoom(
                            Quiz.Room.Players.Concat(Quiz.Room.Spectators).Select(x => x.Id), treasureRoom);

                        TypedQuizHub.ReceiveUpdateRemainingMs(new[] { session.Player.Id }, Quiz.QuizState.RemainingMs);

                        TypedQuizHub.ReceiveUpdatePlayerLootingInfo(new[] { session.Player.Id }, player.Id,
                            player.LootingInfo,
                            false);
                    }
                }
                else
                {
                    Quiz.Room.Log(
                        $"Player is not close enough to the treasure to pickup: {player.LootingInfo.X},{player.LootingInfo.Y} -> " +
                        $"{treasure.Position.X},{treasure.Position.Y}", player.Id);
                }
            }
            else
            {
                Quiz.Room.Log(
                    $"Could not find the treasure {treasureGuid} to pickup at {treasureRoom.Coords.X},{treasureRoom.Coords.Y}");
            }
        }
        else
        {
            Quiz.Room.Log("Invalid player treasure room coords", player.Id);
        }
    }

    public async Task OnSendDropTreasure(Session session, Guid treasureGuid)
    {
        var player = Quiz.Room.Players.Single(x => x.Id == session.Player.Id);
        var treasure = player.LootingInfo.Inventory.SingleOrDefault(x => x.Guid == treasureGuid);
        if (treasure is not null)
        {
            var treasureRoom = Quiz.Room.TreasureRooms[player.LootingInfo.TreasureRoomCoords.X][
                player.LootingInfo.TreasureRoomCoords.Y];

            int newX = (int)Math.Clamp(
                player.LootingInfo.X +
                Random.Shared.Next(-LootingConstants.PlayerAvatarSize, LootingConstants.PlayerAvatarSize),
                0, LootingConstants.TreasureRoomWidth - LootingConstants.PlayerAvatarSize);

            int newY = (int)Math.Clamp(
                player.LootingInfo.Y +
                Random.Shared.Next(-LootingConstants.PlayerAvatarSize, LootingConstants.PlayerAvatarSize),
                0, LootingConstants.TreasureRoomHeight - LootingConstants.PlayerAvatarSize);

            player.LootingInfo.Inventory.Remove(treasure);
            treasureRoom.Treasures.Add(treasure with { Position = new Point(newX, newY) });

            TypedQuizHub.ReceiveUpdateTreasureRoom(Quiz.Room.Players.Concat(Quiz.Room.Spectators).Select(x => x.Id),
                treasureRoom);

            TypedQuizHub.ReceiveUpdateRemainingMs(new[] { session.Player.Id }, Quiz.QuizState.RemainingMs);

            TypedQuizHub.ReceiveUpdatePlayerLootingInfo(new[] { session.Player.Id }, player.Id, player.LootingInfo,
                false);
        }
    }

    public async Task OnSendChangeTreasureRoom(Session session, Point treasureRoomCoords, Direction direction)
    {
        if (!Quiz.Room.TreasureRooms.Any())
        {
            // looting phase probably has ended already
            return;
        }

        var player = Quiz.Room.Players.SingleOrDefault(x => x.Id == session.Player.Id) ??
                     Quiz.Room.Spectators.Single(x => x.Id == session.Player.Id);

        bool alreadyInTheRoom =
            player.LootingInfo.X == treasureRoomCoords.X && player.LootingInfo.Y == treasureRoomCoords.Y;
        if (alreadyInTheRoom)
        {
            TypedQuizHub.ReceiveUpdatePlayerLootingInfo(new[] { session.Player.Id }, player.Id, player.LootingInfo,
                true);
            return;
        }

        var currentTreasureRoom =
            Quiz.Room.TreasureRooms[player.LootingInfo.TreasureRoomCoords.X][
                player.LootingInfo.TreasureRoomCoords.Y];
        var newTreasureRoom =
            Quiz.Room.TreasureRooms[treasureRoomCoords.X][treasureRoomCoords.Y];

        if (treasureRoomCoords.X < Quiz.QuizState.LootingGridSize &&
            treasureRoomCoords.Y < Quiz.QuizState.LootingGridSize)
        {
            if (currentTreasureRoom.Exits.ContainsValue(treasureRoomCoords))
            {
                player.LootingInfo.TreasureRoomCoords = treasureRoomCoords;

                int newX = player.LootingInfo.X;
                int newY = player.LootingInfo.Y;
                switch (direction)
                {
                    case Direction.North:
                    case Direction.South:
                        newY = Math.Clamp(LootingConstants.TreasureRoomHeight - player.LootingInfo.Y, 0,
                            LootingConstants.TreasureRoomHeight - LootingConstants.PlayerAvatarSize);
                        break;
                    case Direction.East:
                    case Direction.West:
                        newX = Math.Clamp(LootingConstants.TreasureRoomWidth - player.LootingInfo.X, 0,
                            LootingConstants.TreasureRoomWidth - LootingConstants.PlayerAvatarSize);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(direction), direction, null);
                }

                player.LootingInfo.X = Math.Clamp(newX, 0,
                    LootingConstants.TreasureRoomWidth - LootingConstants.PlayerAvatarSize);
                player.LootingInfo.Y = Math.Clamp(newY, 0,
                    LootingConstants.TreasureRoomHeight - LootingConstants.PlayerAvatarSize);

                TypedQuizHub.ReceiveUpdateTreasureRoom(new[] { session.Player.Id }, newTreasureRoom);

                TypedQuizHub.ReceiveUpdateRemainingMs(new[] { session.Player.Id }, Quiz.QuizState.RemainingMs);

                TypedQuizHub.ReceiveUpdatePlayerLootingInfo(new[] { session.Player.Id }, player.Id, player.LootingInfo,
                    true);

                TypedQuizHub.ReceiveUpdatePlayerLootingInfo(
                    Quiz.Room.Players.Where(x => x.Id != session.Player.Id).Select(y => y.Id),
                    player.Id, player.LootingInfo with { Inventory = new List<Treasure>() }, true);
            }
            else
            {
                Quiz.Room.Log(
                    $"Failed to use non-existing exit {player.LootingInfo.TreasureRoomCoords.X},{player.LootingInfo.TreasureRoomCoords.Y} -> " +
                    $"{treasureRoomCoords.X},{treasureRoomCoords.Y}", player.Id);
                // Console.WriteLine(JsonSerializer.Serialize(Quiz.Room.TreasureRooms[player.LootingInfo.TreasureRoomCoords.X][player.LootingInfo.TreasureRoomCoords.Y].Exits));
            }
        }
        else
        {
            Quiz.Room.Log($"Failed to move to non-existing treasure room {treasureRoomCoords}", player.Id);
        }
    }

    public async Task OnSendToggleSkip(string connectionId, int playerId)
    {
        if (Quiz.QuizState.QuizStatus == QuizStatus.Playing &&
            Quiz.QuizState.RemainingMs > 2000 &&
            Quiz.QuizState.Phase is QuizPhaseKind.Guess or QuizPhaseKind.Results &&
            !Quiz.QuizState.IsPaused)
        {
            var player = Quiz.Room.Players.Single(x => x.Id == playerId);
            if (player.IsSkipping)
            {
                player.IsSkipping = false;
            }
            else
            {
                player.IsSkipping = true;
            }

            TypedQuizHub.ReceiveUpdateRoom(Quiz.Room.Players.Concat(Quiz.Room.Spectators).Select(x => x.Id), Quiz.Room,
                false);
            await TriggerSkipIfNecessary();
        }
    }

    private async Task TriggerSkipIfNecessary()
    {
        var activeSessions = ServerState.Sessions.Where(x => Quiz.Room.Players.Any(y => y.Id == x.Player.Id))
            .Where(x => x.Player.HasActiveConnection).ToList();
        int isSkippingCount = activeSessions.Count(x => x.Player.IsSkipping);

        const float skipConst = 0.8f;
        int skipNumber = (int)Math.Round(activeSessions.Count * skipConst, MidpointRounding.AwayFromZero);

        Quiz.Room.Log($"isSkippingCount: {isSkippingCount}/{skipNumber}", writeToConsole: false);
        if (isSkippingCount >= skipNumber)
        {
            if (Quiz.QuizState.Phase is QuizPhaseKind.Guess)
            {
                bool everyoneAnsweredOrIsSkipping = true;
                foreach (Session session in activeSessions)
                {
                    if (!session.Player.IsSkipping) // don't need to check their guesses if they are skipping
                    {
                        bool answeredAllTypes = true;
                        foreach ((GuessKind key, bool value) in Quiz.Room.QuizSettings.EnabledGuessKinds)
                        {
                            if (value)
                            {
                                answeredAllTypes &= key switch
                                {
                                    GuessKind.Mst => !string.IsNullOrWhiteSpace(session.Player.Guess?.Mst),
                                    GuessKind.A => !string.IsNullOrWhiteSpace(session.Player.Guess?.A),
                                    GuessKind.Mt => !string.IsNullOrWhiteSpace(session.Player.Guess?.Mt),
                                    GuessKind.Rigger => !string.IsNullOrWhiteSpace(session.Player.Guess?.Rigger),
                                    GuessKind.Developer => !string.IsNullOrWhiteSpace(session.Player.Guess?.Developer),
                                    _ => throw new ArgumentOutOfRangeException()
                                };
                            }
                        }

                        everyoneAnsweredOrIsSkipping &= answeredAllTypes;
                    }
                }

                if (!everyoneAnsweredOrIsSkipping)
                {
                    Quiz.Room.Log("not skipping because not everyone (answered || wants to skip)",
                        writeToConsole: false);
                    return;
                }
            }

            Quiz.QuizState.RemainingMs = 500;
            Quiz.QuizState.ExtraInfo = "Skipping...";
            Quiz.Room.Log($"Skipping...");

            foreach (Player p in Quiz.Room.Players)
            {
                p.IsSkipping = false;
            }

            TypedQuizHub.ReceiveUpdateRoom(Quiz.Room.Players.Concat(Quiz.Room.Spectators).Select(x => x.Id), Quiz.Room,
                false);
        }
    }

    public async Task OnConnectedAsync(int playerId, string connectionId)
    {
        if (Quiz.QuizState.QuizStatus is QuizStatus.Playing)
        {
            await OnSendPlayerIsBuffered(playerId, "OnConnectedAsync");
        }

        // todo
        // HubContext.Clients.Clients(connectionId)
        //     .SendAsync("ReceiveRequestPlayerStatus");

        // HubContext.Clients.Client(oldConnectionId)
        //     .SendAsync("ReceiveDisconnectSelf"); // todo should be on room page too
    }

    private static Dictionary<int, List<Label>> GetPlayerLabelsForSong(Song song,
        Dictionary<int, PlayerVndbInfo> vndbInfos)
    {
        // todo? this could be written in a more efficient (batched) manner
        Dictionary<int, List<Label>> playerLabels = new();
        foreach ((int playerId, PlayerVndbInfo? vndbInfo) in vndbInfos)
        {
            if (vndbInfo.Labels != null)
            {
                playerLabels[playerId] = new List<Label>();
                foreach (Label label in vndbInfo.Labels.Where(x => x.Kind == LabelKind.Include))
                {
                    var currentSongSourceVndbUrls = song.Sources
                        .SelectMany(x => x.Links.Where(y => y.Type == SongSourceLinkType.VNDB))
                        .Select(z => z.Url)
                        .ToList();

                    if (currentSongSourceVndbUrls.Any(x => label.VNs.ContainsKey(x)))
                    {
                        // todo? add preference for showing private labels as is
                        if (label.IsPrivate)
                        {
                            var newLabel = new Label
                            {
                                Id = -1,
                                IsPrivate = true,
                                Name = "Private Label",
                                VNs = label.VNs.Where(x => currentSongSourceVndbUrls.Contains(x.Key))
                                    .ToDictionary(x => x.Key, x => x.Value),
                                Kind = label.Kind
                            };
                            playerLabels[playerId].Add(newLabel);
                        }
                        else
                        {
                            var newLabel = new Label
                            {
                                Id = label.Id,
                                IsPrivate = label.IsPrivate,
                                Name = label.Name,
                                VNs = label.VNs.Where(x => currentSongSourceVndbUrls.Contains(x.Key))
                                    .ToDictionary(x => x.Key, x => x.Value),
                                Kind = label.Kind
                            };
                            playerLabels[playerId].Add(newLabel);
                        }
                    }
                }
            }
        }

        return playerLabels;
    }

    public static async Task<(UserSpacedRepetition previous, UserSpacedRepetition current)> DoSpacedRepetition(
        int userId, int musicId, bool isCorrect)
    {
        var previous = await DbManager.GetPreviousSpacedRepetitionInfo(userId, musicId) ??
                       new UserSpacedRepetition();

        // Very rough implementation of an earliness nerf,
        // which is necessary in EMQ's case as most songs will be reviewed very early most of the time.
        // We don't care about lateness.
        float previousDays = previous.interval_days; // backup old state for UI
        float earlyDays = (float)(previous.due_at - DateTime.UtcNow).TotalDays;
        if (earlyDays > 1)
        {
            previous.interval_days /= 1.5f;
        }

        var current = previous.DoSM2(isCorrect);
        current.user_id = userId;
        current.music_id = musicId;
        current.reviewed_at = DateTime.UtcNow;
        current.due_at = DateTime.UtcNow.AddHours(current.interval_days * 24);

        previous.interval_days = previousDays; // restore old state for UI
        bool success = await DbManager.UpsertEntity(current);
        return (previous, current);
    }
}
