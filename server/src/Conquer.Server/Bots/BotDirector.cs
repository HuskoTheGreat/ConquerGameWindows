using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Conquer.Server.Bots
{
    /// <summary>
    /// Runs bot requests one at a time on a single background worker, because a small CPU model serves one
    /// prompt at a time anyway. The queue is short and drops when full, there's a global per-minute budget,
    /// and each call has a timeout: the game never waits on a bot, it just hears from them less.
    /// </summary>
    public sealed class BotDirector : BackgroundService
    {
        public sealed class Job
        {
            public BotPrompt Prompt;
            public Action<BotPersona, string> Done; // called on the worker thread; must only post to the room
        }

        readonly ILlmBackend _backend;
        readonly BotOptions _options;
        readonly ILogger<BotDirector> _log;
        readonly Channel<Job> _queue;
        readonly Func<double> _clock;
        double _windowStart;
        int _callsInWindow;

        public BotDirector(ILlmBackend backend, ServerOptions options, ILogger<BotDirector> log, Func<double> clock)
        {
            _backend = backend;
            _options = options.Bots;
            _log = log;
            _clock = clock;
            _queue = Channel.CreateBounded<Job>(new BoundedChannelOptions(Math.Max(1, _options.QueueLength))
            {
                SingleReader = true,
                FullMode = BoundedChannelFullMode.DropWrite,
            });
        }

        public bool Enabled => _options.Enabled;

        /// <summary>Never blocks. Returns false (and the caller should clear its pending flag) if dropped.</summary>
        public bool TryEnqueue(Job job) => _options.Enabled && _queue.Writer.TryWrite(job);

        protected override async Task ExecuteAsync(CancellationToken stopping)
        {
            await foreach (Job job in _queue.Reader.ReadAllAsync(stopping))
            {
                string text = null;
                if (TakeBudget())
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stopping);
                    timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
                    try
                    {
                        text = await _backend.CompleteAsync(job.Prompt.Messages, timeout.Token);
                    }
                    catch (Exception e) when (!stopping.IsCancellationRequested)
                    {
                        // Model down or slow: bots go quiet, the game carries on.
                        _log.LogDebug("Bot request failed: {Error}", e.GetType().Name);
                    }
                }
                job.Done(job.Prompt.Persona, text);
            }
        }

        bool TakeBudget()
        {
            double now = _clock();
            if (now - _windowStart >= 60)
            {
                _windowStart = now;
                _callsInWindow = 0;
            }
            if (_callsInWindow >= _options.MaxCallsPerMinute) return false;
            _callsInWindow++;
            return true;
        }
    }
}
