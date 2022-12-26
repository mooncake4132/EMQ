﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using EMQ.Shared.Auth.Entities.Concrete;
using EMQ.Shared.Auth.Entities.Concrete.Dto.Request;
using EMQ.Shared.Quiz.Entities.Concrete;
using EMQ.Shared.Quiz.Entities.Concrete.Dto.Response;
using EMQ.Shared.VNDB.Business;
using Juliet.Model.VNDBObject;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace EMQ.Server.Controllers;

[ApiController]
[Route("[controller]")]
public class AuthController : ControllerBase
{
    public AuthController(ILogger<AuthController> logger)
    {
        _logger = logger;
    }

    private readonly ILogger<AuthController> _logger;

    [HttpPost]
    [Route("CreateSession")]
    public async Task<ResCreateSession> CreateSession([FromBody] ReqCreateSession req)
    {
        // todo: authenticate with db and get player
        int playerId = new Random().Next();

        List<Label>? vns = null;
        if (!string.IsNullOrWhiteSpace(req.VndbInfo.VndbId))
        {
            var labels = new List<Label>();
            VNDBLabel[] vndbLabels = await VndbMethods.GetLabels(req.VndbInfo);

            foreach (VNDBLabel vndbLabel in vndbLabels)
            {
                labels.Add(Label.FromVndbLabel(vndbLabel));
            }

            // we try including playing, finished, stalled, voted, EMQ-wl, and EMQ-bl labels by default
            foreach (Label label in labels)
            {
                switch (label.Name.ToLowerInvariant())
                {
                    case "playing":
                    case "finished":
                    case "stalled":
                    case "voted":
                    case "emq-wl":
                        label.Kind = LabelKind.Include;
                        break;
                    case "emq-bl":
                        label.Kind = LabelKind.Exclude;
                        break;
                }
            }

            vns = await VndbMethods.GrabPlayerVNsFromVndb(
                new PlayerVndbInfo()
                {
                    VndbId = req.VndbInfo.VndbId, VndbApiToken = req.VndbInfo.VndbApiToken, Labels = labels,
                }
            );
        }

        var player = new Player(playerId, req.Username) { Avatar = new Avatar(AvatarCharacter.Auu, "default"), };

        // todo: invalidate previous session with the same playerId if it exists
        string token = Guid.NewGuid().ToString();

        var session = new Session(player, token);
        session.VndbInfo = new PlayerVndbInfo()
        {
            VndbId = req.VndbInfo.VndbId, VndbApiToken = req.VndbInfo.VndbApiToken, Labels = vns
        };
        ServerState.Sessions.Add(session);
        _logger.LogInformation("Created new session for player " + player.Id + $" ({session.VndbInfo.VndbId})");

        return new ResCreateSession(session);
    }

    [HttpPost]
    [Route("RemoveSession")]
    public async Task RemoveSession([FromBody] ReqRemoveSession req)
    {
        var session = ServerState.Sessions.Single(x => x.Token == req.Token);
        _logger.LogInformation("Removing session " + session.Token);
        ServerState.Sessions.Remove(session);
        // todo notify room?
    }

    [HttpPost]
    [Route("UpdateLabel")]
    public async Task<Label> UpdateLabel([FromBody] ReqUpdateLabel req)
    {
        var session = ServerState.Sessions.Single(x => x.Token == req.Token);
        if (session.VndbInfo.Labels != null)
        {
            var label = session.VndbInfo.Labels.FirstOrDefault(x => x.Id == req.Label.Id);
            if (label == null)
            {
                label = req.Label;
                session.VndbInfo.Labels.Add(label);
            }

            switch (req.Label.Kind)
            {
                case LabelKind.Ignore:
                    label.Kind = req.Label.Kind;
                    label.VnUrls = new List<string>();
                    break;
                case LabelKind.Include:
                case LabelKind.Exclude:
                    Console.WriteLine($"{session.VndbInfo.VndbId}: " + label.Id + ", " + label.Kind + " => " +
                                      req.Label.Kind);
                    label.Kind = req.Label.Kind;
                    var grabbed = await VndbMethods.GrabPlayerVNsFromVndb(new PlayerVndbInfo()
                    {
                        VndbId = session.VndbInfo.VndbId,
                        VndbApiToken = session.VndbInfo.VndbApiToken,
                        Labels = new List<Label>() { label },
                    });
                    label.VnUrls = grabbed.SingleOrDefault()?.VnUrls ?? new List<string>();
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            return label;
        }
        else
        {
            throw new Exception("Could not find the label to update");
        }
    }
}
