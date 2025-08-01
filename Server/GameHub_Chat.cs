using System.Threading.Tasks;
using Treachery.Shared;
using Treachery.Bots;

namespace Treachery.Server;

public partial class GameHub
{
    public async Task<VoidResult> SendChatMessage(string userToken, string gameId, GameChatMessage e)
    {
        if (!AreValid(userToken, gameId, out _, out var managedGame, out var error))
            return error;
        
        // Process potential bot instructions before sending the message
        try
        {
            BotInstructionSystem.ProcessChatMessage(gameId, e, managedGame.Game);
        }
        catch (Exception ex)
        {
            // Log error but don't fail the chat message
            Console.WriteLine($"Error processing bot instruction: {ex.Message}");
        }
        
        await Clients.Group(gameId).HandleChatMessage(e);
        
        // Send bot instruction confirmation if applicable
        await SendBotInstructionConfirmation(gameId, e, managedGame.Game);
        
        return Success();
    }
    
    private async Task SendBotInstructionConfirmation(string gameId, GameChatMessage originalMessage, Game game)
    {
        var sourcePlayer = game.GetPlayerByUserId(originalMessage.SourceUserId);
        if (sourcePlayer == null || sourcePlayer.IsBot) return;
        
        // Check if this was a message to their bot ally
        var targetPlayer = originalMessage.TargetUserId > 0 ? game.GetPlayerByUserId(originalMessage.TargetUserId) : null;
        var isToAlly = targetPlayer != null && targetPlayer.IsBot && sourcePlayer.HasAlly && sourcePlayer.AlliedPlayer == targetPlayer;
        
        if (!isToAlly) return;
        
        var instruction = BotInstructionSystem.GetBotInstruction(gameId, targetPlayer.Faction);
        if (instruction != null && instruction.CreatedAt > DateTime.UtcNow.AddSeconds(-2)) // Recent instruction
        {
            var confirmationMessage = new GameChatMessage
            {
                SourceUserId = -1, // System message
                TargetUserId = originalMessage.SourceUserId,
                Body = $"🤖 {targetPlayer.Faction} Bot received instruction: {instruction.Summary}"
            };
            
            await Clients.Group(gameId).HandleChatMessage(confirmationMessage);
        }
    }

    public async Task<VoidResult> SendGlobalChatMessage(string userToken, GlobalChatMessage message)
    {
        if (!UsersByUserToken.ContainsKey(userToken))
            return Error(ErrorType.UserNotFound);
        
        await Clients.All.HandleGlobalChatMessage(message);
        return Success();
    }
}

