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

public class PhaseAwareGemmaBot : IBot
{
    private readonly Game _game;
    private readonly Player _player;
    private readonly OllamaClient _ollamaClient;
    private readonly ClassicBot _fallbackBot;
    private readonly string _gameId;
    
    // Track bidding state
    private bool _hasDeterminedBiddingStrategy = false;
    private int _maxCardsToTarget = 0;
    private int _maxSpiceToSpend = 0;

    public PhaseAwareGemmaBot(Game game, Player player, BotParameters parameters, string ollamaUrl = "http://localhost:11434", string model = "gemma3:latest", string gameId = "")
    {
        _game = game;
        _player = player;
        _ollamaClient = new OllamaClient(ollamaUrl, model);
        _fallbackBot = new ClassicBot(game, player, parameters);
        _gameId = gameId;
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

        var currentPhase = _game.CurrentPhase;
        var currentMainPhase = _game.CurrentMainPhase;
        
        LogInfo($"PhaseAwareGemmaBot determining {priority} action in {currentPhase}/{currentMainPhase} from {events.Count} options");

        // Check if we should use Gemma for this phase and these actions
        if (ShouldUseGemmaForPhase(currentPhase, currentMainPhase, events, priority))
        {
            LogInfo("Using Gemma for strategic decision");
            return DetermineActionAsync(events, priority).GetAwaiter().GetResult();
        }
        else
        {
            LogInfo("Using ClassicBot for this phase/action combination");
            return GetFallbackAction(events, priority);
        }
    }

    private bool ShouldUseGemmaForPhase(Phase currentPhase, MainPhase currentMainPhase, List<Type> events, string priority)
    {
        // Always check for deals at start of phases
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

        // Phase-specific logic
        switch (currentMainPhase)
        {
            case MainPhase.Storm:
                LogInfo("Storm phase - using ClassicBot only");
                return false;

            case MainPhase.Blow:
                // Only use Gemma if we have relevant cards to play
                var hasRelevantSpiceBlowCards = events.Any(e => 
                    e.Name.Contains("Harvester") || 
                    e.Name.Contains("FamilyAtomics") ||
                    e.Name.Contains("Thumper") ||
                    e.Name.Contains("WeatherControl"));
                
                LogInfo($"Spice Blow phase - using Gemma: {hasRelevantSpiceBlowCards}");
                return hasRelevantSpiceBlowCards;

            case MainPhase.Charity:
                LogInfo("Charity phase - using ClassicBot only");
                return false;

            case MainPhase.Bidding:
                return ShouldUseGemmaForBidding(events);

            case MainPhase.Resurrection:
                LogInfo("Revival phase - using ClassicBot only");
                return false;

            case MainPhase.ShipmentAndMove:
                LogInfo("Ship-Move phase - using Gemma for strategic decisions");
                return true;

            case MainPhase.Battle:
                LogInfo("Battle phase - using Gemma for all decisions");
                return true;

            case MainPhase.Collection:
                LogInfo("Collection phase - using ClassicBot only");
                return false;

            case MainPhase.Contemplate:
                LogInfo("Mentat phase - using ClassicBot only");
                return false;

            default:
                // For other phases, use Gemma if it's a high-level strategic decision
                var isStrategicAction = events.Any(e => 
                    e.Name.Contains("Move") || 
                    e.Name.Contains("Battle") || 
                    e.Name.Contains("Shipment") ||
                    e.Name.Contains("Alliance") ||
                    e.Name.Contains("Deal"));
                
                LogInfo($"Other phase ({currentMainPhase}) - using Gemma for strategic: {isStrategicAction}");
                return isStrategicAction;
        }
    }

    private bool ShouldUseGemmaForBidding(List<Type> events)
    {
        // At start of bidding phase, ask Gemma for overall strategy
        if (!_hasDeterminedBiddingStrategy && events.Any(e => e.Name.Contains("Bid")))
        {
            LogInfo("First bidding decision - using Gemma for strategy");
            _hasDeterminedBiddingStrategy = true;
            
            // Ask Gemma for bidding strategy (this would be a separate method)
            DetermineBiddingStrategy();
            return true;
        }

        // For individual card bids, use ClassicBot with the strategy from Gemma
        LogInfo("Individual card bidding - using ClassicBot with Gemma strategy");
        return false;
    }

    private void DetermineBiddingStrategy()
    {
        try
        {
            var strategyPrompt = CreateBiddingStrategyPrompt();
            var response = _ollamaClient.GenerateAsync(strategyPrompt).GetAwaiter().GetResult();
            
            // Parse strategy response (simplified for now)
            var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                if (line.ToLower().Contains("max cards") && int.TryParse(ExtractNumber(line), out int maxCards))
                {
                    _maxCardsToTarget = maxCards;
                }
                if (line.ToLower().Contains("max spice") && int.TryParse(ExtractNumber(line), out int maxSpice))
                {
                    _maxSpiceToSpend = maxSpice;
                }
            }

            LogInfo($"Gemma bidding strategy: Target {_maxCardsToTarget} cards, spend up to {_maxSpiceToSpend} spice");
        }
        catch (Exception ex)
        {
            LogInfo($"Error getting bidding strategy from Gemma: {ex.Message}");
            // Use default strategy
            _maxCardsToTarget = 2;
            _maxSpiceToSpend = _player.Resources / 2;
        }
    }

    private string CreateBiddingStrategyPrompt()
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are playing Dune board game. Determine your bidding strategy for this turn.");
        sb.AppendLine();
        sb.AppendLine($"Your Status: {_player.Faction}");
        sb.AppendLine($"Resources: {_player.Resources}");
        sb.AppendLine($"Current Cards: {_player.TreacheryCards.Count()}/{_player.MaximumNumberOfCards}");
        sb.AppendLine($"Turn: {_game.CurrentTurn}/{_game.MaximumTurns}");
        sb.AppendLine();
        sb.AppendLine("Determine:");
        sb.AppendLine("1. Maximum number of cards to target this bidding round (0-4)");
        sb.AppendLine("2. Maximum total spice you're willing to spend (0-" + _player.Resources + ")");
        sb.AppendLine();
        sb.AppendLine("Format your response as:");
        sb.AppendLine("Max cards: X");
        sb.AppendLine("Max spice: Y");
        
        return sb.ToString();
    }

    private string ExtractNumber(string text)
    {
        var numbers = System.Text.RegularExpressions.Regex.Matches(text, @"\d+");
        return numbers.Count > 0 ? numbers[0].Value : "0";
    }

    // Reuse methods from original GemmaBot
    private async Task<GameEvent?> DetermineActionAsync(List<Type> events, string priority)
    {
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

    // Copy remaining methods from GemmaBot (GetHumanAllyInstruction, CreateEnhancedPromptWithInstructions, etc.)
    private BotInstructions? GetHumanAllyInstruction()
    {
        if (string.IsNullOrEmpty(_gameId)) return null;
        
        var instruction = BotInstructionSystem.GetBotInstruction(_gameId, _player.Faction);
        
        if (instruction != null && !instruction.IsExpired(TimeSpan.FromMinutes(10)))
        {
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
        sb.AppendLine("2. Choose ONLY from the available actions listed below - other actions are illegal");
        sb.AppendLine("3. Consider winning conditions: control 3+ strongholds or achieve faction-specific victory");
        sb.AppendLine("4. Think strategically about resource management, positioning, and timing");
        sb.AppendLine("5. Be aware of other players' winning positions and try to prevent them from winning");
        sb.AppendLine("6. Cards can only be used for their intended purpose (weapons in battle, defenses against attacks, etc.)");
        sb.AppendLine("7. You cannot play cards you don't have or use cards inappropriately");
        
        if (instruction != null)
        {
            sb.AppendLine("8. PRIORITIZE following your ally's instructions when possible and strategic");
            sb.AppendLine("9. If the ally instruction doesn't match available actions, choose the closest strategic alternative");
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
                BindingFlags.NonPublic | BindingFlags.Instance);
            
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

    private GameEvent? TryGetValidActionOfType(Type actionType, List<Type> events, int maxAttempts)
    {
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
        Console.WriteLine($"[PhaseAwareGemmaBot] {_player.Faction}: {message}");
        #endif
    }
    
    public void Dispose()
    {
        _ollamaClient?.Dispose();
    }
}