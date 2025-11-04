/*
 * Copyright (C) 2020-2025 Ronald Ossendrijver (admin@treachery.online)
 * This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version. This
 * program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License for more details. You should have
 * received a copy of the GNU General Public License along with this program. If not, see <http://www.gnu.org/licenses/>.
 */

using System.Text.Json;
using Treachery.Shared;

namespace Treachery.Bots;

/// <summary>
/// Unified bot using gpt-oss:20b via Ollama with MCP tool calling
/// Replaces all experimental Gemma bots with a single, well-tested implementation
/// </summary>
public class GptMcpBot : IBot, IDisposable
{
    private readonly Game _game;
    private readonly Player _player;
    private readonly OllamaChatClient _ollamaClient;
    private readonly McpServerClient _mcpClient;
    private readonly ClassicBot _classicBot;
    private readonly string _gameId;
    private readonly string _mcpServerPath;
    private int _invalidActionCount = 0;
    private int _toolCallCount = 0;
    private const int MAX_TOOL_CALLS = 10; // Safety limit per decision
    private const int MAX_RETRIES_BEFORE_FALLBACK = 2;

    public GptMcpBot(Game game, Player player, BotParameters parameters, string mcpServerPath, string ollamaBaseUrl = "http://localhost:11434")
    {
        _game = game;
        _player = player;
        _gameId = game.Uid.ToString();
        _mcpServerPath = mcpServerPath;
        _ollamaClient = new OllamaChatClient(ollamaBaseUrl, "gpt-oss:20b");
        _mcpClient = new McpServerClient(mcpServerPath);
        _classicBot = new ClassicBot(game, player, parameters);

        InitializeAsync().GetAwaiter().GetResult();
    }

    private async Task InitializeAsync()
    {
        try
        {
            await _mcpClient.StartAsync();

            // Register game state with MCP server
            var gameState = JsonSerializer.Serialize(_game);
            await _mcpClient.RegisterGameAsync(_gameId, gameState);

            LogInfo("GptMcpBot initialized successfully");
        }
        catch (Exception ex)
        {
            LogError($"Failed to initialize GptMcpBot: {ex.Message}");
            LogError("Will fall back to ClassicBot for all decisions");
        }
    }

    public GameEvent? DetermineHighestPriorityInPhaseAction(List<Type> events)
    {
        return DetermineActionWithPhaseAwareness(events, "HighestPriority");
    }

    public GameEvent? DetermineHighPriorityInPhaseAction(List<Type> events)
    {
        return DetermineActionWithPhaseAwareness(events, "HighPriority");
    }

    public GameEvent? DetermineMiddlePriorityInPhaseAction(List<Type> events)
    {
        return DetermineActionWithPhaseAwareness(events, "MiddlePriority");
    }

    public GameEvent? DetermineLowPriorityInPhaseAction(List<Type> events)
    {
        return DetermineActionWithPhaseAwareness(events, "LowPriority");
    }

    public GameEvent? DetermineEndPhaseAction(List<Type> events)
    {
        return DetermineActionWithPhaseAwareness(events, "EndPhase");
    }

    private GameEvent? DetermineActionWithPhaseAwareness(List<Type> events, string priority)
    {
        if (!events.Any())
            return null;

        LogInfo($"GptMcpBot determining {priority} action in {_game.CurrentPhase}/{_game.CurrentMainPhase} from {events.Count} options");

        // Update game state in MCP server
        try
        {
            var gameState = JsonSerializer.Serialize(_game);
            _mcpClient.UpdateGameStateAsync(_gameId, gameState).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            LogError($"Failed to update game state: {ex.Message}");
        }

        // Determine if we should use GPT or fall back to ClassicBot
        if (!BotUtilities.ShouldUseGemmaForPhase(_game, _player))
        {
            LogInfo($"Using ClassicBot for phase {_game.CurrentMainPhase}");
            return GetClassicBotAction(events, priority);
        }

        // Try GPT decision with MCP tools
        try
        {
            var action = DetermineActionAsync(events, priority).GetAwaiter().GetResult();

            if (action != null)
            {
                LogInfo($"GPT selected action: {action.GetType().Name}");
                return action;
            }
        }
        catch (Exception ex)
        {
            LogError($"Error in GPT decision: {ex.Message}");
        }

        // Fallback to ClassicBot if GPT fails
        LogWarning("GPT decision failed, falling back to ClassicBot");
        return GetClassicBotAction(events, priority);
    }

    private GameEvent? GetClassicBotAction(List<Type> events, string priority)
    {
        return priority switch
        {
            "HighestPriority" => _classicBot.DetermineHighestPriorityInPhaseAction(events),
            "HighPriority" => _classicBot.DetermineHighPriorityInPhaseAction(events),
            "MiddlePriority" => _classicBot.DetermineMiddlePriorityInPhaseAction(events),
            "LowPriority" => _classicBot.DetermineLowPriorityInPhaseAction(events),
            "EndPhase" => _classicBot.DetermineEndPhaseAction(events),
            _ => null
        };
    }

    private async Task<GameEvent?> DetermineActionAsync(List<Type> events, string priority)
    {
        _toolCallCount = 0;

        try
        {
            if (!events.Any())
            {
                LogWarning("No available actions");
                return null;
            }

            // Build initial messages
            var messages = new List<OllamaChatMessage>
            {
                new()
                {
                    Role = "system",
                    Content = BuildSystemPrompt()
                },
                new()
                {
                    Role = "user",
                    Content = BuildUserPrompt(events)
                }
            };

            // Get tool definitions from MCP
            var tools = _mcpClient.GetToolDefinitions();

            // First LLM call - it should call tools to gather information
            LogInfo("Requesting GPT decision with tool access...");
            var response = await _ollamaClient.ChatAsync(messages, tools);

            // Handle tool calls
            if (response.Message.ToolCalls != null && response.Message.ToolCalls.Any())
            {
                messages.Add(response.Message);

                // Execute tool calls
                foreach (var toolCall in response.Message.ToolCalls)
                {
                    _toolCallCount++;

                    if (_toolCallCount > MAX_TOOL_CALLS)
                    {
                        LogError($"Exceeded maximum tool calls ({MAX_TOOL_CALLS}), aborting");
                        BotUtilities.LogInvalidAction("ToolCallLimit", "Too many tool calls", _game, _player);
                        return null;
                    }

                    var toolResult = await ExecuteToolCallAsync(toolCall);

                    // Add tool result to conversation
                    messages.Add(new OllamaChatMessage
                    {
                        Role = "tool",
                        Content = toolResult
                    });
                }

                // Second LLM call - make final decision with tool results
                LogInfo("Requesting final decision from GPT with tool results...");
                response = await _ollamaClient.ChatAsync(messages, tools);

                // If it tries to call more tools, limit it
                if (response.Message.ToolCalls != null && response.Message.ToolCalls.Any())
                {
                    LogWarning("GPT attempted additional tool calls, forcing decision");

                    // Execute one more round only
                    messages.Add(response.Message);
                    foreach (var toolCall in response.Message.ToolCalls.Take(3))
                    {
                        var toolResult = await ExecuteToolCallAsync(toolCall);
                        messages.Add(new OllamaChatMessage
                        {
                            Role = "tool",
                            Content = toolResult
                        });
                    }

                    // Force final decision
                    messages.Add(new OllamaChatMessage
                    {
                        Role = "user",
                        Content = "You have all the information needed. Choose your action now from the available actions list. Respond with: ACTION: [ActionName]"
                    });

                    response = await _ollamaClient.ChatAsync(messages, null); // No more tools
                }
            }

            // Parse action from final response
            var actionType = BotUtilities.ParseActionFromResponse(response.Message.Content, events);

            if (actionType == null)
            {
                LogWarning($"Could not parse action from response: {response.Message.Content}");
                _invalidActionCount++;
                BotUtilities.LogInvalidAction("UnparsableResponse", response.Message.Content, _game, _player);
                return null;
            }

            // Create action using ClassicBot's logic
            var action = BotUtilities.CreateActionUsingReflection(_game, _player, actionType, _classicBot);

            if (action == null)
            {
                LogWarning($"Could not create action of type {actionType.Name}");
                _invalidActionCount++;
                BotUtilities.LogInvalidAction(actionType.Name, "Failed to create action", _game, _player);
                return null;
            }

            // Validate action is in the available events list
            if (!events.Contains(actionType))
            {
                LogWarning($"Action {actionType.Name} is not in available actions for this priority level");
                _invalidActionCount++;
                BotUtilities.LogInvalidAction(actionType.Name, "Not in available actions", _game, _player);
                return null;
            }

            return action;
        }
        catch (Exception ex)
        {
            LogError($"Error in DetermineActionAsync: {ex.Message}");
            return null;
        }
    }

    private async Task<string> ExecuteToolCallAsync(OllamaToolCall toolCall)
    {
        try
        {
            var toolName = toolCall.Function.Name;
            var argsJson = toolCall.Function.Arguments;

            LogInfo($"Executing tool: {toolName}");
            BotUtilities.LogToolCall(toolName, argsJson);

            var args = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(argsJson);
            if (args == null)
            {
                return "Error: Could not parse tool arguments";
            }

            // Route to appropriate MCP method
            var result = toolName switch
            {
                "GetGameState" => await _mcpClient.GetGameStateAsync(_gameId, _player.Faction),
                "GetLegalActions" => await _mcpClient.GetLegalActionsAsync(_gameId, _player.Faction),
                "GetStrategicAnalysis" => await _mcpClient.GetStrategicAnalysisAsync(_gameId, _player.Faction),
                "ValidateAction" => await _mcpClient.ValidateActionAsync(
                    _gameId,
                    _player.Faction,
                    args.ContainsKey("actionType") ? args["actionType"].GetString() ?? "" : ""),
                _ => $"Unknown tool: {toolName}"
            };

            BotUtilities.LogToolCall(toolName, argsJson, result);
            return result;
        }
        catch (Exception ex)
        {
            var error = $"Tool execution error: {ex.Message}";
            LogError(error);
            return error;
        }
    }

    private string BuildSystemPrompt()
    {
        return @"You are an expert Dune board game AI playing as " + _player.Faction.ToString() + @".

Your goal is to win by controlling 3 or more strongholds (or meeting your faction's special victory condition).

To make decisions:
1. Call tools to gather information about the game state
2. Analyze the strategic situation
3. Choose the best action from the available actions list

When you've decided on an action, respond with:
ACTION: [ActionName]

For example:
ACTION: Move
ACTION: BattlePlan
ACTION: BidForCard

Be strategic but decisive. Consider:
- Your victory condition progress
- Opponent threats
- Resource management
- Alliance opportunities
- Card advantages

Choose the action that maximizes your winning chances.";
    }

    private string BuildUserPrompt(List<Type> availableActionTypes)
    {
        var actionsDescription = BotUtilities.GetAvailableActionsDescription(availableActionTypes);

        return $@"It is your turn in the Dune board game.

Current Phase: {_game.CurrentMainPhase}

Available Actions:
{actionsDescription}

Use the available tools to gather information, then choose the best action from the list above.
Remember to respond with: ACTION: [ActionName]";
    }

    private void LogInfo(string message)
    {
#if DEBUG
        Console.WriteLine($"[GptMcpBot - {_player.Faction}] {message}");
#endif
    }

    private void LogWarning(string message)
    {
#if DEBUG
        Console.WriteLine($"[GptMcpBot - {_player.Faction} WARNING] {message}");
#endif
    }

    private void LogError(string message)
    {
#if DEBUG
        Console.Error.WriteLine($"[GptMcpBot - {_player.Faction} ERROR] {message}");
#endif
    }

    public void Dispose()
    {
        // Log final statistics
        LogInfo($"Session complete. Invalid actions: {_invalidActionCount}, Total tool calls: {_toolCallCount}");

        _ollamaClient?.Dispose();
        _mcpClient?.Dispose();
    }
}
