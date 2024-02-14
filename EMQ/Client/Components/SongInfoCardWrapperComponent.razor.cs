﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net.Http.Json;
using System.Threading.Tasks;
using EMQ.Client.Pages;
using EMQ.Shared.Auth.Entities.Concrete.Dto.Request;
using EMQ.Shared.Core;
using EMQ.Shared.Library.Entities.Concrete.Dto.Request;
using EMQ.Shared.Mod.Entities.Concrete.Dto.Request;
using EMQ.Shared.Quiz.Entities.Concrete;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace EMQ.Client.Components;

public partial class SongInfoCardWrapperComponent
{
    [Parameter]
    public IEnumerable<Song> CurrentSongs { get; set; } = new List<Song>();

    [Parameter]
    public LibrarySongFilterKind LibrarySongFilter { get; set; }

    [Parameter]
    public string NoSongsText { get; set; } = "";

    [Parameter]
    public bool IsLibraryPage { get; set; }

    [Parameter]
    public Dictionary<int, Func<Task>>? BatchUploaderCallbacks { get; set; }

    private Dictionary<int, AddSongLinkModel> _addSongLinkModel { get; set; } = new();

    private int VisibleSongsCount { get; set; }

    private string _batchSetSubmittedByText = "";

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
                _addSongLinkModel[mId].Url = "Failed to submit."; // todo hack
                Console.WriteLine("Error importing song link");
            }
        }
        else
        {
            _addSongLinkModel[mId].Url = "Failed to submit."; // todo hack
            Console.WriteLine("Error importing song link");
        }
    }

    private async Task BatchSetSubmittedBy()
    {
        var links = CurrentSongs.SelectMany(x => x.Links.Where(y => y.SubmittedBy == "[unknown]"));
        string[] urls = links.Select(x => x.Url).ToArray();

        var req = new ReqSetSubmittedBy(urls, _batchSetSubmittedByText);
        var res = await _client.PostAsJsonAsync("Mod/SetSubmittedBy", req);
        if (!res.IsSuccessStatusCode)
        {
            throw new Exception();
        }
    }

    private async Task DeleteSongLink(int mId, string url)
    {
        // todo? generic confirm modal
        bool confirmed = await _jsRuntime.InvokeAsync<bool>("confirm", $"Really delete {url}?");
        if (!confirmed)
        {
            return;
        }

        var req = new ReqDeleteSongLink(mId, url);
        var res = await _client.PostAsJsonAsync("Mod/DeleteSongLink", req);
        if (!res.IsSuccessStatusCode)
        {
            throw new Exception();
        }

        int rowsDeleted = await res.Content.ReadFromJsonAsync<int>();
        if (rowsDeleted > 0)
        {
            var song = CurrentSongs.Single(x => x.Id == mId);
            song.Links.RemoveAll(x => x.Url == url);
        }
        else
        {
            await _jsRuntime.InvokeVoidAsync("alert", $"Error deleting {url}");
        }
    }

    private async Task CallStateHasChanged()
    {
        StateHasChanged();
    }
}

public class AddSongLinkModel
{
    [Required]
    [RegularExpression(RegexPatterns.CatboxRegex, ErrorMessage = "Invalid Url")]
    public string Url { get; set; } = "";
}
