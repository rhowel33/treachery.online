# Dune Board Game - Bot System Documentation

## Overview

The Dune board game uses an AI bot system to control computer players. This document describes the current bot implementation and how to configure it.

## Bot Types

### 1. ClassicBot (Default)
- **Description**: Sophisticated rule-based AI with 300KB+ of hand-crafted strategy
- **Strengths**: Fast, deterministic, reliable
- **Weaknesses**: Predictable patterns, no learning
- **Use case**: Default for all games, fallback when LLM bots fail

### 2. GptMcpBot (LLM-Enhanced)
- **Description**: Unified bot using gpt-oss:20b LLM via Ollama with MCP tool calling
- **Strengths**: Strategic reasoning, unpredictable play, context-aware decisions
- **Weaknesses**: Slower (LLM inference latency), requires external services
- **Use case**: Experimental, for improved strategic play

## Architecture

```
┌──────────────────┐
│   Game Server    │
│  (GameHub.cs)    │
└────────┬─────────┘
         │
         ├─→ GetOrInitializeBot() ← Environment variables
         │
         ├─→ GptMcpBot
         │   ├─→ OllamaChatClient (HTTP → Ollama)
         │   ├─→ McpServerClient (Process → MCP Server)
         │   ├─→ BotUtilities (shared helpers)
         │   └─→ ClassicBot (fallback)
         │
         └─→ ClassicBot
```

## Configuration

### Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `USE_GPT_MCP_BOT` | Enable GptMcpBot | `false` |
| `OLLAMA_URL` | Ollama server URL | `http://localhost:11434` |
| `MCP_SERVER_PATH` | Path to MCP server DLL | `/home/user/treachery.online/MCP/.../Treachery.MCP.Server.dll` |

### Enabling GptMcpBot

**Linux/Mac:**
```bash
export USE_GPT_MCP_BOT=true
export OLLAMA_URL=http://localhost:11434
export MCP_SERVER_PATH=/path/to/MCP/Server.dll
```

**Windows (PowerShell):**
```powershell
$env:USE_GPT_MCP_BOT="true"
$env:OLLAMA_URL="http://localhost:11434"
$env:MCP_SERVER_PATH="C:\path\to\MCP\Server.dll"
```

## Installation & Setup

### 1. Install Ollama

**Linux:**
```bash
curl -fsSL https://ollama.com/install.sh | sh
ollama serve
```

**Mac:**
```bash
brew install ollama
ollama serve
```

**Windows:**
Download from https://ollama.com/download

### 2. Install gpt-oss:20b Model

```bash
ollama pull gpt-oss:20b
```

This downloads ~12GB model. First run will take several minutes.

### 3. Build MCP Server

```bash
cd MCP/Treachery.MCP.Server
dotnet build -c Release
```

### 4. Test Installation

```bash
./test_gpt_mcp_bot.sh
```

If all checks pass ✅, you're ready to use GptMcpBot!

## How GptMcpBot Works

### Phase-Aware Behavior

The bot uses different strategies depending on game phase:

| Phase | Strategy | Reason |
|-------|----------|--------|
| Storm | ClassicBot | Deterministic calculation |
| Charity | ClassicBot | Simple resource check |
| Bidding | ClassicBot | Efficient card valuation |
| Revival | ClassicBot | Straightforward decision |
| **Ship/Move** | **GPT** | **Strategic positioning** |
| **Battle** | **GPT** | **Complex combat tactics** |
| **Deals** | **GPT** | **Negotiation reasoning** |
| **Alliances** | **GPT** | **Social dynamics** |

### Tool Calling Flow

1. **Turn starts** → Bot receives available actions
2. **GPT decision** → LLM receives system prompt + available actions
3. **Tool calls** → LLM calls MCP tools to gather context:
   - `GetGameState()` - Full board state
   - `GetStrategicAnalysis()` - Threat analysis, win conditions
   - `GetLegalActions()` - Valid actions with descriptions
   - `ValidateAction()` - Pre-validate specific action
4. **Analysis** → LLM analyzes tool results
5. **Decision** → LLM responds with `ACTION: [ActionName]`
6. **Execution** → Bot creates action via ClassicBot's logic
7. **Fallback** → If GPT fails, ClassicBot takes over

### Safety Mechanisms

- **Tool call limit**: Max 10 tool calls per decision (prevents infinite loops)
- **Timeout**: 30 second timeout per tool call
- **MCP auto-restart**: Server automatically restarts if crashed
- **Graceful fallback**: ClassicBot used if GPT fails
- **Invalid action tracking**: Logs when GPT attempts illegal moves

## Debugging & Monitoring

### Debug Logging (DEBUG Mode)

When compiled in DEBUG mode, extensive logging is available:

```
[GptMcpBot - Yellow] GptMcpBot determining LowPriority action in Battle/Battle from 5 options
[GptMcpBot - Yellow] Using ClassicBot for phase Battle
```

Or when using GPT:

```
[GptMcpBot - Yellow] Requesting GPT decision with tool access...
[Tool Call] GetGameState
  Arguments: {"gameId":"abc-123","faction":"Yellow"}
  Result: # DUNE BOARD GAME STATE...
[Tool Call] GetStrategicAnalysis
  Arguments: {"gameId":"abc-123","faction":"Yellow"}
  Result: {"threats":[{"Faction":"Blue","ThreatLevel":"High"...
[GptMcpBot - Yellow] Requesting final decision from GPT with tool results...
[GptMcpBot - Yellow] GPT selected action: BattlePlan
```

### Invalid Action Tracking

```
[BotUtilities] Invalid Action: Yellow attempted Move
  Reason: Not in available actions
  Phase: ShipmentAndMove / ShipmentAndMove
```

### MCP Server Monitoring

```
[MCP Server] Starting Dune MCP Server...
[MCP Request] {"jsonrpc":"2.0","id":1,"method":"initialize"...}
[MCP Response] {"jsonrpc":"2.0","id":1,"result":{"capabilities"...}}
[MCP Server] Process exited unexpectedly. Attempting restart...
```

### Metrics to Track

The bot logs statistics on disposal:

```
[GptMcpBot - Yellow] Session complete. Invalid actions: 3, Total tool calls: 47
```

**Key metrics:**
- **Invalid actions** - How often GPT chooses illegal moves (lower is better)
- **Tool calls per game** - Average tool usage (30-60 is typical)
- **Fallback frequency** - How often ClassicBot is used as fallback

## Performance Considerations

### Latency

| Operation | Time | Notes |
|-----------|------|-------|
| ClassicBot decision | <10ms | Instant |
| GPT tool call | 200-500ms | Per tool call |
| GPT final decision | 1-3 seconds | With 2-3 tool calls |
| MCP server startup | 500ms | One-time per game |

**Bot delay settings** (in `GameHub_GameEvents.cs`):
- Bidding: 1200ms
- Ship/Move/Battle: 4800ms (accounts for GPT latency)
- Other: 800ms

### Resource Usage

- **gpt-oss:20b memory**: ~12GB VRAM/RAM
- **MCP server**: ~50MB RAM
- **Ollama server**: ~100MB + model size

## Troubleshooting

### Problem: "Failed to initialize GptMcpBot"

**Symptoms:**
```
Failed to initialize GptMcpBot for Yellow: Connection refused. Falling back to ClassicBot.
```

**Solutions:**
1. Check Ollama is running: `curl http://localhost:11434/api/tags`
2. Check model is installed: `ollama list | grep gpt-oss`
3. Check MCP server path exists
4. Review server logs for detailed error

### Problem: "MCP Server Process exited unexpectedly"

**Symptoms:**
```
[MCP Server] Process exited unexpectedly. Attempting restart...
```

**Solutions:**
1. Check MCP server logs in stderr
2. Verify .NET runtime is available
3. Check file permissions on MCP server DLL
4. Manual test: `dotnet /path/to/Treachery.MCP.Server.dll`

### Problem: Bot makes invalid actions repeatedly

**Symptoms:**
```
[BotUtilities] Invalid Action: Yellow attempted Ship
  Reason: Not in available actions
```

**Solutions:**
1. Check tool call logs - is GPT getting correct game state?
2. Verify `GetLegalActions` returns accurate action list
3. Check for prompt issues in `GptMcpBot.BuildSystemPrompt()`
4. Consider adjusting temperature (currently 0.1 for deterministic)

### Problem: Bot takes too long to respond

**Symptoms:**
- Turn takes >10 seconds
- Timeout errors

**Solutions:**
1. Check Ollama server load
2. Verify gpt-oss:20b is fully loaded (not streaming from disk)
3. Reduce `MAX_TOOL_CALLS` in `GptMcpBot.cs`
4. Increase timeout in `OllamaChatClient` (currently 5 minutes)

## Development

### Adding New MCP Tools

1. **Define tool in MCP server** (`DuneGameTools.cs`):
```csharp
[Description("Get player's treachery cards")]
public async Task<string> GetPlayerCards(
    [Description("Game ID")] string gameId,
    [Description("Faction")] string faction)
{
    // Implementation
    return JsonSerializer.Serialize(cards);
}
```

2. **Add tool call handler** (`GptMcpBot.cs`):
```csharp
var result = toolName switch
{
    "GetGameState" => await _mcpClient.GetGameStateAsync(...),
    "GetPlayerCards" => await _mcpClient.GetPlayerCardsAsync(...),
    ...
};
```

3. **Update client method** (`McpServerClient.cs`):
```csharp
public async Task<string> GetPlayerCardsAsync(string gameId, Faction faction)
{
    var result = await CallToolAsync("GetPlayerCards", new { gameId, faction = faction.ToString() });
    return ExtractContent(result);
}
```

Tool is now automatically available to GPT!

### Modifying Phase-Aware Logic

Edit `BotUtilities.ShouldUseGemmaForPhase()`:

```csharp
// Add new phase to use GPT
if (game.CurrentMainPhase == MainPhase.YourNewPhase)
{
    return true; // Use GPT for this phase
}
```

### Tuning Prompts

Edit `GptMcpBot.BuildSystemPrompt()` and `BuildUserPrompt()`:

```csharp
private string BuildSystemPrompt()
{
    return @"You are an expert Dune board game AI...

    NEW INSTRUCTION: Always prioritize defensive play when behind.";
}
```

## Testing

### Unit Testing

See `Test/Tests.cs` for examples:

```csharp
var game = CreateTestGame();
var player = game.Players[0];
var bot = new GptMcpBot(game, player, parameters, mcpServerPath);
var action = bot.DetermineLowPriorityInPhaseAction(availableActions);
Assert.IsNotNull(action);
```

### Integration Testing

Run test script:
```bash
./test_gpt_mcp_bot.sh
```

### Manual Testing

1. Start game server with `USE_GPT_MCP_BOT=true`
2. Create game with bot players
3. Watch console logs for tool calls
4. Verify bot makes legal moves
5. Check session statistics at end

## FAQ

**Q: Can I use a different model?**
A: Yes! Change `gpt-oss:20b` in `GptMcpBot.cs` line 39. The model must support function calling.

**Q: Can multiple bots share one MCP server?**
A: Yes! The MCP server is started once per game and shared by all bot factions.

**Q: How do I disable tool calling logs?**
A: Remove the `#if DEBUG` blocks in `BotUtilities.LogToolCall()` or compile in Release mode.

**Q: Can I use GPT for all phases?**
A: Yes! In `BotUtilities.ShouldUseGemmaForPhase()`, return `true` for all phases. But this will be slower and may not improve quality for simple phases.

**Q: Does it work offline?**
A: Yes! Once you've pulled the gpt-oss:20b model, Ollama runs entirely locally. No internet required.

**Q: Can I contribute improvements?**
A: Yes! See the codebase on GitHub. Key files to review:
- `GptMcpBot.cs` - Main bot logic
- `BotUtilities.cs` - Shared helpers
- `McpServerClient.cs` - MCP protocol
- `DuneGameTools.cs` - MCP tools

## Version History

### v171 (2025-01-13)
- **Major refactor**: Replaced 4 experimental bots with unified GptMcpBot
- **Fixed**: AnalyzeThreatLevel bug (IsBot detection)
- **Added**: Proper MCP protocol implementation with JSON-RPC
- **Added**: Function calling support via Ollama chat API
- **Added**: Auto-restart for crashed MCP server
- **Added**: Tool call limiting (max 10 per decision)
- **Added**: Phase-aware behavior (GPT for strategic, ClassicBot for deterministic)
- **Added**: Comprehensive debug logging
- **Removed**: GemmaBot, PhaseAwareGemmaBot, StructuredGemmaBot, McpEnhancedGemmaBot
- **Removed**: 1,127 lines of duplicate code

### v170 (Previous)
- Experimental Gemma bot implementations
- Basic MCP integration (broken protocol)

## Support

For issues, questions, or contributions, please visit the GitHub repository or contact the development team.

---

**Last updated**: 2025-01-13
**Author**: Claude Code
**License**: GNU General Public License v3.0
