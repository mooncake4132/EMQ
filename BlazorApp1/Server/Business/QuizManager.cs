﻿using System;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using BlazorApp1.Server.Hubs;
using BlazorApp1.Shared.Quiz.Entities.Concrete;
using Microsoft.AspNetCore.SignalR;

namespace BlazorApp1.Server.Business;

public class QuizManager
{
    public QuizManager(Quiz quiz, IHubContext<QuizHub> hubContext)
    {
        Quiz = quiz;
        HubContext = hubContext;
    }

    public Quiz Quiz { get; }

    private IHubContext<QuizHub> HubContext { get; }

    private string[] AllPlayerConnectionIds =>
        ServerState.Sessions.Where(x => Quiz.Room.Players.Select(y => y.Id).Contains(x.PlayerId))
            .Select(x => x.ConnectionId!).ToArray();

    private void SetTimer()
    {
        Quiz.Timer.Stop();
        Quiz.Timer.Elapsed -= OnTimedEvent;

        Quiz.Timer.Interval = TimeSpan.FromSeconds(1).TotalMilliseconds;
        Quiz.Timer.Elapsed += OnTimedEvent;
        Quiz.Timer.AutoReset = true;
        Quiz.Timer.Start();
    }

    private async void OnTimedEvent(object? sender, ElapsedEventArgs e)
    {
        if (Quiz.QuizState.IsActive)
        {
            if (Quiz.QuizState.RemainingSeconds >= 0)
            {
                Quiz.QuizState.RemainingSeconds -= 1;
            }

            if (Quiz.QuizState.RemainingSeconds < 0)
            {
                Quiz.Timer.Stop();

                switch (Quiz.QuizState.Phase.Kind)
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
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                Quiz.Timer.Start();
            }
        }
    }

    private async Task EnterGuessingPhase()
    {
        Quiz.QuizState.Phase = new GuessPhase();
        Quiz.QuizState.RemainingSeconds = Quiz.QuizSettings.GuessTime;
        Quiz.QuizState.sp += 1;
        await HubContext.Clients.Clients(AllPlayerConnectionIds)
            .SendAsync("ReceivePhaseChanged", Quiz.QuizState.Phase.Kind);
    }

    private async Task EnterJudgementPhase()
    {
        Quiz.QuizState.Phase = new JudgementPhase();
        await HubContext.Clients.Clients(AllPlayerConnectionIds)
            .SendAsync("ReceivePhaseChanged", Quiz.QuizState.Phase.Kind);
        await JudgeGuesses();
    }

    private async Task EnterResultsPhase()
    {
        Quiz.QuizState.Phase = new ResultsPhase();
        Quiz.QuizState.RemainingSeconds = Quiz.QuizSettings.ResultsTime;
        await HubContext.Clients.Clients(AllPlayerConnectionIds)
            .SendAsync("ReceivePhaseChanged", Quiz.QuizState.Phase.Kind);

        if (Quiz.QuizState.sp + 1 == Quiz.Songs.Count)
        {
            await EndQuiz();
        }
    }

    private async Task JudgeGuesses()
    {
        await Task.Delay(3000);
    }

    public async Task EndQuiz()
    {
        // todo other cleanup
        Quiz.QuizState.IsActive = false;
        Quiz.Timer.Stop();
        Quiz.Timer.Elapsed -= OnTimedEvent;

        await HubContext.Clients.Clients(AllPlayerConnectionIds)
            .SendAsync("ReceiveQuizEnded", Quiz.QuizState.IsActive);
    }

    public async Task StartQuiz()
    {
        Quiz.QuizState.IsActive = true;

        await HubContext.Clients.Clients(AllPlayerConnectionIds)
            .SendAsync("ReceiveQuizStarted", Quiz.QuizState.IsActive);
        await EnterGuessingPhase();
        SetTimer();
    }

    public async Task OnSendPlayerJoinedQuiz(string connectionId)
    {
        // TODO: only start quiz if all? players ready
        if (!Quiz.QuizState.IsActive) // todo: && !quizEnded
        {
            await StartQuiz();
        }
        else if (true) // todo: && !quizEnded
        {
            await HubContext.Clients.Clients(connectionId)
                .SendAsync("ReceiveQuizStarted", Quiz.QuizState.IsActive);
        }
        else
        {
            // todo warn quiz is already over
        }
    }

    public async Task OnSendGuessChanged(int playerId, string guess)
    {
        var player = Quiz.Room.Players.Find(x => x.Id == playerId);
        if (player != null)
        {
            player.Guess = guess;
        }
        else
        {
            // todo log invalid guess submitted
        }
    }
}
