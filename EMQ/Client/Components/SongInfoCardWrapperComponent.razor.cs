﻿using System;
using System.Collections.Generic;
using System.Net.Http.Json;
using System.Threading.Tasks;
using EMQ.Client.Pages;
using EMQ.Shared.Core;
using EMQ.Shared.Library.Entities.Concrete.Dto.Request;
using EMQ.Shared.Quiz.Entities.Concrete;
using Microsoft.AspNetCore.Components;

namespace EMQ.Client.Components;

public partial class SongInfoCardWrapperComponent
{
    [Parameter]
    public IEnumerable<Song> CurrentSongs { get; set; } = new List<Song>();

    [Parameter]
    public LibrarySongFilterKind LibrarySongFilter { get; set; }

    [Parameter]
    public string NoSongsText { get; set; } = "";

    private Dictionary<int, AddSongLinkModel> _addSongLinkModel { get; set; } = new();

    private int VisibleSongsCount { get; set; }

    private async Task SubmitSongUrl(int mId, string url)
    {
        if (ClientState.Session?.Player.Username is null)
        {
            return;
        }

        _addSongLinkModel[mId].Url = "";
        StateHasChanged();

        url = url.Trim().ToLowerInvariant();
        bool isVideo = url.IsVideoLink();
        SongLinkType songLinkType = url.Contains("catbox") ? SongLinkType.Catbox : SongLinkType.Unknown;

        string submittedBy = ClientState.Session.Player.Username;
        var req = new ReqImportSongLink(mId,
            new SongLink() { Url = url, IsVideo = isVideo, Type = songLinkType, SubmittedBy = submittedBy });
        var res = await _client.PostAsJsonAsync("Library/ImportSongLink", req);
        if (res.IsSuccessStatusCode)
        {
            var isSuccess = await res.Content.ReadFromJsonAsync<bool>();
            if (isSuccess)
            {
                Console.WriteLine("Imported song link!");
                // await _reviewQueueComponent!.RefreshRQs(); // todo
            }
            else
            {
                // todo show error
                Console.WriteLine("Error importing song link");
            }
        }
    }
}