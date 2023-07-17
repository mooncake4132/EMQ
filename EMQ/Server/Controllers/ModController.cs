﻿using System;
using System.Runtime;
using System.Threading.Tasks;
using EMQ.Server.Db;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace EMQ.Server.Controllers;

[ApiController]
[Route("[controller]")]
public class ModController : ControllerBase
{
    public ModController(ILogger<ModController> logger)
    {
        _logger = logger;
    }

    private readonly ILogger<ModController> _logger;

    [HttpGet]
    [Route("ExportSongLite")]
    public async Task<ActionResult<string>> ExportSongLite([FromQuery] string adminPassword)
    {
        string? envVar = Environment.GetEnvironmentVariable("EMQ_ADMIN_PASSWORD");
        if (string.IsNullOrWhiteSpace(envVar) || envVar != adminPassword)
        {
            _logger.LogInformation("Rejected ExportSongLite request");
            return Unauthorized();
        }

        _logger.LogInformation("Approved ExportSongLite request");
        string songLite = await DbManager.ExportSongLite();
        return songLite;
    }

    [HttpPost]
    [Route("RunGc")]
    public async Task<ActionResult> RunGc([FromBody] string adminPassword)
    {
        string? envVar = Environment.GetEnvironmentVariable("EMQ_ADMIN_PASSWORD");
        if (string.IsNullOrWhiteSpace(envVar) || envVar != adminPassword)
        {
            _logger.LogInformation("Rejected RunGc request");
            return Unauthorized();
        }

        long before = GC.GetTotalMemory(false);
        _logger.LogInformation("Running GC");
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true, true);
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true, true);
        long after = GC.GetTotalMemory(false);
        _logger.LogInformation($"GC freed {(before - after) / 1000 / 1000} MB");

        return Ok();
    }
}
