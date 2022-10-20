﻿using System.Text.Json.Serialization;
using BlazorApp1.Shared.Quiz.Abstract;
using BlazorApp1.Shared.Quiz.Concrete;

namespace BlazorApp1.Shared.Quiz;

public class QuizState
{
    public bool IsActive { get; set; }

    [JsonIgnore] public IQuizPhase Phase { get; set; } = new GuessPhase();

    // public int ElapsedSeconds { get; set; }

    /// <summary>
    ///  The remaining time for current phase in seconds
    /// </summary>
    public int RemainingSeconds { get; set; }

    /// <summary>
    ///  "Song Pointer" (a.k.a. Current Song Index)
    /// </summary>
    public int sp { get; set; } = -1;

    public int NumSongs { get; set; } = 9; // todo hook this up
}
