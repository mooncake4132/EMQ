﻿using System.ComponentModel.DataAnnotations;

namespace EMQ.Shared.Quiz.Entities.Concrete;

public enum SongDifficultyLevel
{
    [Range(70.01d, 100d)]
    [Display(Name = "Very Easy")]
    VeryEasy,

    [Range(50.01d, 70d)]
    [Display(Name = "Easy")]
    Easy,

    [Range(30.01d, 50d)]
    [Display(Name = "Medium")]
    Medium,

    [Range(15.01d, 30d)]
    [Display(Name = "Hard")]
    Hard,

    [Range(0.01d, 15d)]
    [Display(Name = "Very Hard")]
    VeryHard,

    [Range(0d, 0d)]
    [Display(Name = "Impossible")]
    Impossible
}
