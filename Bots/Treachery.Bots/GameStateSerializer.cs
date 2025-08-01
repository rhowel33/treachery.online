/*
 * Copyright (C) 2020-2025 Ronald Ossendrijver (admin@treachery.online)
 * This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version. This
 * program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License for more details. You should have
 * received a copy of the GNU General Public License along with this program. If not, see <http://www.gnu.org/licenses/>.
 */

using System.Text;

namespace Treachery.Bots;

public static class GameStateSerializer
{
    public static string SerializeGameStateForLLM(Game game, Player currentPlayer, List<Type> availableActions)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine("# DUNE BOARD GAME STATE");
        sb.AppendLine();
        
        // Basic game info
        sb.AppendLine($"## Game Status");
        sb.AppendLine($"Turn: {game.CurrentTurn}/{game.MaximumTurns}");
        sb.AppendLine($"Phase: {game.CurrentPhase}");
        sb.AppendLine($"Current Player: {currentPlayer.Faction}");
        sb.AppendLine();
        
        // Victory conditions
        sb.AppendLine("## Victory Status");
        foreach (var player in game.Players)
        {
            var isWinning = game.MeetsNormalVictoryCondition(player, true);
            var victoryPoints = game.NumberOfVictoryPoints(player, true);
            sb.AppendLine($"{player.Faction}: {victoryPoints} victory points{(isWinning ? " (WINNING!)" : "")}");
        }
        sb.AppendLine();
        
        // Current player details
        sb.AppendLine($"## Your Status ({currentPlayer.Faction})");
        sb.AppendLine($"Resources: {currentPlayer.Resources}");
        sb.AppendLine($"Forces in Reserve: {currentPlayer.ForcesInReserve}");
        sb.AppendLine($"Special Forces in Reserve: {currentPlayer.SpecialForcesInReserve}");
        sb.AppendLine($"Treachery Cards: {currentPlayer.TreacheryCards.Count()}/{currentPlayer.MaximumNumberOfCards}");
        
        if (currentPlayer.HasAlly)
        {
            sb.AppendLine($"Allied with: {currentPlayer.Ally}");
        }
        
        // Known treachery cards
        var knownCards = game.KnownCards(currentPlayer).ToList();
        if (knownCards.Any())
        {
            sb.AppendLine("Known Cards in Hand:");
            foreach (var card in currentPlayer.TreacheryCards.Where(c => knownCards.Contains(c)))
            {
                sb.AppendLine($"  - {card.Type}");
            }
        }
        sb.AppendLine();
        
        // Forces on planet
        if (currentPlayer.ForcesOnPlanet.Any())
        {
            sb.AppendLine("## Your Forces on Planet");
            foreach (var kvp in currentPlayer.ForcesOnPlanet)
            {
                var location = kvp.Key;
                var battalion = kvp.Value;
                sb.AppendLine($"{location}: {battalion.TotalAmountOfForces} forces ({battalion.AmountOfForces} regular, {battalion.AmountOfSpecialForces} special)");
            }
            sb.AppendLine();
        }
        
        // Other players status
        sb.AppendLine("## Other Players");
        foreach (var player in game.Players.Where(p => p != currentPlayer))
        {
            sb.AppendLine($"### {player.Faction}");
            sb.AppendLine($"Resources: {player.Resources}");
            sb.AppendLine($"Cards: {player.TreacheryCards.Count()}");
            sb.AppendLine($"Total Forces: {player.ForcesOnPlanet.Sum(f => f.Value.TotalAmountOfForces)}");
            
            if (player.ForcesOnPlanet.Any())
            {
                sb.AppendLine("Locations:");
                foreach (var kvp in player.ForcesOnPlanet.Take(5)) // Limit to avoid too much text
                {
                    sb.AppendLine($"  {kvp.Key}: {kvp.Value.TotalAmountOfForces}");
                }
            }
            sb.AppendLine();
        }
        
        // Storm information
        sb.AppendLine($"## Storm");
        sb.AppendLine($"Current Sector: {game.SectorInStorm}");
        if (game.HasStormPrescience(currentPlayer))
        {
            sb.AppendLine($"Next Storm Moves: {game.NextStormMoves}");
        }
        sb.AppendLine();
        
        // Current battle info
        if (game.CurrentBattle != null)
        {
            sb.AppendLine("## Current Battle");
            sb.AppendLine($"Location: {game.CurrentBattle.Territory}");
            sb.AppendLine($"Aggressor: {game.CurrentBattle.Aggressor}");
            sb.AppendLine($"Defender: {game.CurrentBattle.Defender}");
            sb.AppendLine();
        }
        
        // Available actions
        sb.AppendLine("## Available Actions");
        sb.AppendLine("You must choose ONE of these actions (these are the ONLY legal options):");
        for (int i = 0; i < availableActions.Count; i++)
        {
            sb.AppendLine($"{i + 1}. {availableActions[i].Name}");
        }
        sb.AppendLine();
        
        return sb.ToString();
    }
    
    public static string CreateActionPrompt(string gameState, List<Type> availableActions)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine("You are an AI playing the Dune board game. Based on the current game state, you need to decide which action to take.");
        sb.AppendLine();
        sb.AppendLine("RULES:");
        sb.AppendLine("1. You must respond with ONLY the exact class name of the action you want to take");
        sb.AppendLine("2. Choose ONLY from the available actions listed below - other actions are illegal");
        sb.AppendLine("3. Consider winning conditions: control 3+ strongholds or achieve faction-specific victory");
        sb.AppendLine("4. Think strategically about resource management, positioning, and timing");
        sb.AppendLine("5. Be aware of other players' winning positions and try to prevent them from winning");
        sb.AppendLine("6. Cards can only be used for their intended purpose (weapons in battle, defenses against attacks, etc.)");
        sb.AppendLine("7. You cannot play cards you don't have or use cards inappropriately");
        sb.AppendLine();
        
        sb.AppendLine(gameState);
        
        sb.AppendLine("RESPONSE FORMAT:");
        sb.AppendLine("Respond with only the action class name, for example:");
        foreach (var actionType in availableActions.Take(3))
        {
            sb.AppendLine($"- {actionType.Name}");
        }
        sb.AppendLine();
        sb.AppendLine("Your choice:");
        
        return sb.ToString();
    }
}