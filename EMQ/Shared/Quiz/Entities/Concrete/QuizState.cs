﻿using System.Text.Json.Serialization;
using EMQ.Shared.Quiz.Entities.Abstract;

namespace EMQ.Shared.Quiz.Entities.Concrete;

public class QuizState
{
    public QuizStatus QuizStatus { get; set; } = QuizStatus.Starting; // todo should this be here or on Quiz?

    [JsonIgnore]
    public IQuizPhase Phase { get; set; } = new GuessPhase();

    // public int ElapsedSeconds { get; set; }

    /// <summary>
    ///  The remaining time for current phase in milliseconds
    /// </summary>
    public float RemainingMs { get; set; }

    /// <summary>
    ///  "Song Pointer" (a.k.a. Current Song Index)
    /// </summary>
    public int sp { get; set; } = -1;

    public int NumSongs { get; set; }

    public bool IsPaused { get; set; }
}
