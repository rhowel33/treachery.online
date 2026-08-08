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
/// Currently LLM-powered: bidding, battle plans and shipment.
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
        GameEvent? llmDecision = null;

        if (events.Contains(typeof(Bid)))
            llmDecision = await TryDetermineBidAsync();
        else if (events.Contains(typeof(Battle)))
            llmDecision = await TryDetermineBattleAsync();
        else if (events.Contains(typeof(Shipment)))
            llmDecision = await TryDetermineShipmentAsync();

        return llmDecision ?? await _fallback.DetermineLowPriorityInPhaseAction(events);
    }

    public Task<GameEvent?> DetermineEndPhaseAction(List<Type> events)
        => _fallback.DetermineEndPhaseAction(events);

    #region DecisionLoop

    /// <summary>
    /// Asks the LLM for a decision, builds the corresponding event and validates it against the
    /// game engine. Validation errors are fed back to the LLM for another attempt; returns null
    /// (= use classic fallback) when no valid event was produced.
    /// </summary>
    private async Task<T?> TryDecideAsync<T>(string decision, string situation, JsonNode responseSchema, Func<JsonNode, T?> buildEvent, Func<T, string?>? errorHint = null) where T : GameEvent
    {
        try
        {
            var messages = new List<(string Role, string Content)> { ("user", situation) };

            for (var attempt = 0; attempt < MaxLlmAttempts; attempt++)
            {
                var reply = await _llm.ChatAsync(SystemPrompt, messages, responseSchema);
                if (reply == null) return null;

                messages.Add(("assistant", reply));

                var parsed = JsonNode.Parse(reply);
                if (parsed == null) return null;

                var reasoning = parsed["reasoning"]?.GetValue<string>() ?? string.Empty;
                var evt = buildEvent(parsed);

                if (evt == null)
                {
                    messages.Add(("user", "That is not one of the offered choices. Choose again."));
                    continue;
                }

                var error = evt.Validate();
                if (error == null)
                {
                    Log($"{Skin.Describe(Faction)} {decision}: {evt.GetMessage().ToString(Skin)} ({reasoning})");
                    return evt;
                }

                Log($"{Skin.Describe(Faction)} attempted invalid {decision} ({evt.GetMessage().ToString(Skin)}): {error.ToString(Skin)}");
                var hint = errorHint?.Invoke(evt);
                messages.Add(("user", $"That decision is not allowed: {error.ToString(Skin)}. {(hint == null ? "" : hint + " ")}Choose again."));
            }

            Log($"{Skin.Describe(Faction)} produced no valid {decision} in {MaxLlmAttempts} attempts, falling back to classic bot");
        }
        catch (Exception e)
        {
            Log($"LLM {decision} failed, falling back to classic bot: {e.Message}");
        }

        return null;
    }

    private const string SystemPrompt =
        "You are an expert player of the board game Dune (played on treachery.online). " +
        "You make sharp, competitive decisions for your faction. " +
        "Always answer with a single JSON object matching the requested schema, with a short reasoning.";

    private static JsonObject Schema(params (string Name, JsonNode Definition)[] properties)
    {
        var props = new JsonObject { ["reasoning"] = new JsonObject { ["type"] = "string" } };
        var required = new JsonArray { "reasoning" };

        foreach (var (name, definition) in properties)
        {
            props[name] = definition;
            required.Add(name);
        }

        return new JsonObject { ["type"] = "object", ["properties"] = props, ["required"] = required };
    }

    private static JsonObject IntRange(int min, int max) =>
        new() { ["type"] = "integer", ["minimum"] = min, ["maximum"] = max };

    private static JsonObject Enum(IEnumerable<string> values) =>
        new() { ["type"] = "string", ["enum"] = new JsonArray(values.Select(v => (JsonNode)v).ToArray()) };

    private static JsonObject Bool() => new() { ["type"] = "boolean" };

    /// <summary>Maps display names back to objects; on duplicate names the first occurrence wins.</summary>
    private Dictionary<string, T> ByName<T>(IEnumerable<T> items) where T : notnull
    {
        var result = new Dictionary<string, T>();
        foreach (var item in items) result.TryAdd(Skin.Describe(item), item);
        return result;
    }

    #endregion

    #region Bidding

    private async Task<Bid?> TryDetermineBidAsync()
    {
        var schema = Schema(
            ("pass", Bool()),
            ("bidAmount", IntRange(0, 100)));

        return await TryDecideAsync("bid", DescribeBiddingSituation(), schema, parsed =>
        {
            var pass = parsed["pass"]?.GetValue<bool>() ?? true;
            var amount = parsed["bidAmount"]?.GetValue<int>() ?? 0;
            return BuildBid(pass, amount);
        });
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
            ? string.Join(", ", Player.TreacheryCards.Select(DescribeCard))
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
             The card being auctioned is {(cardIsKnown ? DescribeCard(Game.CardsOnAuction.Top) : "unknown to you")}.
             You have {Player.Resources} spice. {(Player.HasAlly ? $"Your ally is {Skin.Describe(Player.Ally)}." : "You have no ally.")}
             Your ally can contribute up to {allyContribution} spice and {Skin.Describe(Faction.Red)} can contribute up to {redContribution} spice, so you can pay {maxPayable} in total.
             Your hand holds {Player.TreacheryCards.Count()} of a maximum {Player.MaximumNumberOfCards} treachery cards: {hand}.
             Decide whether to pass or how much to bid. Treachery cards (weapons, defenses, special powers) are how battles are won, and an empty or weak hand is dangerous, so cheap cards are usually worth buying even unseen. Pass when the price is getting high, your hand is already strong, or you must save spice for shipping and revival.
             """;
    }

    #endregion

    #region Battle

    private async Task<Battle?> TryDetermineBattleAsync()
    {
        var currentBattle = Game.CurrentBattle;
        var territory = currentBattle?.Territory;
        var opponent = currentBattle?.OpponentOf(Player);
        if (currentBattle == null || territory == null || opponent == null) return null;

        var heroesByName = ByName<object>(Battle.ValidBattleHeroes(Game, Player));
        if (heroesByName.Count == 0) return null;

        var weaponsByName = ByName<object>(Battle.ValidWeapons(Game, Player, null, null, territory).OfType<TreacheryCard>());
        var defensesByName = ByName<object>(Battle.ValidDefenses(Game, Player, null, territory).OfType<TreacheryCard>());

        var maxNormalForces = Battle.MaxForces(Game, Player, false);
        var maxSpecialForces = Battle.MaxForces(Game, Player, true);
        var mustPayForForces = Battle.MustPayForAnyForcesInBattle(Game, Player);
        var maxSpice = mustPayForForces ? Battle.MaxResources(Game, Player, maxNormalForces, maxSpecialForces) : 0;

        var schema = Schema(
            ("leaderName", Enum(heroesByName.Keys)),
            ("weaponName", Enum(weaponsByName.Keys.Prepend("none"))),
            ("defenseName", Enum(defensesByName.Keys.Prepend("none"))),
            ("normalForcesDialed", IntRange(0, maxNormalForces)),
            ("specialForcesDialed", IntRange(0, maxSpecialForces)),
            ("spicePaidForForces", IntRange(0, maxSpice)));

        return await TryDecideAsync("battle plan",
            DescribeBattleSituation(opponent, territory, maxNormalForces, maxSpecialForces, mustPayForForces, heroesByName, weaponsByName, defensesByName),
            schema,
            parsed => BuildBattle(parsed, heroesByName, weaponsByName, defensesByName, mustPayForForces));
    }

    private Battle? BuildBattle(JsonNode parsed, Dictionary<string, object> heroes, Dictionary<string, object> weapons, Dictionary<string, object> defenses, bool mustPayForForces)
    {
        if (!heroes.TryGetValue(parsed["leaderName"]?.GetValue<string>() ?? "", out var hero)) return null;

        var weaponName = parsed["weaponName"]?.GetValue<string>() ?? "none";
        var defenseName = parsed["defenseName"]?.GetValue<string>() ?? "none";
        var weapon = weaponName == "none" ? null : weapons.GetValueOrDefault(weaponName) as TreacheryCard;
        var defense = defenseName == "none" ? null : defenses.GetValueOrDefault(defenseName) as TreacheryCard;

        var forces = Math.Max(0, parsed["normalForcesDialed"]?.GetValue<int>() ?? 0);
        var specialForces = Math.Max(0, parsed["specialForcesDialed"]?.GetValue<int>() ?? 0);
        var spice = Math.Max(0, parsed["spicePaidForForces"]?.GetValue<int>() ?? 0);

        int forcesFull, forcesHalf, specialFull, specialHalf;
        if (mustPayForForces)
            Battle.DetermineForces(Game, Player, forces, specialForces, spice, out forcesFull, out forcesHalf, out specialFull, out specialHalf);
        else
        {
            forcesFull = forces;
            forcesHalf = 0;
            specialFull = specialForces;
            specialHalf = 0;
        }

        return new Battle(Game, Faction)
        {
            Hero = (IHero)hero,
            Forces = forcesFull,
            ForcesAtHalfStrength = forcesHalf,
            SpecialForces = specialFull,
            SpecialForcesAtHalfStrength = specialHalf,
            Weapon = weapon,
            Defense = defense,
            BankerBonus = 0,
            Messiah = false
        };
    }

    private string DescribeBattleSituation(Player opponent, Territory territory, int maxNormalForces, int maxSpecialForces, bool mustPayForForces,
        Dictionary<string, object> heroes, Dictionary<string, object> weapons, Dictionary<string, object> defenses)
    {
        var isAggressor = Game.CurrentBattle!.Aggressor == Faction;
        var normalStrength = Battle.DetermineNormalForceStrength(Game, Faction);
        var specialStrength = Battle.DetermineSpecialForceStrength(Game, Faction, opponent.Faction);

        var leaderList = string.Join(", ", heroes.Select(kvp => $"{kvp.Key} (strength {(kvp.Value as IHero)?.Value ?? 0})"));
        var weaponList = weapons.Count == 0 ? "none" : string.Join(", ", weapons.Keys.Select(n => DescribeCard(weapons[n] as TreacheryCard)));
        var defenseList = defenses.Count == 0 ? "none" : string.Join(", ", defenses.Keys.Select(n => DescribeCard(defenses[n] as TreacheryCard)));

        var traitors = Player.Traitors.Count == 0
            ? "You hold no traitor cards."
            : $"You secretly hold these leaders as traitors: {string.Join(", ", Player.Traitors.Select(t => $"{Skin.Describe(t)} ({Skin.Describe(t.Faction)})"))}. If the opponent fights with a leader that is your traitor, you can reveal it and win the battle outright — dial low against such leaders.";

        var opponentLeaders = string.Join(", ", opponent.Leaders.Where(l => Game.IsAlive(l)).Select(l => $"{Skin.Describe(l)} (strength {l.Value})"));

        var paymentRules = mustPayForForces
            ? $"IMPORTANT: forces only fight at FULL strength if you pay 1 spice each; unpaid forces fight at HALF strength. You have {Player.Resources} spice to spend on this."
            : "Your forces fight at full strength for free.";

        return
            $"""
             It is turn {Game.CurrentTurn} of {Game.MaximumTurns}. You are {Skin.Describe(Faction)}.
             You must now secretly submit a battle plan for the battle in {Skin.Describe(territory)} against {Skin.Describe(opponent.Faction)}. You are the {(isAggressor ? "aggressor (you win ties)" : "defender (you lose ties)")}.

             How battles work: both sides secretly choose a leader, dial a number of forces, and may play one weapon and one defense card. Your total = dialed force strength + leader strength. Higher total wins. The LOSER loses ALL forces in the territory; the WINNER loses only the forces they dialed. A weapon kills the enemy leader unless they play the matching defense (projectile weapons are stopped by shields, poison weapons by snoopers); a killed leader adds no strength. A lasgun together with any shield in the battle destroys everything on both sides.

             Your forces in {Skin.Describe(territory)}: {maxNormalForces} normal forces (strength {normalStrength} each) and {maxSpecialForces} special forces (strength {specialStrength} each).
             {paymentRules}
             The opponent has {opponent.AnyForcesIn(territory)} forces in the territory and these leaders alive: {opponentLeaders}.
             Your available leaders: {leaderList}.
             Weapons you can play: {weaponList}.
             Defenses you can play: {defenseList}.
             {traitors}
             Dial high enough to win if the battle matters, but never waste more forces than needed, and consider what the opponent is likely to dial and play.
             """;
    }

    #endregion

    #region Shipment

    private async Task<Shipment?> TryDetermineShipmentAsync()
    {
        // White's No-Field shipment mechanics are not LLM-suitable yet; let the classic bot handle them
        if (Faction == Faction.White) return null;

        var candidates = DetermineShipmentCandidates();
        if (candidates.Count == 0) return null;

        var maxNormal = Player.ForcesInReserve;
        var maxSpecial = Player.SpecialForcesInReserve;
        if (maxNormal + maxSpecial == 0) return null;

        var schema = Schema(
            ("pass", Bool()),
            ("destination", Enum(candidates.Keys)),
            ("normalForces", IntRange(0, maxNormal)),
            ("specialForces", IntRange(0, maxSpecial)));

        return await TryDecideAsync("shipment", DescribeShipmentSituation(candidates, maxNormal, maxSpecial), schema, parsed =>
            BuildShipment(parsed, candidates),
            evt => evt.Passed || evt.To == null
                ? null
                : $"Shipping {evt.ForceAmount + evt.SpecialForceAmount} forces to {Skin.Describe(evt.To)} costs {Shipment.DetermineCost(Game, Player, evt)} spice and you only have {Player.Resources}. Ship fewer forces, choose a cheaper destination, or set pass to true if you can't afford a useful shipment.");
    }

    private Shipment? BuildShipment(JsonNode parsed, Dictionary<string, Location> candidates)
    {
        if (parsed["pass"]?.GetValue<bool>() ?? true) return new Shipment(Game, Faction) { Passed = true };

        if (!candidates.TryGetValue(parsed["destination"]?.GetValue<string>() ?? "", out var location)) return null;
        var forces = Math.Max(0, parsed["normalForces"]?.GetValue<int>() ?? 0);
        var specialForces = Math.Max(0, parsed["specialForces"]?.GetValue<int>() ?? 0);

        return new Shipment(Game, Faction)
        {
            ForceAmount = forces,
            SpecialForceAmount = specialForces,
            Passed = false,
            ShipmentType = ShipmentType.ShipmentNormal,
            From = null,
            KarmaCard = null,
            To = location,
            ForceLocations = DetermineShipmentSources(forces, specialForces) ?? []
        };
    }

    /// <summary>
    /// When a faction can ship from multiple homeworlds, the engine requires the shipment to specify
    /// per-homeworld sources; assign them greedily since the distinction is rarely strategic.
    /// </summary>
    private Dictionary<Location, Battalion>? DetermineShipmentSources(int forces, int specialForces)
    {
        if (Game.Version < 164) return null;

        var normalSources = Shipment.HomeworldsToShipFrom(Player, false).ToList();
        var specialSources = Shipment.HomeworldsToShipFrom(Player, true).ToList();
        if ((forces == 0 || normalSources.Count <= 1) && (specialForces == 0 || specialSources.Count <= 1)) return null;

        var result = new Dictionary<Location, Battalion>();

        var remaining = forces;
        foreach (var world in normalSources)
        {
            var take = Math.Min(remaining, Player.ForcesIn(world));
            if (take > 0) result[world] = new Battalion(Faction, take, 0, world);
            remaining -= take;
            if (remaining <= 0) break;
        }

        var remainingSpecial = specialForces;
        foreach (var world in specialSources)
        {
            var take = Math.Min(remainingSpecial, Player.SpecialForcesIn(world));
            if (take > 0)
            {
                if (result.TryGetValue(world, out var battalion)) battalion.AmountOfSpecialForces = take;
                else result[world] = new Battalion(Faction, 0, take, world);
            }

            remainingSpecial -= take;
            if (remainingSpecial <= 0) break;
        }

        return result;
    }

    private Dictionary<string, Location> DetermineShipmentCandidates()
    {
        var valid = Shipment.ValidShipmentLocations(Game, Player, false, false)
            .Where(l => !Game.IsInStorm(l))
            .OrderByDescending(l => l.IsStronghold)
            .ThenByDescending(l => Game.ResourcesOnPlanet.GetValueOrDefault(l))
            .Take(40);

        return ByName(valid);
    }

    private string DescribeShipmentSituation(Dictionary<string, Location> candidates, int maxNormal, int maxSpecial)
    {
        var candidateLines = candidates.Select(kvp =>
        {
            var location = kvp.Value;
            var costPerForce = Shipment.DetermineCost(Game, Player, 1, 0, location, false, false, false, false, false);
            var affordable = costPerForce == 0 ? maxNormal + maxSpecial : Math.Min(maxNormal + maxSpecial, Player.Resources / costPerForce);
            var spice = Game.ResourcesOnPlanet.GetValueOrDefault(location);
            var occupants = Game.Players
                .Where(p => p != Player && p.AnyForcesIn(location.Territory) > 0)
                .Select(p => $"{Skin.Describe(p.Faction)}: {p.AnyForcesIn(location.Territory)}")
                .ToList();

            return $"- {kvp.Key}{(location.IsStronghold ? " [STRONGHOLD]" : "")}: {costPerForce} spice per force, you can afford at most {affordable} forces here" +
                   (spice > 0 ? $", {spice} spice lying here" : "") +
                   (occupants.Count > 0 ? $", occupied by {string.Join(" and ", occupants)}" : ", unoccupied");
        });

        var ownPositions = Game.Players.Any(p => p == Player)
            ? string.Join("; ", Game.Map.Territories(false).Where(t => Player.AnyForcesIn(t) > 0).Select(t => $"{Skin.Describe(t)}: {Player.AnyForcesIn(t)}"))
            : "";

        return
            $"""
             It is turn {Game.CurrentTurn} of {Game.MaximumTurns}. You are {Skin.Describe(Faction)}.
             It is your turn to ship forces from your off-planet reserves onto the planet (or pass).
             You have {maxNormal} normal forces and {maxSpecial} special forces in reserve, and {Player.Resources} spice.
             Your forces already on the planet: {(ownPositions.Length > 0 ? ownPositions : "none")}.

             To win the game you must control strongholds (usually 3 at the end of a turn wins). Spice on the planet can be collected by forces in that location. After shipping you will also get one move with forces already on the planet.

             Possible destinations:
             {string.Join(Environment.NewLine, candidateLines)}

             Ship where it helps you take or defend strongholds or collect spice, in numbers that can win the ensuing battle; do not overspend or strand small vulnerable groups, and pass if saving spice is better this turn.
             """;
    }

    #endregion

    private string DescribeCard(TreacheryCard? card) => card == null ? "none" : $"{Skin.Describe(card)} ({Skin.Describe(card.Type)})";

    private void Log(string message) => Console.WriteLine($"[OllamaBot:{_llm.Model}] {message}");
}
