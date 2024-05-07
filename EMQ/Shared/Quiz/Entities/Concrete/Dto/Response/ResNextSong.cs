﻿namespace EMQ.Shared.Quiz.Entities.Concrete.Dto.Response;

public class ResNextSong
{
    public ResNextSong(int songIndex, string url, int startTime, string screenshotUrl, string coverUrl, SongHint hint)
    {
        SongIndex = songIndex;
        Url = url;
        StartTime = startTime;
        ScreenshotUrl = screenshotUrl;
        CoverUrl = coverUrl;
        Hint = hint;
    }

    public int SongIndex { get; }

    public string Url { get; }

    public int StartTime { get; }

    public string ScreenshotUrl { get; }

    public string CoverUrl { get; }

    public SongHint Hint { get; set; }
}
