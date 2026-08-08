using Treachery.Shared.Model;

namespace Treachery.Server;

public partial class GameHub
{
    public async Task<VoidResult> SendChatMessage(string userToken, string gameId, GameChatMessage e)
    {
        if (!AreValid(userToken, gameId, out var user, out var game, out var error))
            return error!;

        await Clients.Group(gameId).HandleChatMessage(e);

        if (e.TargetUserId < 0 && !Game.IsBotUserId(e.SourceUserId))
            _ = LetBotsRespondToChatMessage(game!, e, user!.Username);

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
        var senderFaction = game.Game.GetPlayerByUserId(message.SourceUserId)?.Faction ?? Faction.None;
        var sender = senderFaction != Faction.None ? DefaultSkin.Default.Describe(senderFaction) : senderUsername;

        foreach (var botPlayer in game.Game.Players.Where(p => p.IsBot))
        {
            if (GetOrInitializeBot(game, botPlayer) is not IChatBot chatBot) continue;

            var reply = await chatBot.HandleChatMessage(sender, message.Body);
            if (reply == null) continue;

            await Clients.Group(game.GameId).HandleChatMessage(new GameChatMessage
            {
                SourceUserId = Game.BotUserId(botPlayer),
                TargetUserId = -1,
                Body = reply
            });
        }
    }
}

