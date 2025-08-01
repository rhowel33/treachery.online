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
using System.Text.Json;

namespace Treachery.Bots;

/// <summary>
/// Enhanced Gemma bot with structured data access and comprehensive validation
/// Provides better context than text-only prompts and validates all actions before execution
/// </summary>
public class StructuredGemmaBot : IBot
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

    public StructuredGemmaBot(Game game, Player player, BotParameters parameters, string ollamaUrl = "http://localhost:11434", string model = "gemma3:latest", string gameId = "")
    {
        _game = game;
        _player = player;
        _ollamaClient = new OllamaClient(ollamaUrl, model);
        _fallbackBot = new ClassicBot(game, player, parameters);
        _gameId = gameId;
    }

    public GameEvent? DetermineHighestPriorityInPhaseAction(List<Type> events)
    {
        return DetermineActionWithStructuredContext(events, "HighestPriority");
    }

    public GameEvent? DetermineHighPriorityInPhaseAction(List<Type> events)
    {
        return DetermineActionWithStructuredContext(events, "HighPriority");
    }

    public GameEvent? DetermineMiddlePriorityInPhaseAction(List<Type> events)
    {
        return DetermineActionWithStructuredContext(events, "MiddlePriority");
    }

    public GameEvent? DetermineLowPriorityInPhaseAction(List<Type> events)
    {
        return DetermineActionWithStructuredContext(events, "LowPriority");
    }

    public GameEvent? DetermineEndPhaseAction(List<Type> events)
    {
        return DetermineActionWithStructuredContext(events, "EndPhase");
    }

    private GameEvent? DetermineActionWithStructuredContext(List<Type> events, string priority)
    {
        if (!events.Any())
            return null;

        var currentPhase = _game.CurrentPhase;
        var currentMainPhase = _game.CurrentMainPhase;
        
        LogInfo($"StructuredGemmaBot determining {priority} action in {currentPhase}/{currentMainPhase} from {events.Count} options");

        // Use same phase-aware logic as PhaseAwareGemmaBot
        if (ShouldUseGemmaForPhase(currentPhase, currentMainPhase, events, priority))
        {
            LogInfo("Using Gemma with structured context for strategic decision");
            return DetermineActionWithStructuredDataAsync(events, priority).GetAwaiter().GetResult();
        }
        else
        {
            LogInfo("Using ClassicBot for this phase/action combination");
            return GetFallbackAction(events, priority);
        }
    }

    private async Task<GameEvent?> DetermineActionWithStructuredDataAsync(List<Type> events, string priority)
    {
        try
        {
            LogInfo($"Structured GemmaBot determining {priority} action from {events.Count} options");
            
            // Get structured game data
            var gameAnalysis = GetStructuredGameAnalysis(events);
            var legalActions = GetPreValidatedLegalActions(events);
            var humanInstruction = GetHumanAllyInstruction();
            
            // Create enhanced prompt with structured data
            var prompt = CreateStructuredPrompt(gameAnalysis, legalActions, humanInstruction, priority);
            
            LogInfo("Sending structured request to Gemma...");
            
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
            
            // Verify the action is in our pre-validated list
            if (!legalActions.ValidatedActions.Any(a => a.ActionType == chosenActionType.Name))
            {
                LogInfo($"Gemma chose non-validated action {chosenActionType.Name}, falling back to ClassicBot");
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
                var validation = action.Validate();
                if (validation == null)
                {
                    LogInfo($"Structured Gemma chose valid action: {action.GetMessage()}" + 
                           (humanInstruction != null ? $" (following instruction: {humanInstruction.Summary})" : ""));
                    return action;
                }
                else
                {
                    LogInfo($"Final validation failed ({validation}), trying alternatives");
                    
                    // Try to get a valid action of the same type with multiple attempts
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
            LogInfo($"Error with Structured Gemma decision: {ex.Message}");
        }
        
        return GetFallbackAction(events, priority);
    }

    private StructuredGameAnalysis GetStructuredGameAnalysis(List<Type> availableActions)
    {
        return new StructuredGameAnalysis
        {
            GameState = new GameStateInfo
            {
                CurrentTurn = _game.CurrentTurn,
                MaxTurns = _game.MaximumTurns,
                Phase = _game.CurrentPhase.ToString(),
                MainPhase = _game.CurrentMainPhase.ToString(),
                StormSector = _game.SectorInStorm,
                NextStormSector = (_game.SectorInStorm + _game.NextStormMoves) % 18
            },
            
            PlayerStatus = new PlayerStatusInfo
            {
                Faction = _player.Faction.ToString(),
                Resources = _player.Resources,
                Forces = _player.ForcesInReserve,
                SpecialForces = _player.SpecialForcesInReserve,
                TreacheryCards = _player.TreacheryCards.Count(),
                MaxCards = _player.MaximumNumberOfCards,
                Leaders = new List<string>(), // Simplified for now  
                CapturedLeaders = new List<string>(), // Simplified for now
                Ally = _player.HasAlly ? _player.Ally.ToString() : null,
                StrongholdsControlled = _game.NumberOfVictoryPoints(_player, true),
                VictoryPoints = _game.NumberOfVictoryPoints(_player, true)
            },
            
            Threats = AnalyzeThreats(),
            Opportunities = AnalyzeOpportunities(),
            
            ResourceMap = _game.ResourcesOnPlanet
                .Where(kvp => kvp.Value > 0)
                .ToDictionary(kvp => kvp.Key.ToString(), kvp => kvp.Value),
                
            FactionSpecificInfo = GetFactionSpecificInfo()
        };
    }

    private PreValidatedActions GetPreValidatedLegalActions(List<Type> events)
    {
        var validatedActions = new List<ValidatedAction>();
        
        foreach (var eventType in events)
        {
            try
            {
                // Try to create the action to see if it would be valid
                var testAction = CreateActionUsingReflection(eventType);
                var isValid = testAction?.Validate() == null;
                
                validatedActions.Add(new ValidatedAction
                {
                    ActionType = eventType.Name,
                    IsValid = isValid,
                    Description = GetActionDescription(eventType),
                    ValidationResult = isValid ? "Valid" : testAction?.Validate()?.ToString() ?? "Could not create action",
                    StrategicImportance = GetStrategicImportance(eventType)
                });
            }
            catch (Exception ex)
            {
                validatedActions.Add(new ValidatedAction
                {
                    ActionType = eventType.Name,
                    IsValid = false,
                    Description = GetActionDescription(eventType),
                    ValidationResult = $"Exception during validation: {ex.Message}",
                    StrategicImportance = "Unknown"
                });
            }
        }
        
        return new PreValidatedActions
        {
            ValidatedActions = validatedActions,
            RecommendedActions = validatedActions
                .Where(a => a.IsValid && a.StrategicImportance != "Low")
                .OrderByDescending(a => GetImportanceScore(a.StrategicImportance))
                .Take(3)
                .ToList()
        };
    }

    private string CreateStructuredPrompt(StructuredGameAnalysis analysis, PreValidatedActions actions, BotInstructions? instruction, string priority)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine("You are an AI playing the Dune board game. You have access to structured game analysis and pre-validated actions.");
        sb.AppendLine("Use this detailed information to make the optimal strategic decision.");
        sb.AppendLine();

        if (instruction != null)
        {
            sb.AppendLine("🤝 ALLY INSTRUCTION:");
            sb.AppendLine($"Your human ally instructed: \"{instruction.Details}\"");
            sb.AppendLine($"Summary: {instruction.Summary} | Priority: {instruction.Priority}");
            sb.AppendLine();
        }

        sb.AppendLine("📊 STRUCTURED GAME ANALYSIS:");
        sb.AppendLine(JsonSerializer.Serialize(analysis, new JsonSerializerOptions { WriteIndented = true }));
        sb.AppendLine();

        sb.AppendLine("⚖️ PRE-VALIDATED LEGAL ACTIONS:");
        sb.AppendLine(JsonSerializer.Serialize(actions, new JsonSerializerOptions { WriteIndented = true }));
        sb.AppendLine();

        sb.AppendLine("🎯 DECISION FRAMEWORK:");
        sb.AppendLine("1. Consider your win condition progress (need 3+ strongholds or faction-specific win)");
        sb.AppendLine("2. Evaluate threats from other players (especially those with 2+ strongholds)");
        sb.AppendLine("3. Assess resource efficiency and positioning advantages");
        sb.AppendLine("4. Think about alliance coordination and deal opportunities");
        sb.AppendLine("5. Factor in remaining turns and urgency of actions");
        
        if (instruction != null)
        {
            sb.AppendLine("6. PRIORITIZE your ally's strategic guidance when applicable");
        }
        
        sb.AppendLine();
        sb.AppendLine("RESPONSE REQUIREMENTS:");
        sb.AppendLine("- Respond with EXACTLY ONE action name from the ValidatedActions list above");
        sb.AppendLine("- Only choose actions marked as IsValid: true");
        sb.AppendLine("- Consider RecommendedActions for highest strategic value");
        sb.AppendLine();
        sb.AppendLine("Valid action choices:");
        foreach (var action in actions.ValidatedActions.Where(a => a.IsValid).Take(5))
        {
            sb.AppendLine($"  {action.ActionType} - {action.Description} ({action.StrategicImportance} importance)");
        }
        sb.AppendLine();
        sb.AppendLine("Your strategic choice:");
        
        return sb.ToString();
    }

    // Helper methods for analysis
    private List<ThreatInfo> AnalyzeThreats()
    {
        var threats = new List<ThreatInfo>();
        
        foreach (var opponent in _game.Players.Where(p => p != _player))
        {
            var victoryPoints = _game.NumberOfVictoryPoints(opponent, true);
            var totalForces = opponent.ForcesOnPlanet.Sum(f => f.Value.TotalAmountOfForces);
            
            if (victoryPoints >= 2 || totalForces >= 15)
            {
                threats.Add(new ThreatInfo
                {
                    Faction = opponent.Faction.ToString(),
                    ThreatLevel = victoryPoints >= 3 ? "Critical" : victoryPoints >= 2 ? "High" : "Medium",
                    Strongholds = victoryPoints,
                    VictoryPoints = victoryPoints,
                    Resources = opponent.Resources,
                    Forces = opponent.ForcesInReserve + opponent.SpecialForcesInReserve
                });
            }
        }
        
        return threats.OrderByDescending(t => t.Strongholds).ToList();
    }

    private List<OpportunityInfo> AnalyzeOpportunities()
    {
        var opportunities = new List<OpportunityInfo>();
        
        // Check for available strongholds (simplified)
        var strongholds = _game.Map.Strongholds.Take(3);
            
        foreach (var stronghold in strongholds)
        {
            opportunities.Add(new OpportunityInfo
            {
                Type = "Stronghold",
                Description = $"Capture {stronghold}",
                StrategicValue = "High",
                Requirements = $"Forces and movement to {stronghold}"
            });
        }
        
        // Check for spice opportunities
        var richSpiceLocations = _game.ResourcesOnPlanet
            .Where(kvp => kvp.Value >= 3)
            .OrderByDescending(kvp => kvp.Value)
            .Take(2);
            
        foreach (var spiceLocation in richSpiceLocations)
        {
            opportunities.Add(new OpportunityInfo
            {
                Type = "Resources",
                Description = $"Harvest {spiceLocation.Value} spice from {spiceLocation.Key}",
                StrategicValue = "Medium",
                Requirements = "Forces for harvesting"
            });
        }
        
        return opportunities;
    }

    private object GetFactionSpecificInfo()
    {
        return _player.Faction switch
        {
            Faction.Green => new { 
                Advantage = "Prescience - see opponent's battle plans (Atreides)",
                Strategy = "Use foreknowledge in battles and bidding"
            },
            Faction.Black => new { 
                Advantage = "Traitors - capture enemy leaders (Harkonnen)",
                Strategy = "Use traitor cards to win key battles"
            },
            Faction.Blue => new { 
                Advantage = "Desert movement and storm immunity (Fremen)", 
                Strategy = "Leverage desert mobility and Sietch Tabr stronghold"
            },
            Faction.Red => new { 
                Advantage = "Sardaukar elite troops and spice taxes (Emperor)",
                Strategy = "Use superior forces and economic advantages"
            },
            Faction.Orange => new { 
                Advantage = "Shipping control and spice fees (Guild)",
                Strategy = "Control transportation and collect shipping fees"
            },
            Faction.Yellow => new { 
                Advantage = "Voice power and spiritual influence (Bene Gesserit)",
                Strategy = "Use voice and prediction to achieve victory conditions"
            },
            _ => new { Advantage = "Standard faction", Strategy = "Control 3+ strongholds" }
        };
    }

    private string GetActionDescription(Type actionType)
    {
        return actionType.Name switch
        {
            "Move" => "Move forces between territories for positioning",
            "Battle" => "Initiate combat to capture territory or eliminate enemies",
            "Bid" => "Bid on treachery cards for strategic advantages",
            "Shipment" => "Bring reinforcements from off-world reserves",
            "Revival" => "Revive killed leaders to restore combat effectiveness",
            "AllianceOffered" => "Form alliance for mutual benefit and coordination",
            "DealOffered" => "Propose trade or cooperation agreement",
            "EndPhase" => "End current phase to advance game",
            "Caravan" => "Move spice with military escort",
            _ => $"Execute {actionType.Name} action"
        };
    }

    private string GetStrategicImportance(Type actionType)
    {
        return actionType.Name switch
        {
            "Move" => "High",
            "Battle" => "High", 
            "Shipment" => "High",
            "AllianceOffered" => "High",
            "DealOffered" => "Medium",
            "Bid" => "Medium",
            "Revival" => "Medium",
            "EndPhase" => "Low",
            _ => "Medium"
        };
    }

    private int GetImportanceScore(string importance)
    {
        return importance switch
        {
            "High" => 3,
            "Medium" => 2,
            "Low" => 1,
            _ => 0
        };
    }

    // Reuse methods from other bots for consistency
    private bool ShouldUseGemmaForPhase(Phase currentPhase, MainPhase currentMainPhase, List<Type> events, string priority)
    {
        // Same logic as PhaseAwareGemmaBot
        if (events.Any(e => e.Name.Contains("DealOffered") || e.Name.Contains("DealAccepted")))
            return true;

        if (events.Any(e => e.Name.Contains("Alliance") || e.Name.Contains("AllyPermission")))
            return true;

        return currentMainPhase switch
        {
            MainPhase.Storm => false,
            MainPhase.Blow => events.Any(e => 
                e.Name.Contains("Harvester") || 
                e.Name.Contains("FamilyAtomics") ||
                e.Name.Contains("Thumper") ||
                e.Name.Contains("WeatherControl")),
            MainPhase.Charity => false,
            MainPhase.Bidding => ShouldUseGemmaForBidding(events),
            MainPhase.Resurrection => false,
            MainPhase.ShipmentAndMove => true,
            MainPhase.Battle => true,
            MainPhase.Collection => false,
            MainPhase.Contemplate => false,
            _ => events.Any(e => 
                e.Name.Contains("Move") || 
                e.Name.Contains("Battle") || 
                e.Name.Contains("Shipment") ||
                e.Name.Contains("Alliance") ||
                e.Name.Contains("Deal"))
        };
    }

    private bool ShouldUseGemmaForBidding(List<Type> events)
    {
        if (!_hasDeterminedBiddingStrategy && events.Any(e => e.Name.Contains("Bid")))
        {
            _hasDeterminedBiddingStrategy = true;
            return true;
        }
        return false;
    }

    // Reuse utility methods from existing bots
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
        Console.WriteLine($"[StructuredGemmaBot] {_player.Faction}: {message}");
        #endif
    }
    
    public void Dispose()
    {
        _ollamaClient?.Dispose();
    }
}

// Data structures for structured analysis
public class StructuredGameAnalysis
{
    public GameStateInfo GameState { get; set; } = new();
    public PlayerStatusInfo PlayerStatus { get; set; } = new();
    public List<ThreatInfo> Threats { get; set; } = new();
    public List<OpportunityInfo> Opportunities { get; set; } = new();
    public Dictionary<string, int> ResourceMap { get; set; } = new();
    public object FactionSpecificInfo { get; set; } = new();
}

public class GameStateInfo
{
    public int CurrentTurn { get; set; }
    public int MaxTurns { get; set; }
    public string Phase { get; set; } = "";
    public string MainPhase { get; set; } = "";
    public int StormSector { get; set; }
    public int NextStormSector { get; set; }
}

public class PlayerStatusInfo
{
    public string Faction { get; set; } = "";
    public int Resources { get; set; }
    public int Forces { get; set; }
    public int SpecialForces { get; set; }
    public int TreacheryCards { get; set; }
    public int MaxCards { get; set; }
    public List<string> Leaders { get; set; } = new();
    public List<string> CapturedLeaders { get; set; } = new();
    public string? Ally { get; set; }
    public int StrongholdsControlled { get; set; }
    public int VictoryPoints { get; set; }
}

public class ThreatInfo
{
    public string Faction { get; set; } = "";
    public string ThreatLevel { get; set; } = "";
    public int Strongholds { get; set; }
    public int VictoryPoints { get; set; }
    public int Resources { get; set; }
    public int Forces { get; set; }
}

public class OpportunityInfo
{
    public string Type { get; set; } = "";
    public string Description { get; set; } = "";
    public string StrategicValue { get; set; } = "";
    public string Requirements { get; set; } = "";
}

public class PreValidatedActions
{
    public List<ValidatedAction> ValidatedActions { get; set; } = new();
    public List<ValidatedAction> RecommendedActions { get; set; } = new();
}

public class ValidatedAction
{
    public string ActionType { get; set; } = "";
    public bool IsValid { get; set; }
    public string Description { get; set; } = "";
    public string ValidationResult { get; set; } = "";
    public string StrategicImportance { get; set; } = "";
}