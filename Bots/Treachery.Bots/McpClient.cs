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

namespace Treachery.Bots;

public class McpClient : IDisposable
{
    private Process? _mcpServerProcess;
    private readonly string _mcpServerPath;
    private bool _isConnected = false;

    public McpClient(string mcpServerPath = "")
    {
        // Default to the MCP server in the solution
        _mcpServerPath = string.IsNullOrEmpty(mcpServerPath) 
            ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "MCP", "Treachery.MCP.Server", "bin", "Debug", "net9.0", "Treachery.MCP.Server.exe")
            : mcpServerPath;
    }

    public async Task<bool> ConnectAsync()
    {
        if (_isConnected) return true;

        try
        {
            // Start the MCP server process
            var startInfo = new ProcessStartInfo
            {
                FileName = _mcpServerPath,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            _mcpServerProcess = Process.Start(startInfo);
            if (_mcpServerProcess == null)
            {
                LogError("Failed to start MCP server process");
                return false;
            }

            // Give the server a moment to start
            await Task.Delay(1000);

            _isConnected = true;
            LogInfo("Connected to MCP server");
            return true;
        }
        catch (Exception ex)
        {
            LogError($"Failed to connect to MCP server: {ex.Message}");
            return false;
        }
    }

    public async Task<string> GetGameState(string gameId, string faction)
    {
        if (!await EnsureConnected()) return "MCP connection failed";

        try
        {
            var request = new McpRequest
            {
                Method = "GetGameState",
                Params = new { gameId, faction }
            };

            return await SendRequestAsync(request);
        }
        catch (Exception ex)
        {
            LogError($"Error getting game state: {ex.Message}");
            return "Error getting game state";
        }
    }

    public async Task<string> GetLegalActions(string gameId, string faction)
    {
        if (!await EnsureConnected()) return "MCP connection failed";

        try
        {
            var request = new McpRequest
            {
                Method = "GetLegalActions",
                Params = new { gameId, faction }
            };

            return await SendRequestAsync(request);
        }
        catch (Exception ex)
        {
            LogError($"Error getting legal actions: {ex.Message}");
            return "Error getting legal actions";
        }
    }

    public async Task<string> ValidateAction(string gameId, string faction, string actionType)
    {
        if (!await EnsureConnected()) return JsonSerializer.Serialize(new { IsValid = false, Reason = "MCP connection failed" });

        try
        {
            var request = new McpRequest
            {
                Method = "ValidateAction",
                Params = new { gameId, faction, actionType }
            };

            return await SendRequestAsync(request);
        }
        catch (Exception ex)
        {
            LogError($"Error validating action: {ex.Message}");
            return JsonSerializer.Serialize(new { IsValid = false, Reason = ex.Message });
        }
    }

    public async Task<string> GetStrategicAnalysis(string gameId, string faction)
    {
        if (!await EnsureConnected()) return "MCP connection failed";

        try
        {
            var request = new McpRequest
            {
                Method = "GetStrategicAnalysis",
                Params = new { gameId, faction }
            };

            return await SendRequestAsync(request);
        }
        catch (Exception ex)
        {
            LogError($"Error getting strategic analysis: {ex.Message}");
            return "Error getting strategic analysis";
        }
    }

    public async Task<string> GetAllyInstructions(string gameId, string faction)
    {
        if (!await EnsureConnected()) return JsonSerializer.Serialize(new { HasInstructions = false });

        try
        {
            var request = new McpRequest
            {
                Method = "GetAllyInstructions",
                Params = new { gameId, faction }
            };

            return await SendRequestAsync(request);
        }
        catch (Exception ex)
        {
            LogError($"Error getting ally instructions: {ex.Message}");
            return JsonSerializer.Serialize(new { HasInstructions = false });
        }
    }

    public async Task<string> RegisterGame(string gameId, string gameStateJson)
    {
        if (!await EnsureConnected()) return "MCP connection failed";

        try
        {
            var request = new McpRequest
            {
                Method = "RegisterGame",
                Params = new { gameId, gameStateJson }
            };

            return await SendRequestAsync(request);
        }
        catch (Exception ex)
        {
            LogError($"Error registering game: {ex.Message}");
            return "Error registering game";
        }
    }

    public async Task<string> UpdateGameState(string gameId, string gameStateJson)
    {
        if (!await EnsureConnected()) return "MCP connection failed";

        try
        {
            var request = new McpRequest
            {
                Method = "UpdateGameState",
                Params = new { gameId, gameStateJson }
            };

            return await SendRequestAsync(request);
        }
        catch (Exception ex)
        {
            LogError($"Error updating game state: {ex.Message}");
            return "Error updating game state";
        }
    }

    private async Task<bool> EnsureConnected()
    {
        if (_isConnected && _mcpServerProcess != null && !_mcpServerProcess.HasExited)
            return true;

        return await ConnectAsync();
    }

    private async Task<string> SendRequestAsync(McpRequest request)
    {
        if (_mcpServerProcess?.StandardInput == null || _mcpServerProcess?.StandardOutput == null)
            throw new InvalidOperationException("MCP server process not available");

        try
        {
            // Serialize and send the request
            var requestJson = JsonSerializer.Serialize(request);
            await _mcpServerProcess.StandardInput.WriteLineAsync(requestJson);
            await _mcpServerProcess.StandardInput.FlushAsync();

            // Read the response
            var response = await _mcpServerProcess.StandardOutput.ReadLineAsync();
            return response ?? "No response from MCP server";
        }
        catch (Exception ex)
        {
            LogError($"Error sending request to MCP server: {ex.Message}");
            throw;
        }
    }

    private void LogInfo(string message)
    {
        #if DEBUG
        Console.WriteLine($"[McpClient]: {message}");
        #endif
    }

    private void LogError(string message)
    {
        #if DEBUG
        Console.WriteLine($"[McpClient ERROR]: {message}");
        #endif
    }

    public void Dispose()
    {
        try
        {
            if (_mcpServerProcess != null && !_mcpServerProcess.HasExited)
            {
                _mcpServerProcess.Kill();
                _mcpServerProcess.WaitForExit(5000); // Wait up to 5 seconds
                _mcpServerProcess.Dispose();
            }
            _isConnected = false;
        }
        catch (Exception ex)
        {
            LogError($"Error disposing MCP client: {ex.Message}");
        }
    }
}

// Data transfer objects for MCP communication
public class McpRequest
{
    public string Method { get; set; } = "";
    public object? Params { get; set; }
    public string Id { get; set; } = Guid.NewGuid().ToString();
}