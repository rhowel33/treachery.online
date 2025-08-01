/*
 * Copyright (C) 2020-2025 Ronald Ossendrijver (admin@treachery.online)
 * This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version. This
 * program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License for more details. You should have
 * received a copy of the GNU General Public License along with this program. If not, see <http://www.gnu.org/licenses/>.
 */

using System.Reflection;
using System.Text;

namespace Treachery.Bots;

public class GemmaBot : IBot
{
    private readonly Game _game;
    private readonly Player _player;
    private readonly OllamaClient _ollamaClient;
    private readonly ClassicBot _fallbackBot;
    private readonly string _gameId;

    public GemmaBot(Game game, Player player, BotParameters parameters, string ollamaUrl = "http://localhost:11434", string model = "gemma3:latest", string gameId = "")
    {
        _game = game;
        _player = player;
        _ollamaClient = new OllamaClient(ollamaUrl, model);
        _fallbackBot = new ClassicBot(game, player, parameters);
        _gameId = gameId;
    }

    public GameEvent? DetermineHighestPriorityInPhaseAction(List<Type> events)
    {
        return DetermineActionAsync(events, "HighestPriority").GetAwaiter().GetResult();
    }

    public GameEvent? DetermineHighPriorityInPhaseAction(List<Type> events)
    {
        return DetermineActionAsync(events, "HighPriority").GetAwaiter().GetResult();
    }

    public GameEvent? DetermineMiddlePriorityInPhaseAction(List<Type> events)
    {
        return DetermineActionAsync(events, "MiddlePriority").GetAwaiter().GetResult();
    }

    public GameEvent? DetermineLowPriorityInPhaseAction(List<Type> events)
    {
        return DetermineActionAsync(events, "LowPriority").GetAwaiter().GetResult();
    }

    public GameEvent? DetermineEndPhaseAction(List<Type> events)
    {
        return DetermineActionAsync(events, "EndPhase").GetAwaiter().GetResult();
    }

    private async Task<GameEvent?> DetermineActionAsync(List<Type> events, string priority)
    {
        if (!events.Any())
            return null;

        try
        {
            LogInfo($"GemmaBot determining {priority} action from {events.Count} options");
            
            // Check for human ally instructions
            var humanInstruction = GetHumanAllyInstruction();
            
            // Create the prompt with game state and human instructions
            var gameState = GameStateSerializer.SerializeGameStateForLLM(_game, _player, events);
            var prompt = CreateEnhancedPromptWithInstructions(gameState, events, humanInstruction, priority);
            
            LogInfo("Sending request to Gemma...");
            
            // Get decision from Gemma
            var response = await _ollamaClient.GenerateAsync(prompt);
            
            LogInfo($"Gemma response: {response}");
            
            // Parse the response to get the action type
            var chosenActionType = ParseActionFromResponse(response, events);
            
            if (chosenActionType == null)
            {
                LogInfo($"Could not parse action from Gemma response: '{response.Trim()}', falling back to ClassicBot");
                return GetFallbackAction(events, priority);
            }
            
            // Double-check that this action type is actually available
            if (!events.Contains(chosenActionType))
            {
                LogInfo($"Gemma chose unavailable action {chosenActionType.Name}, falling back to ClassicBot");
                return GetFallbackAction(events, priority);
            }
            
            // Clear instruction if it was successfully used
            if (humanInstruction != null && ShouldClearInstruction(chosenActionType, humanInstruction))
            {
                BotInstructionSystem.ClearBotInstruction(_gameId, _player.Faction);
                LogInfo($"Cleared instruction after using: {humanInstruction.Summary}");
            }
            
            // Create the actual action using the appropriate method from ClassicBot
            var action = CreateActionUsingReflection(chosenActionType);
            
            if (action != null)
            {
                var validation = action.Validate();
                if (validation == null)
                {
                    LogInfo($"Gemma chose valid action: {action.GetMessage()}" + 
                           (humanInstruction != null ? $" (following instruction: {humanInstruction.Summary})" : ""));
                    return action;
                }
                else
                {
                    LogInfo($"Gemma chose invalid action ({validation}), trying alternative approach");
                    
                    // Try to get a valid action of the same type from ClassicBot with multiple attempts
                    var validAction = TryGetValidActionOfType(chosenActionType, events, 3);
                    if (validAction != null)
                    {
                        LogInfo($"Found valid alternative: {validAction.GetMessage()}");
                        return validAction;
                    }
                    
                    LogInfo($"No valid alternatives found, falling back to ClassicBot");
                }
            }
        }
        catch (Exception ex)
        {
            LogInfo($"Error with Gemma decision: {ex.Message}");
        }
        
        return GetFallbackAction(events, priority);
    }

    private Type? ParseActionFromResponse(string response, List<Type> availableActions)
    {
        if (string.IsNullOrWhiteSpace(response))
            return null;
            
        // Clean up the response
        var cleanResponse = response.Trim()
            .Replace("```", "")
            .Replace("**", "")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault()?.Trim();
            
        if (string.IsNullOrWhiteSpace(cleanResponse))
            return null;
        
        // Try exact match first
        var exactMatch = availableActions.FirstOrDefault(t => 
            string.Equals(t.Name, cleanResponse, StringComparison.OrdinalIgnoreCase));
        
        if (exactMatch != null)
            return exactMatch;
        
        // Try partial match
        var partialMatch = availableActions.FirstOrDefault(t => 
            t.Name.Contains(cleanResponse, StringComparison.OrdinalIgnoreCase) ||
            cleanResponse.Contains(t.Name, StringComparison.OrdinalIgnoreCase));
            
        return partialMatch;
    }

    private GameEvent? CreateActionUsingReflection(Type actionType)
    {
        try
        {
            // Try to find a method in ClassicBot that creates this action
            var methodName = $"Determine{actionType.Name}";
            var method = typeof(ClassicBot).GetMethod(methodName, 
                BindingFlags.NonPublic | BindingFlags.Instance);
            
            if (method != null)
            {
                var result = method.Invoke(_fallbackBot, null);
                return result as GameEvent;
            }
            
            // Fallback: try to construct the action directly
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

    private GameEvent? GetFallbackAction(List<Type> events, string priority) => priority switch
    {
        "HighestPriority" => _fallbackBot.DetermineHighestPriorityInPhaseAction(events),
        "HighPriority" => _fallbackBot.DetermineHighPriorityInPhaseAction(events),
        "MiddlePriority" => _fallbackBot.DetermineMiddlePriorityInPhaseAction(events),
        "LowPriority" => _fallbackBot.DetermineLowPriorityInPhaseAction(events),
        "EndPhase" => _fallbackBot.DetermineEndPhaseAction(events),
        _ => null
    };

    private BotInstructions? GetHumanAllyInstruction()
    {
        if (string.IsNullOrEmpty(_gameId)) return null;
        
        var instruction = BotInstructionSystem.GetBotInstruction(_gameId, _player.Faction);
        
        // Check if instruction is from human ally and not expired
        if (instruction != null && !instruction.IsExpired(TimeSpan.FromMinutes(10)))
        {
            // Verify the instruction came from our human ally
            var humanAlly = _player.HasAlly ? _game.Players.FirstOrDefault(p => p.Faction == _player.Ally && !p.IsBot) : null;
            if (humanAlly != null)
            {
                return instruction;
            }
        }
        
        return null;
    }
    
    private string CreateEnhancedPromptWithInstructions(string gameState, List<Type> availableActions, BotInstructions? instruction, string priority)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine("You are an AI playing the Dune board game. Based on the current game state, you need to decide which action to take.");
        sb.AppendLine();
        
        if (instruction != null)
        {
            sb.AppendLine("🤝 ALLY INSTRUCTION:");
            sb.AppendLine($"Your human ally has given you this instruction: \"{instruction.Details}\"");
            sb.AppendLine($"Summary: {instruction.Summary}");
            sb.AppendLine($"Priority: {instruction.Priority}");
            sb.AppendLine();
            
            switch (instruction.Type)
            {
                case InstructionType.Wait:
                    sb.AppendLine("IMPORTANT: Your ally wants you to wait/pass this turn if possible.");
                    break;
                case InstructionType.Combat:
                    sb.AppendLine("IMPORTANT: Your ally wants you to focus on combat actions.");
                    break;
                case InstructionType.Movement:
                    sb.AppendLine("IMPORTANT: Your ally wants you to focus on movement actions.");
                    break;
                case InstructionType.Bidding:
                    sb.AppendLine("IMPORTANT: Your ally has given you bidding guidance.");
                    break;
                case InstructionType.Strategy:
                    sb.AppendLine("IMPORTANT: Your ally has given you strategic guidance.");
                    break;
            }
            sb.AppendLine();
        }
        
        sb.AppendLine("RULES:");
        sb.AppendLine("1. You must respond with ONLY the exact class name of the action you want to take");
        sb.AppendLine("2. Choose from the available actions listed");
        sb.AppendLine("3. Consider winning conditions: control 3+ strongholds or achieve faction-specific victory");
        sb.AppendLine("4. Think strategically about resource management, positioning, and timing");
        sb.AppendLine("5. Be aware of other players' winning positions and try to prevent them from winning");
        
        if (instruction != null)
        {
            sb.AppendLine("6. PRIORITIZE following your ally's instructions when possible and strategic");
            sb.AppendLine("7. If the ally instruction doesn't match available actions, choose the closest strategic alternative");
        }
        
        sb.AppendLine();
        sb.AppendLine(gameState);
        
        sb.AppendLine("RESPONSE FORMAT:");
        sb.AppendLine("Respond with EXACTLY ONE of the action names from the list above.");
        sb.AppendLine("Examples of correct responses:");
        foreach (var actionType in availableActions.Take(Math.Min(3, availableActions.Count)))
        {
            sb.AppendLine($"  {actionType.Name}");
        }
        sb.AppendLine();
        sb.AppendLine("IMPORTANT: Only respond with the exact action name, nothing else.");
        sb.AppendLine();
        sb.AppendLine("Your choice:");
        
        return sb.ToString();
    }
    
    private GameEvent? TryGetValidActionOfType(Type actionType, List<Type> events, int maxAttempts)
    {
        // Only try if the action type is actually available
        if (!events.Contains(actionType)) return null;
        
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                var action = CreateActionUsingReflection(actionType);
                if (action != null && action.Validate() == null)
                {
                    return action;
                }
            }
            catch (Exception ex)
            {
                LogInfo($"Attempt {attempt + 1} failed for {actionType.Name}: {ex.Message}");
            }
        }
        
        return null;
    }

    private bool ShouldClearInstruction(Type chosenAction, BotInstructions instruction)
    {
        // Clear instruction if it was a one-time command or if it was successfully followed
        return instruction.Type switch
        {
            InstructionType.Wait => chosenAction.Name.Contains("Pass") || chosenAction.Name.Contains("End"),
            InstructionType.Combat => chosenAction.Name.Contains("Battle") || chosenAction.Name.Contains("Attack"),
            InstructionType.Movement => chosenAction.Name.Contains("Move") || chosenAction.Name.Contains("Caravan"),
            InstructionType.Shipment => chosenAction.Name.Contains("Shipment"),
            InstructionType.Bidding => chosenAction.Name.Contains("Bid"),
            InstructionType.Alliance => chosenAction.Name.Contains("Alliance"),
            InstructionType.CardPlay => chosenAction.Name.Contains("Card") || chosenAction.Name.Contains("Play"),
            _ => false // Don't clear general or strategy instructions automatically
        };
    }

    private void LogInfo(string message)
    {
        #if DEBUG
        Console.WriteLine($"[GemmaBot] {_player.Faction}: {message}");
        #endif
    }
    
    public void Dispose()
    {
        _ollamaClient?.Dispose();
    }
}