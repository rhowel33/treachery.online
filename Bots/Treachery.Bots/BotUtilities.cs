/*
 * Copyright (C) 2020-2025 Ronald Ossendrijver (admin@treachery.online)
 * This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version. This
 * program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License for more details. You should have
 * received a copy of the GNU General Public License along with this program. If not, see <http://www.gnu.org/licenses/>.
 */

using System.Reflection;
using System.Text.RegularExpressions;
using Treachery.Shared;

namespace Treachery.Bots;

/// <summary>
/// Shared utilities for bot implementations to eliminate code duplication
/// </summary>
public static class BotUtilities
{
    /// <summary>
    /// Parse action type from LLM response text
    /// Looks for patterns like "ACTION: BidForCard" or "I will perform: Move"
    /// </summary>
    public static Type? ParseActionFromResponse(string response, List<Type> availableActionTypes)
    {
        if (string.IsNullOrWhiteSpace(response))
            return null;

        // Try direct match first (e.g., "BidForCard")
        foreach (var actionType in availableActionTypes)
        {
            if (response.Contains(actionType.Name, StringComparison.OrdinalIgnoreCase))
            {
                return actionType;
            }
        }

        // Try common patterns
        var patterns = new[]
        {
            @"ACTION:\s*(\w+)",
            @"perform\s+(\w+)",
            @"choose\s+(\w+)",
            @"action\s+is\s+(\w+)",
            @"will\s+(\w+)",
            @"decision:\s*(\w+)"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(response, pattern, RegexOptions.IgnoreCase);
            if (match.Success && match.Groups.Count > 1)
            {
                var actionName = match.Groups[1].Value;
                var matchingType = availableActionTypes.FirstOrDefault(t =>
                    t.Name.Equals(actionName, StringComparison.OrdinalIgnoreCase));

                if (matchingType != null)
                    return matchingType;
            }
        }

        return null;
    }

    /// <summary>
    /// Create an action using reflection by calling ClassicBot's Determine method
    /// </summary>
    public static GameEvent? CreateActionUsingReflection(
        Game game,
        Player player,
        Type actionType,
        ClassicBot bot)
    {
        try
        {
            // Try standard pattern: Determine{ActionName}
            var methodName = $"Determine{actionType.Name}";
            var method = typeof(ClassicBot).GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            if (method != null)
            {
                var result = method.Invoke(bot, null);
                return result as GameEvent;
            }

            // Try alternate patterns for special cases
            var alternateNames = new[]
            {
                actionType.Name,
                $"Get{actionType.Name}",
                $"Create{actionType.Name}"
            };

            foreach (var alternateName in alternateNames)
            {
                method = typeof(ClassicBot).GetMethod(
                    alternateName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if (method != null)
                {
                    var result = method.Invoke(bot, null);
                    return result as GameEvent;
                }
            }

#if DEBUG
            Console.WriteLine($"[BotUtilities] No method found for action type: {actionType.Name}");
            Console.WriteLine($"[BotUtilities] Available methods: {string.Join(", ", typeof(ClassicBot).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Where(m => m.Name.StartsWith("Determine")).Select(m => m.Name))}");
#endif

            return null;
        }
        catch (Exception ex)
        {
#if DEBUG
            Console.WriteLine($"[BotUtilities] Failed to create action {actionType.Name}: {ex.Message}");
#endif
            return null;
        }
    }

    /// <summary>
    /// Determine if a phase should use the LLM or fall back to ClassicBot
    /// </summary>
    public static bool ShouldUseGemmaForPhase(Game game, Player player)
    {
        // Use ClassicBot for deterministic/simple phases
        if (game.CurrentPhase == Phase.BlowA ||
            game.CurrentPhase == Phase.BlowB ||
            game.CurrentPhase == Phase.Contemplate ||
            game.CurrentPhase == Phase.CharityClaimed ||
            game.CurrentPhase == Phase.Resurrection ||
            game.CurrentPhase == Phase.ShipmentAndMoveConcluded ||
            game.CurrentPhase == Phase.BlowReport ||
            game.CurrentPhase == Phase.Collection)
        {
            return false;
        }

        // Use ClassicBot for storm
        if (game.CurrentMainPhase == MainPhase.Storm)
        {
            return false;
        }

        // Use ClassicBot for charity
        if (game.CurrentMainPhase == MainPhase.Charity)
        {
            return false;
        }

        // Use ClassicBot for bidding (but could use Gemma for strategy)
        if (game.CurrentMainPhase == MainPhase.Bidding)
        {
            return false;
        }

        // Use ClassicBot for revival (simple decision)
        if (game.CurrentMainPhase == MainPhase.Revival)
        {
            return false;
        }

        // Use Gemma for strategic phases: battles, movement, deals, alliances
        if (game.CurrentMainPhase == MainPhase.Battle ||
            game.CurrentMainPhase == MainPhase.ShipmentAndMove ||
            game.CurrentMainPhase == MainPhase.Mentat ||
            game.CurrentMainPhase == MainPhase.ClaimingCharity)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Log tool calls for debugging in local games
    /// </summary>
    public static void LogToolCall(string toolName, object arguments, string? result = null)
    {
#if DEBUG
        Console.WriteLine($"[Tool Call] {toolName}");
        Console.WriteLine($"  Arguments: {System.Text.Json.JsonSerializer.Serialize(arguments)}");
        if (result != null)
        {
            var truncatedResult = result.Length > 200 ? result.Substring(0, 200) + "..." : result;
            Console.WriteLine($"  Result: {truncatedResult}");
        }
#endif
    }

    /// <summary>
    /// Track invalid action attempts for analysis
    /// </summary>
    public static void LogInvalidAction(string actionType, string reason, Game game, Player player)
    {
#if DEBUG
        Console.WriteLine($"[Invalid Action] {player.Faction} attempted {actionType}");
        Console.WriteLine($"  Reason: {reason}");
        Console.WriteLine($"  Phase: {game.CurrentPhase} / {game.CurrentMainPhase}");
#endif

        // TODO: Could log to metrics system for analysis
        // For now, just debug output
    }

    /// <summary>
    /// Get a description of available actions for the prompt
    /// </summary>
    public static string GetAvailableActionsDescription(List<Type> availableActionTypes)
    {
        var descriptions = new List<string>();

        foreach (var actionType in availableActionTypes)
        {
            // Add common descriptions for known action types
            var description = actionType.Name switch
            {
                "Move" => "Move - Relocate forces to a different territory",
                "Ship" => "Ship - Ship forces from reserves to the board",
                "BattlePlan" => "BattlePlan - Plan your battle strategy (forces, leader, cards)",
                "BidForCard" => "BidForCard - Bid spice for a treachery card",
                "ClaimCharity" => "ClaimCharity - Claim charity spice if eligible",
                "PassBid" => "PassBid - Pass on the current card being auctioned",
                "Alliance" => "Alliance - Form an alliance with another faction",
                "AcceptOrCancelDeal" => "AcceptOrCancelDeal - Accept or reject a proposed deal",
                "EndPhase" => "EndPhase - End your current turn/phase",
                _ => $"{actionType.Name} - Perform {actionType.Name} action"
            };

            descriptions.Add(description);
        }

        return string.Join("\n", descriptions);
    }

    /// <summary>
    /// Format tool call results for LLM consumption
    /// </summary>
    public static string FormatToolResult(string toolName, string result)
    {
        return $"=== {toolName} Result ===\n{result}\n";
    }
}
