using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Conquer.Server.Bots
{
    /// <summary>
    /// POSTs to /v1/chat/completions, which llama.cpp's llama-server, Ollama, LocalAI and vLLM all serve. The
    /// model only ever sees the public game log and public chat, so whatever it says can't leak hidden cards.
    /// </summary>
    public sealed class OpenAiCompatibleBackend : ILlmBackend
    {
        const int MaxResponseBytes = 64 * 1024;
        static readonly Regex ThinkBlock = new Regex("<think>.*?(</think>|$)", RegexOptions.Singleline | RegexOptions.IgnoreCase);

        readonly HttpClient _http;
        readonly BotOptions _options;
        readonly Uri _endpoint;

        public OpenAiCompatibleBackend(HttpClient http, BotOptions options)
        {
            _http = http;
            _options = options;
            _endpoint = new Uri(new Uri(options.BaseUrl.TrimEnd('/') + "/"), "v1/chat/completions");
        }

        public async Task<string> CompleteAsync(IReadOnlyList<ChatTurn> messages, CancellationToken ct)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
            {
                Content = new ByteArrayContent(BuildBody(messages)),
            };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            if (!string.IsNullOrEmpty(_options.ApiKey))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

            using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) return null;

            await using Stream stream = await response.Content.ReadAsStreamAsync(ct);
            var buffer = new MemoryStream();
            var chunk = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(chunk, ct)) > 0)
            {
                if (buffer.Length + read > MaxResponseBytes) return null;
                buffer.Write(chunk, 0, read);
            }
            return ParseContent(buffer.ToArray());
        }

        byte[] BuildBody(IReadOnlyList<ChatTurn> messages)
        {
            var output = new MemoryStream();
            using (var w = new Utf8JsonWriter(output))
            {
                w.WriteStartObject();
                w.WriteString("model", _options.Model);
                w.WriteNumber("max_tokens", _options.MaxTokens);
                w.WriteNumber("temperature", _options.Temperature);
                w.WriteBoolean("stream", false);
                w.WriteStartArray("messages");
                foreach (ChatTurn m in messages)
                {
                    w.WriteStartObject();
                    w.WriteString("role", m.Role);
                    w.WriteString("content", m.Content);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteEndObject();
            }
            return output.ToArray();
        }

        internal static string ParseContent(byte[] json)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("choices", out JsonElement choices) ||
                    choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0) return null;
                if (!choices[0].TryGetProperty("message", out JsonElement message) ||
                    !message.TryGetProperty("content", out JsonElement content) ||
                    content.ValueKind != JsonValueKind.String) return null;
                return Tidy(content.GetString());
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>Drops reasoning blocks some small models emit, and wrapping quotes.</summary>
        internal static string Tidy(string text)
        {
            if (text == null) return null;
            text = ThinkBlock.Replace(text, "").Trim();
            text = text.Trim('"', '\'', ' ', '\n', '\r', '\t');
            return text.Length == 0 ? null : text;
        }
    }
}
