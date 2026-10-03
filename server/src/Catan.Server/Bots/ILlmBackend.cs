using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Catan.Server.Bots
{
    public readonly struct ChatTurn
    {
        public readonly string Role; // "system", "user" or "assistant"
        public readonly string Content;

        public ChatTurn(string role, string content)
        {
            Role = role;
            Content = content;
        }
    }

    /// <summary>
    /// Where bot lines come from. The default talks to any OpenAI-compatible server (llama.cpp, Ollama); swap in
    /// another implementation to use a different runtime. Returns null when there is nothing usable to say.
    /// </summary>
    public interface ILlmBackend
    {
        Task<string> CompleteAsync(IReadOnlyList<ChatTurn> messages, CancellationToken ct);
    }
}
