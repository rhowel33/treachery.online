# GptMcpBot Usage Examples

This document provides code examples for using the GptMcpBot in various scenarios.

## Basic Usage

### 1. Simple Bot Instantiation

```csharp
using Treachery.Bots;
using Treachery.Shared;

// Create a game
var game = new Game(/* game settings */);
var player = game.Players.First(p => p.Faction == Faction.Yellow);

// Get bot parameters for the faction
var parameters = BotParameters.GetDefaultParameters(Faction.Yellow);

// Create GptMcpBot
var bot = new GptMcpBot(
    game,
    player,
    parameters,
    mcpServerPath: "/path/to/MCP/Treachery.MCP.Server.dll",
    ollamaBaseUrl: "http://localhost:11434"
);

// Get available actions
var availableActions = game.GetApplicableEvents(player, false);
var actionTypes = availableActions.Select(a => a.GetType()).Distinct().ToList();

// Let bot decide on low priority action
var action = bot.DetermineLowPriorityInPhaseAction(actionTypes);

if (action != null)
{
    // Execute the action
    game.Execute(action);
}

// Clean up when done
bot.Dispose();
```

## Advanced Usage

### 2. Using Environment Variables for Configuration

```csharp
// Set environment variables before starting server
Environment.SetEnvironmentVariable("USE_GPT_MCP_BOT", "true");
Environment.SetEnvironmentVariable("OLLAMA_URL", "http://localhost:11434");
Environment.SetEnvironmentVariable("MCP_SERVER_PATH", "/path/to/server.dll");

// In GameHub_GameEvents.cs, GetOrInitializeBot will automatically use these
var bot = GetOrInitializeBot(managedGame, player, gameId);
```

### 3. Custom Bot Parameters

```csharp
// Create custom parameters for more aggressive play
var customParams = new BotParameters
{
    Bidding_ResourcesToKeep = 2,  // Keep less for bidding (more aggressive)
    Battle_MaximumUnsupportedForces = 10,  // Allow more risky moves
    Battle_DialShortageThresholdForThrowing = 5,  // Fight harder
    Shipment_MaxEnemyForceStrengthFightingForSpice = 15  // Take more risks for spice
};

var bot = new GptMcpBot(game, player, customParams, mcpServerPath, ollamaUrl);
```

### 4. Handling All Priority Levels

```csharp
public GameEvent? DetermineNextAction(Game game, Player player, IBot bot)
{
    var availableActions = game.GetApplicableEvents(player, false);
    var actionTypes = availableActions.Select(a => a.GetType()).Distinct().ToList();

    // Try each priority level in order
    var action = bot.DetermineHighestPriorityInPhaseAction(actionTypes);
    if (action != null) return action;

    action = bot.DetermineHighPriorityInPhaseAction(actionTypes);
    if (action != null) return action;

    action = bot.DetermineMiddlePriorityInPhaseAction(actionTypes);
    if (action != null) return action;

    action = bot.DetermineLowPriorityInPhaseAction(actionTypes);
    if (action != null) return action;

    action = bot.DetermineEndPhaseAction(actionTypes);
    return action;
}
```

### 5. Fallback Pattern

```csharp
public IBot CreateBotWithFallback(Game game, Player player, BotParameters parameters)
{
    // Try to create GptMcpBot, fall back to ClassicBot on failure
    try
    {
        var mcpServerPath = "/path/to/MCP/Treachery.MCP.Server.dll";
        var ollamaUrl = "http://localhost:11434";

        var bot = new GptMcpBot(game, player, parameters, mcpServerPath, ollamaUrl);
        Console.WriteLine($"Created GptMcpBot for {player.Faction}");
        return bot;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"GptMcpBot failed: {ex.Message}. Using ClassicBot.");
        return new ClassicBot(game, player, parameters);
    }
}
```

### 6. Testing Bot Decisions

```csharp
[Test]
public void TestBotBattleDecision()
{
    // Setup
    var game = CreateTestGameInBattlePhase();
    var player = game.Players.First(p => p.Faction == Faction.Yellow);
    var parameters = BotParameters.GetDefaultParameters(Faction.Yellow);

    var bot = new GptMcpBot(game, player, parameters, mcpServerPath, ollamaUrl);

    // Get battle actions
    var availableActions = game.GetApplicableEvents(player, false);
    var battleActions = availableActions
        .Select(a => a.GetType())
        .Where(t => t.Name.Contains("Battle"))
        .ToList();

    // Bot should make a decision
    var action = bot.DetermineLowPriorityInPhaseAction(battleActions);

    // Assertions
    Assert.IsNotNull(action, "Bot should make a battle decision");
    Assert.IsTrue(battleActions.Contains(action.GetType()), "Action should be valid");

    // Verify it's a legal action
    var legalActions = game.GetApplicableEvents(player, false);
    Assert.IsTrue(legalActions.Any(a => a.GetType() == action.GetType()),
        "Bot action should be legal");

    bot.Dispose();
}
```

### 7. Monitoring Bot Performance

```csharp
public class BotPerformanceMonitor
{
    private int _totalDecisions;
    private int _invalidActions;
    private int _fallbackUsed;
    private List<long> _decisionTimes = new();

    public GameEvent? MonitoredDetermineAction(IBot bot, List<Type> availableActions)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            _totalDecisions++;

            var action = bot.DetermineLowPriorityInPhaseAction(availableActions);

            if (action == null)
            {
                _invalidActions++;
                Console.WriteLine("WARNING: Bot returned null action");
            }
            else if (!availableActions.Contains(action.GetType()))
            {
                _invalidActions++;
                Console.WriteLine($"WARNING: Bot returned invalid action: {action.GetType().Name}");
            }

            return action;
        }
        catch (Exception ex)
        {
            _fallbackUsed++;
            Console.WriteLine($"ERROR: Bot decision failed: {ex.Message}");
            return null;
        }
        finally
        {
            stopwatch.Stop();
            _decisionTimes.Add(stopwatch.ElapsedMilliseconds);
        }
    }

    public void PrintStats()
    {
        Console.WriteLine("=== Bot Performance Stats ===");
        Console.WriteLine($"Total decisions: {_totalDecisions}");
        Console.WriteLine($"Invalid actions: {_invalidActions} ({_invalidActions * 100.0 / _totalDecisions:F1}%)");
        Console.WriteLine($"Fallbacks used: {_fallbackUsed} ({_fallbackUsed * 100.0 / _totalDecisions:F1}%)");
        Console.WriteLine($"Average decision time: {_decisionTimes.Average():F0}ms");
        Console.WriteLine($"Max decision time: {_decisionTimes.Max()}ms");
    }
}
```

### 8. Custom MCP Tools Integration

If you add new MCP tools, here's how to use them:

```csharp
// In DuneGameTools.cs - Add new tool
[Description("Get player alliance information")]
public async Task<string> GetAllianceInfo(
    [Description("Game ID")] string gameId,
    [Description("Faction")] string faction)
{
    var game = _activeGames[gameId];
    var player = game.Players.First(p => p.Faction.ToString() == faction);

    var allianceInfo = new
    {
        HasAlly = player.Ally != Faction.None,
        AllyFaction = player.Ally.ToString(),
        AllianceType = player.AlliedPlayer != null ? "Active" : "None"
    };

    return JsonSerializer.Serialize(allianceInfo);
}

// In McpServerClient.cs - Add client method
public async Task<string> GetAllianceInfoAsync(string gameId, Faction faction)
{
    var result = await CallToolAsync("GetAllianceInfo",
        new { gameId, faction = faction.ToString() });
    return ExtractContent(result);
}

// In GptMcpBot.cs - Add to tool execution switch
var result = toolName switch
{
    "GetGameState" => await _mcpClient.GetGameStateAsync(_gameId, _player.Faction),
    "GetAllianceInfo" => await _mcpClient.GetAllianceInfoAsync(_gameId, _player.Faction),
    // ... other tools
    _ => $"Unknown tool: {toolName}"
};
```

## Docker Usage

### 9. Running with Docker

```dockerfile
# Dockerfile
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build

# Install Ollama
RUN curl -fsSL https://ollama.com/install.sh | sh

# Copy and build project
WORKDIR /app
COPY . .
RUN dotnet build -c Release

# Pull model
RUN ollama serve & sleep 5 && ollama pull gpt-oss:20b

# Set environment variables
ENV USE_GPT_MCP_BOT=true
ENV OLLAMA_URL=http://localhost:11434
ENV MCP_SERVER_PATH=/app/MCP/Treachery.MCP.Server/bin/Release/net9.0/Treachery.MCP.Server.dll

# Start both Ollama and game server
CMD ollama serve & dotnet run --project Server/Treachery.Server.csproj
```

```bash
# Build and run
docker build -t dune-game .
docker run -p 5000:5000 dune-game
```

## Production Considerations

### 10. Production Deployment

```csharp
public class ProductionBotFactory
{
    public static IBot CreateBot(Game game, Player player, bool allowLLM = true)
    {
        var parameters = BotParameters.GetDefaultParameters(player.Faction);

        // Only use LLM bots in production if explicitly enabled
        if (!allowLLM || !IsLLMAvailable())
        {
            return new ClassicBot(game, player, parameters);
        }

        try
        {
            // Use health check before creating bot
            if (!CheckOllamaHealth())
            {
                throw new Exception("Ollama health check failed");
            }

            return new GptMcpBot(
                game,
                player,
                parameters,
                GetMcpServerPath(),
                GetOllamaUrl()
            );
        }
        catch (Exception ex)
        {
            // Log error and fall back to ClassicBot
            LogError($"Failed to create GptMcpBot: {ex.Message}");
            return new ClassicBot(game, player, parameters);
        }
    }

    private static bool IsLLMAvailable()
    {
        var enabled = Environment.GetEnvironmentVariable("ENABLE_LLM_BOTS");
        return enabled?.ToLowerInvariant() == "true";
    }

    private static bool CheckOllamaHealth()
    {
        try
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var response = client.GetAsync($"{GetOllamaUrl()}/api/tags").Result;
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static string GetOllamaUrl()
    {
        return Environment.GetEnvironmentVariable("OLLAMA_URL")
            ?? "http://localhost:11434";
    }

    private static string GetMcpServerPath()
    {
        return Environment.GetEnvironmentVariable("MCP_SERVER_PATH")
            ?? "/app/MCP/Treachery.MCP.Server/bin/Release/net9.0/Treachery.MCP.Server.dll";
    }

    private static void LogError(string message)
    {
        Console.Error.WriteLine($"[BotFactory] {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} {message}");
    }
}
```

## Debugging

### 11. Debug Logging

```csharp
// Enable detailed logging in DEBUG builds
#if DEBUG
Console.WriteLine("[Bot Debug] Starting decision process");
Console.WriteLine($"[Bot Debug] Available actions: {string.Join(", ", availableActions.Select(a => a.Name))}");
#endif

var action = bot.DetermineLowPriorityInPhaseAction(availableActions);

#if DEBUG
Console.WriteLine($"[Bot Debug] Selected action: {action?.GetType().Name ?? "null"}");
#endif
```

### 12. Tool Call Inspection

```csharp
// In GptMcpBot.cs, you can intercept tool calls:
private async Task<string> ExecuteToolCallAsync(OllamaToolCall toolCall)
{
    var toolName = toolCall.Function.Name;
    var args = toolCall.Function.Arguments;

    // Log tool call
    Console.WriteLine($"[Tool Call] {toolName}({args})");

    var stopwatch = Stopwatch.StartNew();
    var result = await /* ... execute tool ... */;
    stopwatch.Stop();

    Console.WriteLine($"[Tool Result] {toolName} completed in {stopwatch.ElapsedMilliseconds}ms");
    Console.WriteLine($"[Tool Result] First 200 chars: {result.Substring(0, Math.Min(200, result.Length))}");

    return result;
}
```

## Best Practices

### Summary of Best Practices

1. **Always provide fallback to ClassicBot** - LLM services can fail
2. **Use environment variables for configuration** - Makes deployment flexible
3. **Monitor bot performance** - Track invalid actions and decision times
4. **Test in DEBUG mode first** - Use detailed logging to understand behavior
5. **Set appropriate timeouts** - LLM calls can be slow
6. **Handle disposal properly** - Call `bot.Dispose()` when done
7. **Use health checks** - Verify Ollama/MCP availability before creating bots
8. **Log errors comprehensively** - Helps diagnose production issues
9. **Consider costs** - LLM inference uses CPU/GPU resources
10. **Test with real game scenarios** - Don't just unit test, play actual games

## Troubleshooting

See **[BOT_SYSTEM_README.md](BOT_SYSTEM_README.md)** for detailed troubleshooting guide.

---

**Last updated**: 2025-01-13
**Author**: Claude Code
