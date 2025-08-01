/*
 * Copyright (C) 2020-2025 Ronald Ossendrijver (admin@treachery.online)
 * This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version. This
 * program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License for more details. You should have
 * received a copy of the GNU General Public License along with this program. If not, see <http://www.gnu.org/licenses/>.
 */

using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Treachery.Bots;

public class McpEnhancedGemmaBot : IBot
{
    private readonly Game _game;
    private readonly Player _player;
    private readonly OllamaClient _ollamaClient;
    private readonly ClassicBot _fallbackBot;
    private readonly string _gameId;
    private readonly McpClient _mcpClient;

    // Track bidding state (similar to PhaseAwareGemmaBot)
    private bool _hasDeterminedBiddingStrategy = false;
    private int _maxCardsToTarget = 0;
    private int _maxSpiceToSpend = 0;

    public McpEnhancedGemmaBot(Game game, Player player, BotParameters parameters, string ollamaUrl = "http://localhost:11434", string model = "gemma3:latest", string gameId = "")
    {
        _game = game;
        _player = player;
        _ollamaClient = new OllamaClient(ollamaUrl, model);
        _fallbackBot = new ClassicBot(game, player, parameters);
        _gameId = gameId;
        _mcpClient = new McpClient();
        
        // Register this game with the MCP server
        _ = RegisterGameWithMcp();
    }

    public GameEvent? DetermineHighestPriorityInPhaseAction(List<Type> events)
    {
        return DetermineActionWithMcpSupport(events, "HighestPriority");
    }

    public GameEvent? DetermineHighPriorityInPhaseAction(List<Type> events)
    {
        return DetermineActionWithMcpSupport(events, "HighPriority");
    }

    public GameEvent? DetermineMiddlePriorityInPhaseAction(List<Type> events)
    {
        return DetermineActionWithMcpSupport(events, "MiddlePriority");
    }

    public GameEvent? DetermineLowPriorityInPhaseAction(List<Type> events)
    {
        return DetermineActionWithMcpSupport(events, "LowPriority");
    }

    public GameEvent? DetermineEndPhaseAction(List<Type> events)
    {
        return DetermineActionWithMcpSupport(events, "EndPhase");
    }

    private GameEvent? DetermineActionWithMcpSupport(List<Type> events, string priority)
    {
        if (!events.Any())
            return null;

        var currentPhase = _game.CurrentPhase;
        var currentMainPhase = _game.CurrentMainPhase;
        
        LogInfo($"McpEnhancedGemmaBot determining {priority} action in {currentPhase}/{currentMainPhase} from {events.Count} options");

        // Use same phase-aware logic as PhaseAwareGemmaBot
        if (ShouldUseGemmaForPhase(currentPhase, currentMainPhase, events, priority))
        {
            LogInfo("Using Gemma with MCP support for strategic decision");
            return DetermineActionWithMcpAsync(events, priority).GetAwaiter().GetResult();
        }
        else
        {
            LogInfo("Using ClassicBot for this phase/action combination");
            return GetFallbackAction(events, priority);
        }
    }

    private async Task<GameEvent?> DetermineActionWithMcpAsync(List<Type> events, string priority)
    {
        try
        {
            LogInfo($"MCP-Enhanced GemmaBot determining {priority} action from {events.Count} options");
            
            // Update MCP server with current game state
            await UpdateMcpGameState();

            // Get strategic analysis from MCP
            var strategicAnalysis = await _mcpClient.GetStrategicAnalysis(_gameId, _player.Faction.ToString());
            LogInfo($"MCP Strategic Analysis: {strategicAnalysis.Substring(0, Math.Min(200, strategicAnalysis.Length))}...");

            // Get legal actions validation from MCP
            var legalActions = await _mcpClient.GetLegalActions(_gameId, _player.Faction.ToString());
            LogInfo($"MCP Legal Actions validated");

            // Check for human ally instructions
            var humanInstruction = await GetHumanAllyInstructionViaMcp();
            
            // Create enhanced prompt with MCP data
            var prompt = CreateMcpEnhancedPrompt(strategicAnalysis, legalActions, events, humanInstruction, priority);
            
            LogInfo("Sending MCP-enhanced request to Gemma...");
            
            // Get decision from Gemma
            var response = await _ollamaClient.GenerateAsync(prompt);
            
            LogInfo($"Gemma response: {response}");
            
            // Parse and validate the response
            var chosenActionType = ParseActionFromResponse(response, events);
            
            if (chosenActionType == null)
            {
                LogInfo($"Could not parse action from Gemma response: '{response.Trim()}', falling back to ClassicBot");
                return GetFallbackAction(events, priority);
            }
            
            // Use MCP to validate the chosen action
            var validationResult = await _mcpClient.ValidateAction(_gameId, _player.Faction.ToString(), chosenActionType.Name);
            var validation = JsonSerializer.Deserialize<McpValidationResult>(validationResult);
            
            if (validation?.IsValid != true)
            {
                LogInfo($"MCP validation failed for {chosenActionType.Name}: {validation?.Reason}, falling back to ClassicBot");
                return GetFallbackAction(events, priority);
            }

            // Clear instruction if it was successfully used
            if (humanInstruction != null && ShouldClearInstruction(chosenActionType, humanInstruction))
            {
                BotInstructionSystem.ClearBotInstruction(_gameId, _player.Faction);
                LogInfo($"Cleared instruction after using: {humanInstruction.Summary}");
            }
            
            // Create the actual action using ClassicBot
            var action = CreateActionUsingReflection(chosenActionType);
            
            if (action != null)
            {
                var finalValidation = action.Validate();
                if (finalValidation == null)
                {
                    LogInfo($"MCP-Enhanced Gemma chose valid action: {action.GetMessage()}" + 
                           (humanInstruction != null ? $" (following instruction: {humanInstruction.Summary})" : ""));
                    return action;
                }
                else
                {
                    LogInfo($"Final validation failed ({finalValidation}), falling back to ClassicBot");
                }
            }
        }
        catch (Exception ex)
        {
            LogInfo($"Error with MCP-Enhanced Gemma decision: {ex.Message}");
        }
        
        return GetFallbackAction(events, priority);
    }

    private async Task RegisterGameWithMcp()
    {
        try
        {
            var gameStateJson = JsonSerializer.Serialize(_game);
            await _mcpClient.RegisterGame(_gameId, gameStateJson);
            LogInfo("Game registered with MCP server");
        }
        catch (Exception ex)
        {
            LogInfo($"Failed to register game with MCP: {ex.Message}");
        }
    }

    private async Task UpdateMcpGameState()
    {
        try
        {
            var gameStateJson = JsonSerializer.Serialize(_game);
            await _mcpClient.UpdateGameState(_gameId, gameStateJson);
        }
        catch (Exception ex)
        {
            LogInfo($"Failed to update MCP game state: {ex.Message}");
        }
    }

    private async Task<BotInstructions?> GetHumanAllyInstructionViaMcp()
    {
        try
        {
            var instructionsJson = await _mcpClient.GetAllyInstructions(_gameId, _player.Faction.ToString());
            var instructions = JsonSerializer.Deserialize<McpInstructionResult>(instructionsJson);
            
            if (instructions?.HasInstructions == true && !instructions.IsExpired)
            {
                // Verify this is from our actual human ally
                var humanAlly = _player.HasAlly ? _game.Players.FirstOrDefault(p => p.Faction == _player.Ally && !p.IsBot) : null;
                if (humanAlly != null)
                {
                    return new BotInstructions
                    {
                        Type = Enum.Parse<InstructionType>(instructions.Type),
                        Priority = Enum.Parse<InstructionPriority>(instructions.Priority),
                        Summary = instructions.Summary,
                        Details = instructions.Details,
                        CreatedAt = instructions.CreatedAt.DateTime
                    };
                }
            }
        }
        catch (Exception ex)
        {
            LogInfo($"Error getting ally instructions via MCP: {ex.Message}");
        }
        
        return null;
    }

    private string CreateMcpEnhancedPrompt(string strategicAnalysis, string legalActions, List<Type> availableActions, BotInstructions? instruction, string priority)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine("You are an AI playing the Dune board game. You have access to detailed strategic analysis and validated legal actions.");
        sb.AppendLine("Use this information to make the best possible strategic decision.");
        sb.AppendLine();

        if (instruction != null)
        {
            sb.AppendLine("🤝 ALLY INSTRUCTION:");
            sb.AppendLine($"Your human ally has given you this instruction: \"{instruction.Details}\"");
            sb.AppendLine($"Summary: {instruction.Summary}");
            sb.AppendLine($"Priority: {instruction.Priority}");
            sb.AppendLine();
        }

        sb.AppendLine("📊 STRATEGIC ANALYSIS:");
        sb.AppendLine(strategicAnalysis);
        sb.AppendLine();

        sb.AppendLine("⚖️ LEGAL ACTIONS (PRE-VALIDATED):");
        sb.AppendLine(legalActions);
        sb.AppendLine();

        sb.AppendLine("🎯 DECISION RULES:");
        sb.AppendLine("1. You must respond with ONLY the exact class name of the action you want to take");
        sb.AppendLine("2. All actions listed above have been pre-validated as legal");
        sb.AppendLine("3. Consider winning conditions and strategic positioning");
        sb.AppendLine("4. Be aware of threats from other players");
        sb.AppendLine("5. Think long-term about resource management and faction advantages");
        
        if (instruction != null)
        {
            sb.AppendLine("6. PRIORITIZE following your ally's instructions when strategic");
        }
        
        sb.AppendLine();
        sb.AppendLine("RESPONSE FORMAT:");
        sb.AppendLine("Respond with EXACTLY ONE of the action names from the legal actions list above.");
        sb.AppendLine("Examples of correct responses:");
        foreach (var actionType in availableActions.Take(Math.Min(3, availableActions.Count)))
        {
            sb.AppendLine($"  {actionType.Name}");
        }
        sb.AppendLine();
        sb.AppendLine("IMPORTANT: Only respond with the exact action name, nothing else.");
        sb.AppendLine();
        sb.AppendLine("Your strategic choice:");
        
        return sb.ToString();
    }

    // Reuse methods from PhaseAwareGemmaBot with same logic
    private bool ShouldUseGemmaForPhase(Phase currentPhase, MainPhase currentMainPhase, List<Type> events, string priority)
    {
        // Always check for deals
        if (events.Any(e => e.Name.Contains("DealOffered") || e.Name.Contains("DealAccepted")))
        {
            LogInfo("Deal-related action available - using Gemma");
            return true;
        }

        // Always check for alliance/communication actions
        if (events.Any(e => e.Name.Contains("Alliance") || e.Name.Contains("AllyPermission")))
        {
            LogInfo("Alliance-related action available - using Gemma");
            return true;
        }

        // Phase-specific logic (same as PhaseAwareGemmaBot)
        switch (currentMainPhase)
        {
            case MainPhase.Storm:
                return false;
            case MainPhase.Blow:
                return events.Any(e => 
                    e.Name.Contains("Harvester") || 
                    e.Name.Contains("FamilyAtomics") ||
                    e.Name.Contains("Thumper") ||
                    e.Name.Contains("WeatherControl"));
            case MainPhase.Charity:
                return false;
            case MainPhase.Bidding:
                return ShouldUseGemmaForBidding(events);
            case MainPhase.Resurrection:
                return false;
            case MainPhase.ShipmentAndMove:
                return true;
            case MainPhase.Battle:
                return true;
            case MainPhase.Collection:
                return false;
            case MainPhase.Contemplate:
                return false;
            default:
                return events.Any(e => 
                    e.Name.Contains("Move") || 
                    e.Name.Contains("Battle") || 
                    e.Name.Contains("Shipment") ||
                    e.Name.Contains("Alliance") ||
                    e.Name.Contains("Deal"));
        }
    }

    private bool ShouldUseGemmaForBidding(List<Type> events)
    {
        if (!_hasDeterminedBiddingStrategy && events.Any(e => e.Name.Contains("Bid")))
        {
            LogInfo("First bidding decision - using Gemma for strategy");
            _hasDeterminedBiddingStrategy = true;
            return true;
        }
        return false;
    }

    // Reuse other methods from existing bots...
    private Type? ParseActionFromResponse(string response, List<Type> availableActions)
    {
        if (string.IsNullOrWhiteSpace(response))
            return null;
            
        var cleanResponse = response.Trim()
            .Replace("```", "")
            .Replace("**", "")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault()?.Trim();
            
        if (string.IsNullOrWhiteSpace(cleanResponse))
            return null;
        
        var exactMatch = availableActions.FirstOrDefault(t => 
            string.Equals(t.Name, cleanResponse, StringComparison.OrdinalIgnoreCase));
        
        if (exactMatch != null)
            return exactMatch;
        
        var partialMatch = availableActions.FirstOrDefault(t => 
            t.Name.Contains(cleanResponse, StringComparison.OrdinalIgnoreCase) ||
            cleanResponse.Contains(t.Name, StringComparison.OrdinalIgnoreCase));
            
        return partialMatch;
    }

    private GameEvent? CreateActionUsingReflection(Type actionType)
    {
        try
        {
            var methodName = $"Determine{actionType.Name}";
            var method = typeof(ClassicBot).GetMethod(methodName, 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            if (method != null)
            {
                var result = method.Invoke(_fallbackBot, null);
                return result as GameEvent;
            }
            
            var constructor = actionType.GetConstructor(new[] { typeof(Game), typeof(Faction) });
            if (constructor != null)
            {
                return constructor.Invoke(new object[] { _game, _player.Faction }) as GameEvent;
            }
        }
        catch (Exception ex)
        {
            LogInfo($"Error creating action {actionType.Name}: {ex.Message}");
        }
        
        return null;
    }

    private bool ShouldClearInstruction(Type chosenAction, BotInstructions instruction)
    {
        return instruction.Type switch
        {
            InstructionType.Wait => chosenAction.Name.Contains("Pass") || chosenAction.Name.Contains("End"),
            InstructionType.Combat => chosenAction.Name.Contains("Battle") || chosenAction.Name.Contains("Attack"),
            InstructionType.Movement => chosenAction.Name.Contains("Move") || chosenAction.Name.Contains("Caravan"),
            InstructionType.Shipment => chosenAction.Name.Contains("Shipment"),
            InstructionType.Bidding => chosenAction.Name.Contains("Bid"),
            InstructionType.Alliance => chosenAction.Name.Contains("Alliance"),
            InstructionType.CardPlay => chosenAction.Name.Contains("Card") || chosenAction.Name.Contains("Play"),
            _ => false
        };
    }

    private GameEvent? GetFallbackAction(List<Type> events, string priority) => priority switch
    {
        "HighestPriority" => _fallbackBot.DetermineHighestPriorityInPhaseAction(events),
        "HighPriority" => _fallbackBot.DetermineHighPriorityInPhaseAction(events),
        "MiddlePriority" => _fallbackBot.DetermineMiddlePriorityInPhaseAction(events),
        "LowPriority" => _fallbackBot.DetermineLowPriorityInPhaseAction(events),
        "EndPhase" => _fallbackBot.DetermineEndPhaseAction(events),
        _ => null
    };

    private void LogInfo(string message)
    {
        #if DEBUG
        Console.WriteLine($"[McpEnhancedGemmaBot] {_player.Faction}: {message}");
        #endif
    }
    
    public void Dispose()
    {
        _ollamaClient?.Dispose();
        _mcpClient?.Dispose();
    }
}

// Data transfer objects for MCP communication
public class McpValidationResult
{
    public bool IsValid { get; set; }
    public string? Reason { get; set; }
    public string? ActionType { get; set; }
    public string? ActionMessage { get; set; }
}

public class McpInstructionResult
{
    public bool HasInstructions { get; set; }
    public string Type { get; set; } = "";
    public string Priority { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Details { get; set; } = "";
    public bool IsExpired { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}