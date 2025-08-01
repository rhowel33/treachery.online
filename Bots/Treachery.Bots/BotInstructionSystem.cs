/*
 * Copyright (C) 2020-2025 Ronald Ossendrijver (admin@treachery.online)
 * This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version. This
 * program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License for more details. You should have
 * received a copy of the GNU General Public License along with this program. If not, see <http://www.gnu.org/licenses/>.
 */

using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Treachery.Bots;

public static class BotInstructionSystem
{
    // Store instructions per game and faction
    private static readonly ConcurrentDictionary<string, Dictionary<Faction, BotInstructions>> _gameInstructions = new();
    
    public static void ProcessChatMessage(string gameId, GameChatMessage message, Game game)
    {
        var sourcePlayer = game.GetPlayerByUserId(message.SourceUserId);
        if (sourcePlayer == null || sourcePlayer.IsBot) return;
        
        // Check if this is a message to their bot ally
        var targetPlayer = message.TargetUserId > 0 ? game.GetPlayerByUserId(message.TargetUserId) : null;
        var isToAlly = targetPlayer != null && targetPlayer.IsBot && sourcePlayer.HasAlly && sourcePlayer.AlliedPlayer == targetPlayer;
        
        if (!isToAlly) return;
        
        var instruction = ParseBotInstruction(message.Body?.ToString() ?? "");
        if (instruction != null)
        {
            SetBotInstruction(gameId, targetPlayer.Faction, instruction);
            
            // Send confirmation message back
            var confirmationMessage = new GameChatMessage
            {
                SourceUserId = -1, // System message
                TargetUserId = message.SourceUserId,
                Body = $"✓ Instruction received for {targetPlayer.Faction}: {instruction.Summary}"
            };
            
            // Note: In a real implementation, you'd send this through the SignalR hub
        }
    }
    
    public static BotInstructions? GetBotInstruction(string gameId, Faction faction)
    {
        if (!_gameInstructions.TryGetValue(gameId, out var gameInstructions))
            return null;
            
        return gameInstructions.GetValueOrDefault(faction);
    }
    
    public static void SetBotInstruction(string gameId, Faction faction, BotInstructions instruction)
    {
        _gameInstructions.AddOrUpdate(gameId, 
            new Dictionary<Faction, BotInstructions> { { faction, instruction } },
            (key, existing) => 
            {
                existing[faction] = instruction;
                return existing;
            });
    }
    
    public static void ClearBotInstruction(string gameId, Faction faction)
    {
        if (_gameInstructions.TryGetValue(gameId, out var gameInstructions))
        {
            gameInstructions.Remove(faction);
        }
    }
    
    public static void ClearGameInstructions(string gameId)
    {
        _gameInstructions.TryRemove(gameId, out _);
    }
    
    private static BotInstructions? ParseBotInstruction(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return null;
        
        var lowerMessage = message.ToLowerInvariant().Trim();
        
        // Command patterns
        var patterns = new Dictionary<string, Func<string, BotInstructions?>>
        {
            // Movement instructions
            {@"move\s+(\d+)?\s*(?:forces?)?\s+(?:from\s+)?(\w+)\s+(?:to\s+)?(\w+)", ParseMoveInstruction},
            {@"attack\s+(\w+)(?:\s+with\s+(\d+))?\s*(?:forces?)?", ParseAttackInstruction},
            {@"defend\s+(\w+)", ParseDefendInstruction},
            {@"ship\s+(\d+)?\s*(?:forces?)?\s+(?:to\s+)?(\w+)", ParseShipmentInstruction},
            
            // Resource instructions  
            {@"bid\s+(\d+)(?:\s+on\s+(.+))?", ParseBidInstruction},
            {@"save\s+(\d+)\s+(?:resources?|spice)", ParseSaveResourcesInstruction},
            {@"spend\s+(?:up\s+to\s+)?(\d+)", ParseSpendResourcesInstruction},
            
            // Strategic instructions
            {@"focus\s+on\s+(.+)", ParseFocusInstruction},
            {@"avoid\s+(.+)", ParseAvoidInstruction},
            {@"prioritize\s+(.+)", ParsePriorityInstruction},
            {@"wait|pass|skip", ParseWaitInstruction},
            
            // Alliance instructions
            {@"ally\s+with\s+(\w+)", ParseAllyInstruction},
            {@"break\s+alliance", ParseBreakAllianceInstruction},
            
            // Card instructions
            {@"play\s+(.+)\s+card", ParsePlayCardInstruction},
            {@"keep\s+(.+)\s+card", ParseKeepCardInstruction},
        };
        
        foreach (var (pattern, parser) in patterns)
        {
            var match = Regex.Match(lowerMessage, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var instruction = parser(lowerMessage);
                if (instruction != null) return instruction;
            }
        }
        
        // General instruction if no specific pattern matches
        if (lowerMessage.Length > 10) // Avoid very short messages
        {
            return new BotInstructions
            {
                Type = InstructionType.General,
                Summary = "Follow general strategy",
                Details = message,
                Priority = InstructionPriority.Medium,
                CreatedAt = DateTime.UtcNow
            };
        }
        
        return null;
    }
    
    private static BotInstructions? ParseMoveInstruction(string message)
    {
        var match = Regex.Match(message, @"move\s+(\d+)?\s*(?:forces?)?\s+(?:from\s+)?(\w+)\s+(?:to\s+)?(\w+)", RegexOptions.IgnoreCase);
        if (!match.Success) return null;
        
        return new BotInstructions
        {
            Type = InstructionType.Movement,
            Summary = $"Move forces from {match.Groups[2].Value} to {match.Groups[3].Value}",
            Details = message,
            Priority = InstructionPriority.High,
            CreatedAt = DateTime.UtcNow,
            Parameters = new Dictionary<string, object>
            {
                ["fromLocation"] = match.Groups[2].Value,
                ["toLocation"] = match.Groups[3].Value,
                ["forceCount"] = match.Groups[1].Success ? int.Parse(match.Groups[1].Value) : -1
            }
        };
    }
    
    private static BotInstructions? ParseAttackInstruction(string message)
    {
        var match = Regex.Match(message, @"attack\s+(\w+)(?:\s+with\s+(\d+))?\s*(?:forces?)?", RegexOptions.IgnoreCase);
        if (!match.Success) return null;
        
        return new BotInstructions
        {
            Type = InstructionType.Combat,
            Summary = $"Attack {match.Groups[1].Value}",
            Details = message,
            Priority = InstructionPriority.High,
            CreatedAt = DateTime.UtcNow,
            Parameters = new Dictionary<string, object>
            {
                ["target"] = match.Groups[1].Value,
                ["forceCount"] = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : -1
            }
        };
    }
    
    private static BotInstructions? ParseDefendInstruction(string message)
    {
        var match = Regex.Match(message, @"defend\s+(\w+)", RegexOptions.IgnoreCase);
        if (!match.Success) return null;
        
        return new BotInstructions
        {
            Type = InstructionType.Defense,
            Summary = $"Defend {match.Groups[1].Value}",
            Details = message,
            Priority = InstructionPriority.High,
            CreatedAt = DateTime.UtcNow,
            Parameters = new Dictionary<string, object>
            {
                ["location"] = match.Groups[1].Value
            }
        };
    }
    
    private static BotInstructions? ParseShipmentInstruction(string message)
    {
        var match = Regex.Match(message, @"ship\s+(\d+)?\s*(?:forces?)?\s+(?:to\s+)?(\w+)", RegexOptions.IgnoreCase);
        if (!match.Success) return null;
        
        return new BotInstructions
        {
            Type = InstructionType.Shipment,
            Summary = $"Ship forces to {match.Groups[2].Value}",
            Details = message,
            Priority = InstructionPriority.High,
            CreatedAt = DateTime.UtcNow,
            Parameters = new Dictionary<string, object>
            {
                ["location"] = match.Groups[2].Value,
                ["forceCount"] = match.Groups[1].Success ? int.Parse(match.Groups[1].Value) : -1
            }
        };
    }
    
    private static BotInstructions? ParseBidInstruction(string message)
    {
        var match = Regex.Match(message, @"bid\s+(\d+)(?:\s+on\s+(.+))?", RegexOptions.IgnoreCase);
        if (!match.Success) return null;
        
        return new BotInstructions
        {
            Type = InstructionType.Bidding,
            Summary = $"Bid {match.Groups[1].Value} resources",
            Details = message,
            Priority = InstructionPriority.Medium,
            CreatedAt = DateTime.UtcNow,
            Parameters = new Dictionary<string, object>
            {
                ["amount"] = int.Parse(match.Groups[1].Value),
                ["cardType"] = match.Groups[2].Success ? match.Groups[2].Value : ""
            }
        };
    }
    
    private static BotInstructions? ParseSaveResourcesInstruction(string message)
    {
        var match = Regex.Match(message, @"save\s+(\d+)\s+(?:resources?|spice)", RegexOptions.IgnoreCase);
        if (!match.Success) return null;
        
        return new BotInstructions
        {
            Type = InstructionType.ResourceManagement,
            Summary = $"Save {match.Groups[1].Value} resources",
            Details = message,
            Priority = InstructionPriority.Medium,
            CreatedAt = DateTime.UtcNow,
            Parameters = new Dictionary<string, object>
            {
                ["saveAmount"] = int.Parse(match.Groups[1].Value)
            }
        };
    }
    
    private static BotInstructions? ParseSpendResourcesInstruction(string message)
    {
        var match = Regex.Match(message, @"spend\s+(?:up\s+to\s+)?(\d+)", RegexOptions.IgnoreCase);
        if (!match.Success) return null;
        
        return new BotInstructions
        {
            Type = InstructionType.ResourceManagement,
            Summary = $"Spend up to {match.Groups[1].Value} resources",
            Details = message,
            Priority = InstructionPriority.Medium,
            CreatedAt = DateTime.UtcNow,
            Parameters = new Dictionary<string, object>
            {
                ["maxSpend"] = int.Parse(match.Groups[1].Value)
            }
        };
    }
    
    private static BotInstructions? ParseFocusInstruction(string message)
    {
        var match = Regex.Match(message, @"focus\s+on\s+(.+)", RegexOptions.IgnoreCase);
        if (!match.Success) return null;
        
        return new BotInstructions
        {
            Type = InstructionType.Strategy,
            Summary = $"Focus on {match.Groups[1].Value}",
            Details = message,
            Priority = InstructionPriority.Medium,
            CreatedAt = DateTime.UtcNow,
            Parameters = new Dictionary<string, object>
            {
                ["focus"] = match.Groups[1].Value
            }
        };
    }
    
    private static BotInstructions? ParseAvoidInstruction(string message)
    {
        var match = Regex.Match(message, @"avoid\s+(.+)", RegexOptions.IgnoreCase);
        if (!match.Success) return null;
        
        return new BotInstructions
        {
            Type = InstructionType.Strategy,
            Summary = $"Avoid {match.Groups[1].Value}",
            Details = message,
            Priority = InstructionPriority.Medium,
            CreatedAt = DateTime.UtcNow,
            Parameters = new Dictionary<string, object>
            {
                ["avoid"] = match.Groups[1].Value
            }
        };
    }
    
    private static BotInstructions? ParsePriorityInstruction(string message)
    {
        var match = Regex.Match(message, @"prioritize\s+(.+)", RegexOptions.IgnoreCase);
        if (!match.Success) return null;
        
        return new BotInstructions
        {
            Type = InstructionType.Strategy,
            Summary = $"Prioritize {match.Groups[1].Value}",
            Details = message,
            Priority = InstructionPriority.High,
            CreatedAt = DateTime.UtcNow,
            Parameters = new Dictionary<string, object>
            {
                ["priority"] = match.Groups[1].Value
            }
        };
    }
    
    private static BotInstructions? ParseWaitInstruction(string message)
    {
        return new BotInstructions
        {
            Type = InstructionType.Wait,
            Summary = "Wait/Pass this turn",
            Details = message,
            Priority = InstructionPriority.High,
            CreatedAt = DateTime.UtcNow
        };
    }
    
    private static BotInstructions? ParseAllyInstruction(string message)
    {
        var match = Regex.Match(message, @"ally\s+with\s+(\w+)", RegexOptions.IgnoreCase);
        if (!match.Success) return null;
        
        return new BotInstructions
        {
            Type = InstructionType.Alliance,
            Summary = $"Ally with {match.Groups[1].Value}",
            Details = message,
            Priority = InstructionPriority.High,
            CreatedAt = DateTime.UtcNow,
            Parameters = new Dictionary<string, object>
            {
                ["target"] = match.Groups[1].Value
            }
        };
    }
    
    private static BotInstructions? ParseBreakAllianceInstruction(string message)
    {
        return new BotInstructions
        {
            Type = InstructionType.Alliance,
            Summary = "Break current alliance",
            Details = message,
            Priority = InstructionPriority.High,
            CreatedAt = DateTime.UtcNow,
            Parameters = new Dictionary<string, object>
            {
                ["action"] = "break"
            }
        };
    }
    
    private static BotInstructions? ParsePlayCardInstruction(string message)
    {
        var match = Regex.Match(message, @"play\s+(.+)\s+card", RegexOptions.IgnoreCase);
        if (!match.Success) return null;
        
        return new BotInstructions
        {
            Type = InstructionType.CardPlay,
            Summary = $"Play {match.Groups[1].Value} card",
            Details = message,
            Priority = InstructionPriority.Medium,
            CreatedAt = DateTime.UtcNow,
            Parameters = new Dictionary<string, object>
            {
                ["cardType"] = match.Groups[1].Value
            }
        };
    }
    
    private static BotInstructions? ParseKeepCardInstruction(string message)
    {
        var match = Regex.Match(message, @"keep\s+(.+)\s+card", RegexOptions.IgnoreCase);
        if (!match.Success) return null;
        
        return new BotInstructions
        {
            Type = InstructionType.CardPlay,
            Summary = $"Keep {match.Groups[1].Value} card",
            Details = message,
            Priority = InstructionPriority.Low,
            CreatedAt = DateTime.UtcNow,
            Parameters = new Dictionary<string, object>
            {
                ["cardType"] = match.Groups[1].Value,
                ["action"] = "keep"
            }
        };
    }
}

public class BotInstructions
{
    public InstructionType Type { get; set; }
    public string Summary { get; set; } = "";
    public string Details { get; set; } = "";
    public InstructionPriority Priority { get; set; }
    public DateTime CreatedAt { get; set; }
    public Dictionary<string, object> Parameters { get; set; } = new();
    
    public bool IsExpired(TimeSpan maxAge) => DateTime.UtcNow - CreatedAt > maxAge;
}

public enum InstructionType
{
    General,
    Movement,
    Combat,
    Defense,
    Shipment,
    Bidding,
    ResourceManagement,
    Strategy,
    Wait,
    Alliance,
    CardPlay
}

public enum InstructionPriority
{
    Low,
    Medium,
    High
}