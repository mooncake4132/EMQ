﻿using System.Collections.Generic;

namespace EMQ.Shared.Quiz.Entities.Concrete;

public class ServerActivityStats
{
    public Dictionary<string, ServerActivityStatsDailyPlayers> DailyPlayers { get; set; } = new();
}

public class ServerActivityStatsDailyPlayers
{
    public int Users { get; set; }

    public int Guests { get; set; }
}
