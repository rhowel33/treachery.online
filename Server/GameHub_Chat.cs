using Treachery.Shared.Model;

namespace Treachery.Server;

public partial class GameHub
{
    public async Task<VoidResult> SendChatMessage(string userToken, string gameId, GameChatMessage e)
    {
        if (!AreValid(userToken, gameId, out var user, out var game, out var error))
            return error!;

        await Clients.Group(gameId).HandleChatMessage(e);

        if (!Game.IsBotUserId(e.SourceUserId))
        {
            if (e.TargetUserId == -1)
                _ = LetBotsRespondToChatMessage(game!, e, user!.Username);
            else if (Game.IsBotUserId(e.TargetUserId))
                _ = LetTargetBotRespondToChatMessage(game!, e, user!.Username);
        }

        return Success();
    }

    public async Task<VoidResult> SendGlobalChatMessage(string userToken, GlobalChatMessage message)
    {
        if (!UsersByUserToken.ContainsKey(userToken))
            return Error(ErrorType.UserNotFound);

        await Clients.All.HandleGlobalChatMessage(message);
        return Success();
    }

    /// <summary>
    /// Gives every chat-capable bot in the game a chance to react to a public chat message.
    /// Replies are broadcast with a synthetic bot user id so clients render the sending faction.
    /// </summary>
    private async Task LetBotsRespondToChatMessage(ManagedGame game, GameChatMessage message, string senderUsername)
    {
        foreach (var botPlayer in game.Game.Players.Where(p => p.IsBot))
        {
            if (GetOrInitializeBot(game, botPlayer) is not IChatBot chatBot) continue;

            var reply = await chatBot.HandleChatMessage(DescribeSender(game, message, senderUsername), message.Body, false);
            if (reply == null) continue;

            await Clients.Group(game.GameId).HandleChatMessage(new GameChatMessage
            {
                SourceUserId = Game.BotUserId(botPlayer),
                TargetUserId = -1,
                Body = reply
            });
        }
    }

    /// <summary>
    /// Delivers a private chat message to the addressed bot; its reply goes back privately to the
    /// sender. Clients only display private messages to their sender and target, so secrecy holds.
    /// </summary>
    private async Task LetTargetBotRespondToChatMessage(ManagedGame game, GameChatMessage message, string senderUsername)
    {
        var botPlayer = game.Game.GetPlayerByUserId(message.TargetUserId);
        if (botPlayer == null || !botPlayer.IsBot) return;
        if (GetOrInitializeBot(game, botPlayer) is not IChatBot chatBot) return;

        var reply = await chatBot.HandleChatMessage(DescribeSender(game, message, senderUsername), message.Body, true);
        if (reply == null) return;

        await Clients.Group(game.GameId).HandleChatMessage(new GameChatMessage
        {
            SourceUserId = Game.BotUserId(botPlayer),
            TargetUserId = message.SourceUserId,
            Body = reply
        });
    }

    private static string DescribeSender(ManagedGame game, GameChatMessage message, string senderUsername)
    {
        var senderFaction = game.Game.GetPlayerByUserId(message.SourceUserId)?.Faction ?? Faction.None;
        return senderFaction != Faction.None ? DefaultSkin.Default.Describe(senderFaction) : senderUsername;
    }
}

