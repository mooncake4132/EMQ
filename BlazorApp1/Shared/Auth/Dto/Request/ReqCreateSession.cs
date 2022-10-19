﻿namespace BlazorApp1.Shared.Auth.Dto.Request;

public class ReqCreateSession
{
    public ReqCreateSession(string username, string password)
    {
        Username = username;
        Password = password;
    }

    public string Username { get; }

    public string Password { get; }
}
