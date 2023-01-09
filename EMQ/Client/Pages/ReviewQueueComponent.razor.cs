﻿using System;
using System.Collections.Generic;
using System.Net.Http.Json;
using System.Threading.Tasks;
using EMQ.Shared.Library.Entities.Concrete.Dto.Request;
using EMQ.Shared.Quiz.Entities.Concrete;
using Microsoft.Extensions.Logging;

namespace EMQ.Client.Pages;

public partial class ReviewQueueComponent
{
    public List<RQ> CurrentRQs { get; set; } = new();

    protected override async Task OnInitializedAsync()
    {
        await RefreshRQs();
    }

    public async Task RefreshRQs()
    {
        var req = new ReqFindRQs(DateTime.Now.AddDays(-30), DateTime.Now);
        var res = await _client.PostAsJsonAsync("Library/FindRQs", req);
        if (res.IsSuccessStatusCode)
        {
            var content = await res.Content.ReadFromJsonAsync<List<RQ>>();
            if (content is not null)
            {
                content.Reverse();
                CurrentRQs = content;
            }
            else
            {
                _logger.LogError("Failed to find RQs");
            }
        }

        StateHasChanged();
    }
}