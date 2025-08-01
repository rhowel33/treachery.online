/*
 * Copyright (C) 2020-2025 Ronald Ossendrijver (admin@treachery.online)
 * This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version. This
 * program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License for more details. You should have
 * received a copy of the GNU General Public License along with this program. If not, see <http://www.gnu.org/licenses/>.
 */

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Treachery.Bots;

public class OllamaClient
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string _model;

    public OllamaClient(string baseUrl = "http://localhost:11434", string model = "gemma3:latest")
    {
        _httpClient = new HttpClient();
        _baseUrl = baseUrl.TrimEnd('/');
        _model = model;
    }

    public async Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var request = new OllamaGenerateRequest
        {
            Model = _model,
            Prompt = prompt,
            Stream = false,
            Options = new OllamaOptions
            {
                Temperature = 0.1f, // Low temperature for more deterministic decisions
                TopP = 0.9f,
                NumPredict = 1000
            }
        };

        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                $"{_baseUrl}/api/generate", 
                request, 
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>(cancellationToken);
            return result?.Response ?? throw new Exception("Empty response from Ollama");
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to generate response from Ollama: {ex.Message}", ex);
        }
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}

public record OllamaGenerateRequest
{
    [JsonPropertyName("model")]
    public string Model { get; init; } = "";

    [JsonPropertyName("prompt")]
    public string Prompt { get; init; } = "";

    [JsonPropertyName("stream")]
    public bool Stream { get; init; } = false;

    [JsonPropertyName("options")]
    public OllamaOptions? Options { get; init; }
}

public record OllamaOptions
{
    [JsonPropertyName("temperature")]
    public float Temperature { get; init; } = 0.1f;

    [JsonPropertyName("top_p")]
    public float TopP { get; init; } = 0.9f;

    [JsonPropertyName("num_predict")]
    public int NumPredict { get; init; } = 1000;
}

public record OllamaGenerateResponse
{
    [JsonPropertyName("model")]
    public string Model { get; init; } = "";

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; init; }

    [JsonPropertyName("response")]
    public string Response { get; init; } = "";

    [JsonPropertyName("done")]
    public bool Done { get; init; }

    [JsonPropertyName("context")]
    public int[]? Context { get; init; }

    [JsonPropertyName("total_duration")]
    public long TotalDuration { get; init; }

    [JsonPropertyName("load_duration")]
    public long LoadDuration { get; init; }

    [JsonPropertyName("prompt_eval_count")]
    public int PromptEvalCount { get; init; }

    [JsonPropertyName("prompt_eval_duration")]
    public long PromptEvalDuration { get; init; }

    [JsonPropertyName("eval_count")]
    public int EvalCount { get; init; }

    [JsonPropertyName("eval_duration")]
    public long EvalDuration { get; init; }
}