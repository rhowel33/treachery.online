/*
 * Copyright (C) 2020-2025 Ronald Ossendrijver (admin@treachery.online)
 * This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version. This
 * program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License for more details. You should have
 * received a copy of the GNU General Public License along with this program. If not, see <http://www.gnu.org/licenses/>.
 */

using System.Text.Json.Nodes;

namespace Treachery.Bots;

/// <summary>
/// A bot that delegates decisions to a local LLM served by Ollama and falls back to the
/// ClassicBot heuristics for decisions it does not (yet) handle or when the LLM fails.
/// Currently LLM-powered: bidding on treachery cards.
/// </summary>
public class OllamaBot : IBot
{
    private const int MaxLlmAttempts = 3;

    private readonly ClassicBot _fallback;
    private readonly OllamaClient _llm;

    private Game Game { get; set; }
    private Player Player { get; set; }
    private Faction Faction => Player.Faction;
    private Skin Skin => DefaultSkin.Default;

    public OllamaBot(Game game, Player player, BotParameters parameters, OllamaClient llm)
    {
        Game = game;
        Player = player;
        _llm = llm;
        _fallback = new ClassicBot(game, player, parameters);
    }

    public void SetGameAndPlayer(Game game, Player player)
    {
        Game = game;
        Player = player;
        _fallback.SetGameAndPlayer(game, player);
    }

    public Task<GameEvent?> DetermineHighestPriorityInPhaseAction(List<Type> events)
        => _fallback.DetermineHighestPriorityInPhaseAction(events);

    public Task<GameEvent?> DetermineHighPriorityInPhaseAction(List<Type> events)
        => _fallback.DetermineHighPriorityInPhaseAction(events);

    public Task<GameEvent?> DetermineMiddlePriorityInPhaseAction(List<Type> events)
        => _fallback.DetermineMiddlePriorityInPhaseAction(events);

    public async Task<GameEvent?> DetermineLowPriorityInPhaseAction(List<Type> events)
    {
        if (events.Contains(typeof(Bid)))
        {
            var bid = await TryDetermineBidAsync();
            if (bid != null) return bid;
        }

        return await _fallback.DetermineLowPriorityInPhaseAction(events);
    }

    public Task<GameEvent?> DetermineEndPhaseAction(List<Type> events)
        => _fallback.DetermineEndPhaseAction(events);

    #region Bidding

    private async Task<Bid?> TryDetermineBidAsync()
    {
        try
        {
            var messages = new List<(string Role, string Content)> { ("user", DescribeBiddingSituation()) };

            for (var attempt = 0; attempt < MaxLlmAttempts; attempt++)
            {
                var reply = await _llm.ChatAsync(SystemPrompt, messages, BidResponseSchema());
                if (reply == null) return null;

                messages.Add(("assistant", reply));

                var parsed = JsonNode.Parse(reply);
                var pass = parsed?["pass"]?.GetValue<bool>() ?? true;
                var amount = parsed?["bidAmount"]?.GetValue<int>() ?? 0;
                var reasoning = parsed?["reasoning"]?.GetValue<string>() ?? string.Empty;

                var bid = BuildBid(pass, amount);
                var error = bid.Validate();

                if (error == null)
                {
                    Log($"{Skin.Describe(Faction)} bids: {(pass ? "pass" : amount.ToString())} ({reasoning})");
                    return bid;
                }

                Log($"{Skin.Describe(Faction)} attempted invalid bid ({(pass ? "pass" : amount.ToString())}): {error.ToString(Skin)}");
                messages.Add(("user", $"That decision is not allowed: {error.ToString(Skin)}. Choose again."));
            }
        }
        catch (Exception e)
        {
            Log($"LLM bid failed, falling back to classic bot: {e.Message}");
        }

        return null;
    }

    private Bid BuildBid(bool pass, int amount)
    {
        if (pass) return new Bid(Game, Faction) { Passed = true };

        var spiceLeftToPay = amount;
        var redContribution = Math.Min(spiceLeftToPay, Game.SpiceForBidsRedCanPay(Faction));
        spiceLeftToPay -= redContribution;

        var allyContribution = Math.Min(spiceLeftToPay, Game.ResourcesYourAllyCanPay(Player));
        spiceLeftToPay -= allyContribution;

        return new Bid(Game, Faction)
        {
            Amount = spiceLeftToPay,
            AllyContributionAmount = allyContribution,
            RedContributionAmount = redContribution
        };
    }

    private string DescribeBiddingSituation()
    {
        var allyContribution = Game.ResourcesYourAllyCanPay(Player);
        var redContribution = Game.SpiceForBidsRedCanPay(Faction);
        var maxPayable = Player.Resources + allyContribution + redContribution;

        var cardIsKnown = Game.HasBiddingPrescience(Player) ||
                          (Player.HasAlly && Game.HasBiddingPrescience(Player.AlliedPlayer)) ||
                          Game.KnownCards(Player).Contains(Game.CardsOnAuction.Top);

        var hand = Player.TreacheryCards.Any()
            ? string.Join(", ", Player.TreacheryCards.Select(c => Skin.Describe(c)))
            : "empty";

        var auctionRules = Game.CurrentAuctionType switch
        {
            AuctionType.BlackMarketSilent or AuctionType.WhiteSilent =>
                "This is a SILENT auction: every player secretly writes down one bid, the highest wins. You cannot pass; bid 0 if you don't want the card. You cannot bid more spice than you can pay.",
            AuctionType.BlackMarketOnceAround or AuctionType.WhiteOnceAround =>
                "This is a ONCE AROUND auction: each player gets exactly one chance to bid. To bid, you must exceed the current bid; otherwise pass.",
            _ =>
                "This is a normal auction: bidding continues around the table until all but one player passes. To bid, you must exceed the current bid; otherwise pass."
        };

        var currentBid = Game.CurrentBid == null
            ? "There is no bid yet."
            : $"The current bid is {Game.CurrentBid.TotalAmount} spice, made by {Skin.Describe(Game.CurrentBid.Initiator)}.";

        return
            $"""
             It is turn {Game.CurrentTurn} of {Game.MaximumTurns}. You are {Skin.Describe(Faction)}.
             A treachery card is being auctioned. {auctionRules}
             {currentBid}
             The card being auctioned is {(cardIsKnown ? Skin.Describe(Game.CardsOnAuction.Top) : "unknown to you")}.
             You have {Player.Resources} spice. {(Player.HasAlly ? $"Your ally is {Skin.Describe(Player.Ally)}." : "You have no ally.")}
             Your ally can contribute up to {allyContribution} spice and {Skin.Describe(Faction.Red)} can contribute up to {redContribution} spice, so you can pay {maxPayable} in total.
             Your hand holds {Player.TreacheryCards.Count()} of a maximum {Player.MaximumNumberOfCards} treachery cards: {hand}.
             Decide whether to pass or how much to bid. Treachery cards (weapons, defenses, special powers) are how battles are won, and an empty or weak hand is dangerous, so cheap cards are usually worth buying even unseen. Pass when the price is getting high, your hand is already strong, or you must save spice for shipping and revival.
             """;
    }

    private const string SystemPrompt =
        "You are an expert player of the board game Dune (played on treachery.online). " +
        "You make sharp, competitive decisions for your faction. " +
        "Always answer with a single JSON object matching the requested schema, with a short reasoning.";

    private static JsonNode BidResponseSchema() => JsonNode.Parse(
        """
        {
          "type": "object",
          "properties": {
            "reasoning": { "type": "string" },
            "pass": { "type": "boolean" },
            "bidAmount": { "type": "integer", "minimum": 0 }
          },
          "required": ["reasoning", "pass", "bidAmount"]
        }
        """)!;

    #endregion

    private void Log(string message) => Console.WriteLine($"[OllamaBot:{_llm.Model}] {message}");
}
