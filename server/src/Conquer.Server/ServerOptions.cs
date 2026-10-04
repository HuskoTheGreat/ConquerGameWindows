namespace Conquer.Server
{
    /// <summary>
    /// Every cap here exists to keep one person from filling a small VM. Defaults suit an Oracle Always Free
    /// instance; override any of them in appsettings.json under "Conquer" or with CONQUER__NAME environment variables.
    /// </summary>
    public sealed class ServerOptions
    {
        /// <summary>Kestrel listens here. Keep it on loopback: Caddy terminates TLS on 443 and proxies in.</summary>
        public string ListenUrl { get; set; } = "http://127.0.0.1:5080";

        /// <summary>Trust X-Forwarded-For from the loopback proxy only, so per-IP limits can't be dodged.</summary>
        public bool BehindProxy { get; set; } = true;

        public int MaxConnections { get; set; } = 200;
        public int MaxConnectionsPerIp { get; set; } = 6;
        public int MaxRooms { get; set; } = 40;
        public int MaxRoomsPerIp { get; set; } = 2;

        /// <summary>Largest frame a client may send. The biggest real message (a chat line) is under 1 KB.</summary>
        public int MaxMessageBytes { get; set; } = 2048;

        /// <summary>Frames per second per connection before strikes start (the session has finer limits).</summary>
        public double FramesPerSecond { get; set; } = 20;
        public int FrameBurst { get; set; } = 40;

        /// <summary>Create/join attempts per IP: a slow refill so room codes can't be guessed.</summary>
        public double JoinAttemptsPerMinute { get; set; } = 10;
        public int JoinAttemptBurst { get; set; } = 6;

        /// <summary>Clients send a heartbeat; a connection silent this long is treated as dead and its seat freed.</summary>
        public int IdleTimeoutSeconds { get; set; } = 40;

        /// <summary>Frames queued for a slow client before it is dropped rather than buffered without limit.</summary>
        public int SendQueueLength { get; set; } = 64;

        /// <summary>Bigger boards mean bigger snapshots every move; 3 rings (37 tiles) is plenty for 6 players.</summary>
        public int MaxBoardRadius { get; set; } = 3;

        /// <summary>Pause before each computer-player move, so people can follow what it did.</summary>
        public int BotMoveDelayMs { get; set; } = 900;

        public int EmptyLobbyMinutes { get; set; } = 5;
        public int EmptyGameMinutes { get; set; } = 15;
        public int FinishedGameMinutes { get; set; } = 10;
        public int MaxRoomHours { get; set; } = 8;

        public BotOptions Bots { get; set; } = new BotOptions();
    }

    public sealed class BotOptions
    {
        /// <summary>Off by default so the server runs fine without a model. The game never waits on a bot.</summary>
        public bool Enabled { get; set; }

        /// <summary>Any OpenAI-compatible chat endpoint: llama.cpp's llama-server, Ollama, LocalAI, vLLM...</summary>
        public string BaseUrl { get; set; } = "http://127.0.0.1:8081";
        public string Model { get; set; } = "local";
        public string ApiKey { get; set; } = "";

        public int MaxTokens { get; set; } = 60;
        public double Temperature { get; set; } = 0.8;
        public int TimeoutSeconds { get; set; } = 20;

        /// <summary>Requests waiting for the model across all rooms. Extra requests are dropped, not queued.</summary>
        public int QueueLength { get; set; } = 4;

        /// <summary>Global ceiling on model calls, so a busy server can't pin the CPU.</summary>
        public int MaxCallsPerMinute { get; set; } = 12;

        /// <summary>Per room: minimum gap between unprompted comments, and between replies to players.</summary>
        public int CommentaryCooldownSeconds { get; set; } = 45;
        public int ReplyCooldownSeconds { get; set; } = 8;

        /// <summary>At most this many bots per room.</summary>
        public int MaxBotsPerRoom { get; set; } = 2;

        public BotPersona[] Personas { get; set; } =
        {
            new BotPersona
            {
                Name = "Captain Clay",
                Style = "a cheerful old sea captain who loves ports and clay, speaks with nautical slang",
            },
            new BotPersona
            {
                Name = "Professor Hex",
                Style = "a dry, witty statistician who comments on dice odds and probability",
            },
            new BotPersona
            {
                Name = "Steady Sam",
                Style = "a nervous shepherd who is always worried about the raider and loves livestock",
            },
        };
    }

    public sealed class BotPersona
    {
        public string Name { get; set; } = "";
        public string Style { get; set; } = "";
    }
}
