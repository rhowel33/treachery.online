/*
 * Copyright (C) 2020-2025 Ronald Ossendrijver (admin@treachery.online)
 * This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version. This
 * program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License for more details. You should have
 * received a copy of the GNU General Public License along with this program. If not, see <http://www.gnu.org/licenses/>.
 */

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Treachery.Shared;

namespace Treachery.Bots;

/// <summary>
/// Client for communicating with the MCP server process via JSON-RPC over stdin/stdout
/// </summary>
public class McpServerClient : IDisposable
{
    private Process? _mcpServerProcess;
    private readonly SemaphoreSlim _requestLock = new(1, 1);
    private readonly Dictionary<string, TaskCompletionSource<JsonElement>> _pendingRequests = new();
    private int _nextRequestId = 1;
    private Task? _outputReaderTask;
    private readonly string _mcpServerPath;
    private bool _isInitialized = false;
    private List<McpTool> _availableTools = new();

    public McpServerClient(string mcpServerPath)
    {
        _mcpServerPath = mcpServerPath;
    }

    public async Task StartAsync()
    {
        if (_mcpServerProcess != null && !_mcpServerProcess.HasExited)
            return;

        _mcpServerProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = _mcpServerPath,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };

        _mcpServerProcess.ErrorDataReceived += (sender, args) =>
        {
            if (!string.IsNullOrEmpty(args.Data))
            {
#if DEBUG
                Console.Error.WriteLine($"[MCP Server Error] {args.Data}");
#endif
            }
        };

        // Auto-restart on crash
        _mcpServerProcess.Exited += async (sender, args) =>
        {
            Console.Error.WriteLine("[MCP Server] Process exited unexpectedly. Attempting restart...");
            await Task.Delay(1000); // Wait before restart
            try
            {
                await StartAsync();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[MCP Server] Failed to restart: {ex.Message}");
            }
        };

        _mcpServerProcess.Start();
        _mcpServerProcess.BeginErrorReadLine();

        // Start reading responses
        _outputReaderTask = Task.Run(ReadResponsesAsync);

        // Wait a moment for server to start
        await Task.Delay(500);

        // Initialize the MCP session
        await InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        var initRequest = new
        {
            jsonrpc = "2.0",
            id = _nextRequestId++,
            method = "initialize",
            @params = new
            {
                protocolVersion = "2024-11-05",
                capabilities = new
                {
                    tools = new { }
                },
                clientInfo = new
                {
                    name = "Treachery.Bots",
                    version = "1.0.0"
                }
            }
        };

        var response = await SendRequestAsync(initRequest);

        // Parse available tools from initialization response
        if (response.TryGetProperty("result", out var result) &&
            result.TryGetProperty("capabilities", out var capabilities) &&
            capabilities.TryGetProperty("tools", out var toolsCap) &&
            toolsCap.TryGetProperty("available", out var toolsList))
        {
            _availableTools = JsonSerializer.Deserialize<List<McpTool>>(toolsList.GetRawText()) ?? new();
        }

        // Send initialized notification
        var initializedNotification = new
        {
            jsonrpc = "2.0",
            method = "notifications/initialized"
        };

        await SendNotificationAsync(initializedNotification);
        _isInitialized = true;
    }

    public async Task RegisterGameAsync(string gameId, string gameStateJson)
    {
        await CallToolAsync("RegisterGame", new { gameId, gameStateJson });
    }

    public async Task UpdateGameStateAsync(string gameId, string gameStateJson)
    {
        await CallToolAsync("UpdateGameState", new { gameId, gameStateJson });
    }

    public async Task<string> GetGameStateAsync(string gameId, Faction faction)
    {
        var result = await CallToolAsync("GetGameState", new { gameId, faction = faction.ToString() });
        return ExtractContent(result);
    }

    public async Task<string> GetLegalActionsAsync(string gameId, Faction faction)
    {
        var result = await CallToolAsync("GetLegalActions", new { gameId, faction = faction.ToString() });
        return ExtractContent(result);
    }

    public async Task<string> GetStrategicAnalysisAsync(string gameId, Faction faction)
    {
        var result = await CallToolAsync("GetStrategicAnalysis", new { gameId, faction = faction.ToString() });
        return ExtractContent(result);
    }

    public async Task<string> ValidateActionAsync(string gameId, Faction faction, string actionType)
    {
        var result = await CallToolAsync("ValidateAction", new { gameId, faction = faction.ToString(), actionType });
        return ExtractContent(result);
    }

    public List<OllamaTool> GetToolDefinitions()
    {
        // Convert MCP tools to Ollama tool format
        return _availableTools.Select(tool => new OllamaTool
        {
            Type = "function",
            Function = new OllamaToolFunction
            {
                Name = tool.Name,
                Description = tool.Description,
                Parameters = tool.InputSchema
            }
        }).ToList();
    }

    private async Task<JsonElement> CallToolAsync(string toolName, object arguments)
    {
        if (!_isInitialized)
            throw new InvalidOperationException("MCP client not initialized");

        var request = new
        {
            jsonrpc = "2.0",
            id = _nextRequestId++,
            method = "tools/call",
            @params = new
            {
                name = toolName,
                arguments
            }
        };

        return await SendRequestAsync(request);
    }

    private async Task<JsonElement> SendRequestAsync(object request)
    {
        await _requestLock.WaitAsync();
        try
        {
            if (_mcpServerProcess == null || _mcpServerProcess.HasExited)
                throw new InvalidOperationException("MCP server is not running");

            var requestJson = JsonSerializer.Serialize(request);
            var requestId = ExtractRequestId(request);

            var tcs = new TaskCompletionSource<JsonElement>();
            _pendingRequests[requestId] = tcs;

#if DEBUG
            Console.WriteLine($"[MCP Request] {requestJson}");
#endif

            await _mcpServerProcess.StandardInput.WriteLineAsync(requestJson);
            await _mcpServerProcess.StandardInput.FlushAsync();

            // Wait for response with timeout
            var timeoutTask = Task.Delay(30000); // 30 second timeout
            var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

            if (completedTask == timeoutTask)
            {
                _pendingRequests.Remove(requestId);
                throw new TimeoutException($"MCP request {requestId} timed out");
            }

            return await tcs.Task;
        }
        finally
        {
            _requestLock.Release();
        }
    }

    private async Task SendNotificationAsync(object notification)
    {
        if (_mcpServerProcess == null || _mcpServerProcess.HasExited)
            throw new InvalidOperationException("MCP server is not running");

        var notificationJson = JsonSerializer.Serialize(notification);

#if DEBUG
        Console.WriteLine($"[MCP Notification] {notificationJson}");
#endif

        await _mcpServerProcess.StandardInput.WriteLineAsync(notificationJson);
        await _mcpServerProcess.StandardInput.FlushAsync();
    }

    private async Task ReadResponsesAsync()
    {
        try
        {
            while (_mcpServerProcess != null && !_mcpServerProcess.HasExited)
            {
                var line = await _mcpServerProcess.StandardOutput.ReadLineAsync();
                if (string.IsNullOrEmpty(line))
                    continue;

#if DEBUG
                Console.WriteLine($"[MCP Response] {line}");
#endif

                try
                {
                    var response = JsonSerializer.Deserialize<JsonElement>(line);

                    if (response.TryGetProperty("id", out var idElement))
                    {
                        var id = idElement.GetInt32().ToString();
                        if (_pendingRequests.TryGetValue(id, out var tcs))
                        {
                            _pendingRequests.Remove(id);
                            tcs.SetResult(response);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[MCP] Failed to parse response: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[MCP] Response reader error: {ex.Message}");
        }
    }

    private string ExtractRequestId(object request)
    {
        var json = JsonSerializer.Serialize(request);
        var element = JsonSerializer.Deserialize<JsonElement>(json);
        if (element.TryGetProperty("id", out var idElement))
        {
            return idElement.GetInt32().ToString();
        }
        throw new InvalidOperationException("Request missing id field");
    }

    private string ExtractContent(JsonElement response)
    {
        if (response.TryGetProperty("result", out var result))
        {
            if (result.TryGetProperty("content", out var content))
            {
                if (content.ValueKind == JsonValueKind.Array && content.GetArrayLength() > 0)
                {
                    var firstContent = content[0];
                    if (firstContent.TryGetProperty("text", out var text))
                    {
                        return text.GetString() ?? "";
                    }
                }
                return content.GetString() ?? "";
            }
            return result.GetRawText();
        }
        return "";
    }

    public void Dispose()
    {
        try
        {
            _mcpServerProcess?.Kill();
            _mcpServerProcess?.Dispose();
        }
        catch { }
        _requestLock?.Dispose();
    }
}

public record McpTool
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("description")]
    public string Description { get; init; } = "";

    [JsonPropertyName("inputSchema")]
    public object InputSchema { get; init; } = new { };
}
