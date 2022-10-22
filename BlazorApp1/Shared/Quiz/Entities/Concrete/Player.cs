﻿namespace BlazorApp1.Shared.Quiz.Entities.Concrete;

public class Player
{
    public Player(int id, string username)
    {
        Id = id;
        Username = username;
    }

    public int Id { get; }

    public string Username { get; }

    // public string DisplayName { get; }

    public string? Guess { get; set; }

    // todo: might want to keep this within Songs instead
    public bool? IsCorrect { get; set; }

    public int Score { get; set; }
}
