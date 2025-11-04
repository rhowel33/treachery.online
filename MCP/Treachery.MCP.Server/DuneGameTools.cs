using System.ComponentModel;
using ModelContextProtocol.Sdk;
using System.Text.Json;
using Treachery.Shared;
using Treachery.Bots;

namespace Treachery.MCP.Server;

public class DuneGameTools
{
    private readonly Dictionary<string, Game> _activeGames = new();
    private readonly Dictionary<string, Player> _playersByGameId = new();

    [Description("Get the current game state with all relevant information for strategic decision making")]
    public async Task<string> GetGameState(
        [Description("The game ID")] string gameId,
        [Description("The faction making the decision")] string faction)
    {
        if (!_activeGames.TryGetValue(gameId, out var game))
            return "Game not found";

        var player = game.Players.FirstOrDefault(p => p.Faction.ToString() == faction);
        if (player == null)
            return "Player not found";

        // Use existing serializer from the bot system
        var availableActions = game.GetApplicableEvents(player, false);
        return GameStateSerializer.SerializeGameStateForLLM(game, player, availableActions);
    }

    [Description("Get all legal actions available to a player in the current game state")]
    public async Task<string> GetLegalActions(
        [Description("The game ID")] string gameId,
        [Description("The faction to get actions for")] string faction)
    {
        if (!_activeGames.TryGetValue(gameId, out var game))
            return "Game not found";

        var player = game.Players.FirstOrDefault(p => p.Faction.ToString() == faction);
        if (player == null)
            return "Player not found";

        var availableActions = game.GetApplicableEvents(player, false);
        
        var result = new
        {
            GameId = gameId,
            Faction = faction,
            CurrentPhase = game.CurrentPhase.ToString(),
            CurrentMainPhase = game.CurrentMainPhase.ToString(),
            AvailableActions = availableActions.Select(action => new
            {
                ActionType = action.Name,
                ActionDescription = GetActionDescription(action)
            }).ToList()
        };

        return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
    }

    [Description("Validate if a specific action is legal for a player in the current game state")]
    public async Task<string> ValidateAction(
        [Description("The game ID")] string gameId,
        [Description("The faction performing the action")] string faction,
        [Description("The action type to validate")] string actionType)
    {
        if (!_activeGames.TryGetValue(gameId, out var game))
            return JsonSerializer.Serialize(new { IsValid = false, Reason = "Game not found" });

        var player = game.Players.FirstOrDefault(p => p.Faction.ToString() == faction);
        if (player == null)
            return JsonSerializer.Serialize(new { IsValid = false, Reason = "Player not found" });

        var availableActions = game.GetApplicableEvents(player, false);
        var requestedAction = availableActions.FirstOrDefault(a => a.Name == actionType);

        if (requestedAction == null)
        {
            return JsonSerializer.Serialize(new
            {
                IsValid = false,
                Reason = $"Action '{actionType}' is not available",
                AvailableActions = availableActions.Select(a => a.Name).ToList()
            });
        }

        // Try to create the action using ClassicBot to validate it
        try
        {
            var classicBot = new ClassicBot(game, player, BotParameters.GetDefaultParameters(player.Faction));
            var action = CreateActionUsingReflection(classicBot, requestedAction);
            
            if (action != null)
            {
                var validation = action.Validate();
                if (validation == null)
                {
                    return JsonSerializer.Serialize(new
                    {
                        IsValid = true,
                        ActionType = actionType,
                        ActionMessage = action.GetMessage()?.ToString() ?? "Action created successfully"
                    });
                }
                else
                {
                    return JsonSerializer.Serialize(new
                    {
                        IsValid = false,
                        Reason = validation.ToString(),
                        ActionType = actionType
                    });
                }
            }
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new
            {
                IsValid = false,
                Reason = $"Error validating action: {ex.Message}",
                ActionType = actionType
            });
        }

        return JsonSerializer.Serialize(new
        {
            IsValid = false,
            Reason = "Could not create action instance",
            ActionType = actionType
        });
    }

    [Description("Get strategic analysis of the current game state for decision making")]
    public async Task<string> GetStrategicAnalysis(
        [Description("The game ID")] string gameId,
        [Description("The faction to analyze for")] string faction)
    {
        if (!_activeGames.TryGetValue(gameId, out var game))
            return "Game not found";

        var player = game.Players.FirstOrDefault(p => p.Faction.ToString() == faction);
        if (player == null)
            return "Player not found";

        var analysis = new
        {
            GameId = gameId,
            Faction = faction,
            CurrentTurn = game.CurrentTurn,
            MaxTurns = game.MaximumTurns,
            Phase = game.CurrentPhase.ToString(),
            MainPhase = game.CurrentMainPhase.ToString(),
            
            PlayerStatus = new
            {
                Resources = player.Resources,
                Forces = player.ForcesInReserve,
                SpecialForces = player.SpecialForces,
                TreacheryCards = player.TreacheryCards.Count(),
                MaxCards = player.MaximumNumberOfCards,
                Leaders = player.Leaders.Where(l => !l.Captured).Select(l => l.Name).ToList(),
                CapturedLeaders = player.Leaders.Where(l => l.Captured).Select(l => l.Name).ToList(),
                Ally = player.HasAlly ? player.Ally.ToString() : null
            },
            
            WinConditions = new
            {
                StrongholdsControlled = game.StrongholdsControlledBy(player.Faction).Count(),
                StrongholdsNeeded = 3,
                VictoryPoints = game.NumberOfVictoryPoints(player),
                FactionSpecificWinCondition = GetFactionSpecificWinCondition(player.Faction, game)
            },
            
            Threats = AnalyzeThreatLevel(game, player),
            
            Resources = new
            {
                SpiceOnBoard = game.ResourcesOnPlanet.Where(kvp => kvp.Value > 0).ToDictionary(kvp => kvp.Key.ToString(), kvp => kvp.Value),
                StormPosition = game.SectorInStorm,
                NextStormSector = (game.SectorInStorm + game.NextStormMoves) % 18
            }
        };

        return JsonSerializer.Serialize(analysis, new JsonSerializerOptions { WriteIndented = true });
    }

    [Description("Get information about available human ally instructions")]
    public async Task<string> GetAllyInstructions(
        [Description("The game ID")] string gameId,
        [Description("The faction to check instructions for")] string faction)
    {
        var instruction = BotInstructionSystem.GetBotInstruction(gameId, Enum.Parse<Faction>(faction));
        
        if (instruction == null)
        {
            return JsonSerializer.Serialize(new { HasInstructions = false });
        }

        return JsonSerializer.Serialize(new
        {
            HasInstructions = true,
            Type = instruction.Type.ToString(),
            Priority = instruction.Priority.ToString(),
            Summary = instruction.Summary,
            Details = instruction.Details,
            IsExpired = instruction.IsExpired(TimeSpan.FromMinutes(10)),
            CreatedAt = instruction.CreatedAt
        });
    }

    [Description("Register a game for MCP access")]
    public async Task<string> RegisterGame(
        [Description("The game ID")] string gameId,
        [Description("Serialized game state")] string gameStateJson)
    {
        try
        {
            var game = JsonSerializer.Deserialize<Game>(gameStateJson);
            if (game != null)
            {
                _activeGames[gameId] = game;
                return $"Game {gameId} registered successfully";
            }
            return "Failed to deserialize game state";
        }
        catch (Exception ex)
        {
            return $"Error registering game: {ex.Message}";
        }
    }

    [Description("Update game state for ongoing monitoring")]
    public async Task<string> UpdateGameState(
        [Description("The game ID")] string gameId,
        [Description("Updated serialized game state")] string gameStateJson)
    {
        try
        {
            var game = JsonSerializer.Deserialize<Game>(gameStateJson);
            if (game != null)
            {
                _activeGames[gameId] = game;
                return $"Game {gameId} updated successfully";
            }
            return "Failed to deserialize game state";
        }
        catch (Exception ex)
        {
            return $"Error updating game: {ex.Message}";
        }
    }

    private string GetActionDescription(Type actionType)
    {
        // Provide user-friendly descriptions for actions
        return actionType.Name switch
        {
            "Move" => "Move forces between territories",
            "Battle" => "Initiate or participate in battle",
            "Bid" => "Bid on treachery cards",
            "Shipment" => "Bring reinforcements from off-world",
            "Revival" => "Revive killed leaders",
            "AllianceOffered" => "Offer alliance to another faction",
            "DealOffered" => "Propose a deal with another player",
            "EndPhase" => "End current phase",
            "Caravan" => "Move spice with forces",
            _ => $"Perform {actionType.Name} action"
        };
    }

    private GameEvent? CreateActionUsingReflection(ClassicBot classicBot, Type actionType)
    {
        try
        {
            var methodName = $"Determine{actionType.Name}";
            var method = typeof(ClassicBot).GetMethod(methodName, 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            if (method != null)
            {
                var result = method.Invoke(classicBot, null);
                return result as GameEvent;
            }
        }
        catch (Exception)
        {
            // Swallow reflection errors
        }
        
        return null;
    }

    private object GetFactionSpecificWinCondition(Faction faction, Game game)
    {
        return faction switch
        {
            Faction.Atreides => new { Type = "Prediction", Description = "Win by correct predictions about battles" },
            Faction.Harkonnen => new { Type = "Traitors", Description = "Win using traitor cards effectively" },
            Faction.Fremen => new { Type = "Storm", Description = "Special advantages in storm and desert" },
            Faction.Emperor => new { Type = "Spice", Description = "Economic advantages and Sardaukar" },
            Faction.Guild => new { Type = "Shipping", Description = "Control shipping and collect fees" },
            Faction.BeneGesserit => new { Type = "Prediction", Description = "Win by controlling the right person at the right time" },
            _ => new { Type = "Standard", Description = "Control 3+ strongholds" }
        };
    }

    private object AnalyzeThreatLevel(Game game, Player player)
    {
        var threats = new List<object>();

        foreach (var opponent in game.Players.Where(p => p != player && !p.IsBot))
        {
            var strongholds = game.StrongholdsControlledBy(opponent.Faction).Count();
            if (strongholds >= 2)
            {
                threats.Add(new
                {
                    Faction = opponent.Faction.ToString(),
                    ThreatLevel = "High",
                    Reason = $"Controls {strongholds} strongholds",
                    VictoryPoints = game.NumberOfVictoryPoints(opponent)
                });
            }
        }

        return threats;
    }
}