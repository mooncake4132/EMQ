﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using EMQ.Shared;
using EMQ.Shared.Auth;
using EMQ.Shared.Quiz;
using EMQ.Shared.Quiz.Entities.Concrete;
using EMQ.Shared.Quiz.Entities.Concrete.Dto.Request;
using EMQ.Shared.Quiz.Entities.Concrete.Dto.Response;
using EMQ.Server.Business;
using EMQ.Server.Db;
using EMQ.Server.Hubs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace EMQ.Server.Controllers;

[ApiController]
[Route("[controller]")]
public class QuizController : ControllerBase
{
    private readonly ILogger<QuizController> _logger;
    private readonly IHubContext<QuizHub> _hubContext;

    public QuizController(ILogger<QuizController> logger, IHubContext<QuizHub> hubContext)
    {
        _logger = logger;
        _hubContext = hubContext;
    }

    [HttpPost]
    [Route("SyncRoom")]
    public Room? SyncRoom([FromBody] int roomId)
    {
        var room = ServerState.Rooms.SingleOrDefault(x => x.Id == roomId);
        if (room is null)
        {
            _logger.LogError("Room not found: " + roomId);
            return null;
        }

        return room;
    }

    [HttpPost]
    [Route("NextSong")]
    public ResNextSong? NextSong([FromBody] ReqNextSong req)
    {
        // todo? verify user belongs in room
        var room = ServerState.Rooms.SingleOrDefault(x => x.Id == req.RoomId);

        if (room is not null)
        {
            if (room.Quiz != null)
            {
                if (req.SongIndex <= room.Quiz.QuizState.sp + room.Quiz.Room.QuizSettings.PreloadAmount)
                {
                    if (req.SongIndex < room.Quiz.Songs.Count)
                    {
                        var song = room.Quiz.Songs[req.SongIndex];
                        string url = song.Links.First().Url; // todo link selection
                        return new ResNextSong(req.SongIndex, url, song.StartTime);
                    }
                    else
                    {
                        _logger.LogError("Requested song index is invalid: " + req.SongIndex);
                        return null;
                    }
                }
                else
                {
                    _logger.LogError("Requested song index is too far in the future: " + req.SongIndex);
                    return null;
                }
            }
            else
            {
                _logger.LogError("Room does not have a quiz initialized: " + req.RoomId);
                return null;
            }
        }
        else
        {
            _logger.LogError("Room not found: " + req.RoomId);
            return null;
        }
    }

    [HttpGet]
    [Route("GetRooms")]
    public async Task<IEnumerable<Room>> GetRooms()
    {
        // todo
        // return ServerState.Rooms.Where(x => x.Quiz is null ||
        //                                     x.Quiz?.QuizState.QuizStatus is QuizStatus.Starting or QuizStatus.Playing);

        return ServerState.Rooms;
    }

    [HttpPost]
    [Route("CreateRoom")]
    public async Task<int> CreateRoom([FromBody] ReqCreateRoom req)
    {
        var session = ServerState.Sessions.Find(x => x.Player.Id == req.PlayerId);
        if (session is null)
        {
            // todo
            throw new Exception();
        }

        var owner = session.Player;
        var room = new Room(new Random().Next(), req.Name, owner)
        {
            Password = req.Password, QuizSettings = req.QuizSettings
        };
        ServerState.Rooms.Add(room);
        _logger.LogInformation("Created room " + room.Id);

        return room.Id;
    }

    // todo decouple joining room from joining quiz maybe?
    [HttpPost]
    [Route("JoinRoom")]
    public async Task JoinRoom([FromBody] ReqJoinRoom req)
    {
        var room = ServerState.Rooms.Find(x => x.Id == req.RoomId);
        var session = ServerState.Sessions.Find(x => x.Player.Id == req.PlayerId);

        if (room is null || session is null)
        {
            // todo warn error
            throw new Exception();
        }

        var player = session.Player;
        if (room.Password == req.Password)
        {
            // todo check if player is already in the same room?
            if (!room.Players.Any(x => x.Id == req.PlayerId))
            {
                var oldRoom = ServerState.Rooms.Find(x => x.Players.Any(y => y.Id == req.PlayerId));
                if (oldRoom is not null)
                {
                    _logger.LogInformation($"Removed player {req.PlayerId} from room " + oldRoom.Id);
                    oldRoom.Players.RemoveAll(x => x.Id == player.Id);
                }

                _logger.LogInformation($"Added player {req.PlayerId} to room " + room.Id);
                room.Players.Add(player);
                // _logger.LogInformation("cnnid: " + session.ConnectionId!);
                room.AllPlayerConnectionIds.Add(session.ConnectionId!);

                // let every other player in the room know that a new player joined,
                // we can't send this message to the joining player because their room page hasn't initialized yet
                await _hubContext.Clients.Clients(room.AllPlayerConnectionIds.Where(x => x != session.ConnectionId))
                    .SendAsync("ReceivePlayerJoinedRoom");
            }
            else
            {
                // todo warn error
            }
        }
        else
        {
            // todo warn wrong password
        }
    }

    [HttpPost]
    [Route("StartQuiz")]
    public async Task StartQuiz([FromBody] ReqStartQuiz req)
    {
        var session = ServerState.Sessions.Find(x => x.Player.Id == req.PlayerId);
        if (session is null)
        {
            // todo
            throw new Exception();
        }

        var player = session.Player;
        var room = ServerState.Rooms.Find(x => x.Id == req.RoomId);

        if (room is not null)
        {
            if (room.Owner.Id == player.Id)
            {
                var quiz = new Quiz(room, new Random().Next());
                room.Quiz = quiz;
                var quizManager = new QuizManager(quiz, _hubContext);
                ServerState.QuizManagers.Add(quizManager);
                _logger.LogInformation("Created quiz " + quiz.Id);

                if (await quizManager.PrimeQuiz())
                {
                    _logger.LogInformation("Primed quiz {quiz.Id}", quiz.Id);
                }
                else
                {
                    _logger.LogInformation("Failed to prime quiz {quiz.Id} - canceling", quiz.Id);
                    // todo cancel quiz and return to room
                }
            }
            else
            {
                _logger.LogInformation("Attempt to start quiz in room {room.Id} by non-owner player {req.playerId}",
                    room.Id, req.PlayerId);
                // todo warn not owner
            }
        }
        else
        {
            _logger.LogInformation("Attempt to start quiz in room {req.RoomId} that is null", req.RoomId);
            // todo
        }
    }
}
