﻿using System;
using System.Collections.Generic;

namespace EMQ.Shared.Quiz.Entities.Concrete;

public class SongLite_MB
{
    public SongLite_MB(Guid recording, List<SongLink> links, SongStats? songStats = null)
    {
        Recording = recording;
        Links = links;
        SongStats = songStats;
    }

    public Guid Recording { get; set; }

    public List<SongLink> Links { get; set; }

    public SongStats? SongStats { get; set; }
}
