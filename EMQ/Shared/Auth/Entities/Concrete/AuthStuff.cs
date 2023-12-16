﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace EMQ.Shared.Auth.Entities.Concrete;

public static class AuthStuff // todo? find better name
{
    public const UserRoleKind LowestModeratorRole = UserRoleKind.ChatModerator; // todo? get rid of this

    public static readonly string AuthorizationHeaderName = HttpRequestHeader.Authorization.ToString();

    public static PermissionKind[] DefaultVisitorPermissions { get; } =
    {
        PermissionKind.Visitor, PermissionKind.Login, PermissionKind.SearchLibrary, PermissionKind.ViewStats
    };

    public static PermissionKind[] DefaultGuestPermissions { get; } =
        DefaultVisitorPermissions.Concat(new[]
        {
            PermissionKind.Guest, PermissionKind.CreateRoom, PermissionKind.PlayQuiz,
            PermissionKind.SendChatMessage, PermissionKind.UpdatePreferences
        }).ToArray();

    public static PermissionKind[] DefaultUserPermissions { get; } =
        DefaultGuestPermissions.Concat(new[]
        {
            PermissionKind.User, PermissionKind.JoinRanked, PermissionKind.UploadSongLink,
            PermissionKind.ReportSongLink
        }).ToArray();

    public static PermissionKind[] DefaultModeratorPermissions { get; } =
        DefaultUserPermissions.Concat(new[] { PermissionKind.Moderator }).ToArray();

    public static PermissionKind[] DefaultChatModeratorPermissions { get; } =
        DefaultModeratorPermissions.Concat(new[] { PermissionKind.ModerateChat }).ToArray();

    public static PermissionKind[] DefaultReviewQueueModeratorPermissions { get; } =
        DefaultModeratorPermissions.Concat(new[] { PermissionKind.ReviewSongLink }).ToArray();

    public static PermissionKind[] DefaultDatabaseModeratorPermissions { get; } =
        DefaultModeratorPermissions.Concat(new[] { PermissionKind.EditSongMetadata }).ToArray();

    public static PermissionKind[] DefaultAdminPermissions { get; } = Enum.GetValues<PermissionKind>();

    public static Dictionary<UserRoleKind, PermissionKind[]> DefaultRolePermissionsDict { get; } =
        new()
        {
            { UserRoleKind.Visitor, DefaultVisitorPermissions },
            { UserRoleKind.Guest, DefaultGuestPermissions },
            { UserRoleKind.User, DefaultUserPermissions },
            { UserRoleKind.ChatModerator, DefaultChatModeratorPermissions },
            { UserRoleKind.ReviewQueueModerator, DefaultReviewQueueModeratorPermissions },
            { UserRoleKind.DatabaseModerator, DefaultDatabaseModeratorPermissions },
            { UserRoleKind.Admin, DefaultAdminPermissions },
        };
}

[Flags]
public enum UserRoleKind
{
    Visitor = 0, // not logged in
    Guest = 1 << 0, // logged in as a temporary guest
    User = 1 << 1, // logged in as a registered user
    ChatModerator = 1 << 2,
    ReviewQueueModerator = 1 << 3,
    DatabaseModerator = 1 << 4,
    Admin = int.MaxValue,
}

public enum PermissionKind
{
    None = 0, // do not use

    Visitor = 1000,
    Login = 1001,
    SearchLibrary = 1002,
    ViewStats = 1003,

    Guest = 2000,
    CreateRoom = 2001,
    PlayQuiz = 2002,
    SendChatMessage = 2003,
    UpdatePreferences = 2004,

    User = 3000,
    JoinRanked = 3001,
    UploadSongLink = 3002,
    ReportSongLink = 3003,

    Moderator = 4000,

    ModerateChat = 5001,

    ReviewSongLink = 6001,

    EditSongMetadata = 7001,

    EditUsers = 8001,

    Admin = 9000,
}