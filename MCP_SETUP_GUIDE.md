# MCP Server Setup Guide for Dune Board Game

## 🚀 **What is MCP Integration?**

The Model Context Protocol (MCP) server provides Gemma with **much better access** to game state information and legal action validation. Instead of relying on text-based prompts, Gemma can now:

- **Query real-time game state** with structured data
- **Validate actions before choosing them** to prevent illegal moves  
- **Access strategic analysis** with threat detection and win condition tracking
- **Get pre-validated legal actions** to avoid hallucinated moves
- **Integrate human ally instructions** seamlessly

## 🏗️ **Architecture Overview**

```
┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐
│   Gemma LLM     │◄──►│ McpEnhanced     │◄──►│   MCP Server    │
│   (Ollama)      │    │   GemmaBot      │    │  (Standalone)   │
└─────────────────┘    └─────────────────┘    └─────────────────┘
                                │                       │
                                ▼                       ▼
                       ┌─────────────────┐    ┌─────────────────┐
                       │   ClassicBot    │    │  Game State +   │
                       │   (Fallback)    │    │  Validation     │
                       └─────────────────┘    └─────────────────┘
```

## 📦 **Installation & Setup**

### **1. Build the MCP Server**
```bash
cd MCP/Treachery.MCP.Server
dotnet build
```

### **2. Configure Environment Variables**
```bash
# Enable MCP-enhanced bot
export USE_MCP_GEMMA_BOT=true

# Standard Gemma configuration  
export USE_GEMMA_BOT=true
export OLLAMA_URL=http://localhost:11434
export OLLAMA_MODEL=gemma3:latest

# Optional: MCP server path (auto-detected by default)
export MCP_SERVER_PATH=/path/to/MCP/server/executable
```

### **3. Update Server Integration**
The server will automatically use `McpEnhancedGemmaBot` when `USE_MCP_GEMMA_BOT=true` is set.

## 🛠️ **MCP Server Capabilities**

### **Core Tools Available to Gemma:**

#### **1. GetGameState**
```json
{
  "gameId": "game123",
  "faction": "Atreides"
}
```
Returns comprehensive game state including:
- Current turn, phase, and storm position
- Player resources, forces, and cards
- Territory control and stronghold status
- Recent events and battle outcomes

#### **2. ValidateAction** 
```json
{
  "gameId": "game123", 
  "faction": "Atreides",
  "actionType": "Move"
}
```
Returns:
- `IsValid`: true/false
- `Reason`: Why action is invalid (if applicable)
- `ActionMessage`: What the action would do

#### **3. GetStrategicAnalysis**
```json
{
  "gameId": "game123",
  "faction": "Atreides" 
}
```
Returns strategic analysis including:
- Win condition progress (strongholds, victory points)
- Threat assessment from other players
- Resource analysis and spice availability
- Faction-specific advantages

#### **4. GetLegalActions**
```json
{
  "gameId": "game123",
  "faction": "Atreides"
}
```
Returns all legal actions with descriptions:
- Pre-validated action types
- Action descriptions and strategic context
- Phase-appropriate actions only

## 🎯 **Key Improvements Over Standard GemmaBot**

### **Before (Text-Only)**
```
Prompt: "You have these actions: Move, Battle, Bid..."
Gemma: "Move" 
Bot: Creates Move action → Validation fails → Fallback to ClassicBot
```

### **After (MCP-Enhanced)**
```
MCP: GetLegalActions() → ["Move", "Battle"] (pre-validated)
MCP: ValidateAction("Move") → {IsValid: true, ActionMessage: "Move 3 forces from Arrakeen to Carthag"}
Gemma: "Move" (with strategic context)
Bot: Creates Move action → ✅ Success!
```

## 🎮 **User Experience Improvements**

### **Fewer Illegal Moves**
- All actions are pre-validated through MCP
- Gemma sees only legal options
- Detailed validation prevents edge case failures

### **Better Strategic Decisions**
- Real-time threat analysis
- Accurate resource and territory information  
- Win condition tracking
- Human ally instruction integration

### **Improved Performance**
- Structured data instead of long text prompts
- Efficient action validation
- Strategic phase-aware routing (inherited from PhaseAwareGemmaBot)

## 🔧 **Configuration Options**

### **Environment Variables**
```bash
# MCP Integration
USE_MCP_GEMMA_BOT=true           # Enable MCP-enhanced bot
MCP_SERVER_PATH=/path/to/server  # Custom MCP server location

# Gemma Configuration  
USE_GEMMA_BOT=true               # Enable any Gemma bot
OLLAMA_URL=http://localhost:11434
OLLAMA_MODEL=gemma3:latest

# Fallback Configuration
MCP_FALLBACK_TO_PHASE_AWARE=true # Fallback to PhaseAwareGemmaBot if MCP fails
```

### **Automatic Fallback Chain**
```
McpEnhancedGemmaBot → PhaseAwareGemmaBot → ClassicBot
```

## 📊 **Monitoring & Debugging**

### **MCP Server Logs**
```bash
[MCP Server] Game game123 registered successfully
[MCP Server] Validating action Move for Atreides: Valid
[MCP Server] Strategic analysis requested for Atreides
```

### **Bot Logs**
```bash
[McpEnhancedGemmaBot] Atreides: Using Gemma with MCP support for strategic decision
[McpEnhancedGemmaBot] Atreides: MCP Strategic Analysis: {PlayerStatus: {...}, Threats: [...]}
[McpEnhancedGemmaBot] Atreides: MCP validation confirmed Move is legal
[McpEnhancedGemmaBot] Atreides: MCP-Enhanced Gemma chose valid action: Move 3 forces from Arrakeen to Carthag
```

### **Error Handling**
- MCP connection failures → Automatic fallback to PhaseAwareGemmaBot
- Server timeouts → Graceful degradation with logging
- Validation errors → Detailed error messages and fallback chains

## 🚀 **Running the System**

### **1. Start Ollama with Gemma**
```bash
ollama serve
ollama pull gemma3:latest
```

### **2. Start the Game Server**
```bash
cd Server
export USE_MCP_GEMMA_BOT=true
export USE_GEMMA_BOT=true
export OLLAMA_MODEL=gemma3:latest
dotnet run
```

### **3. MCP Server Auto-Start**
The MCP server will automatically start when the first bot needs it. No manual intervention required!

## 🎯 **Expected Results**

With MCP integration, you should see:

✅ **Significantly fewer illegal moves** (Gemma can't choose invalid actions)  
✅ **Better strategic decisions** (real-time threat analysis and game state)  
✅ **Faster decision making** (pre-validated actions, no retry loops)  
✅ **Improved human-bot coordination** (structured ally instruction handling)  
✅ **Enhanced debugging** (detailed logs of MCP interactions)  

The Emperor should **never again** try to use Lasegun as Karama! 🎭

## 🔍 **Troubleshooting**

### **MCP Server Won't Start**
- Check .NET 9.0 is installed
- Verify MCP server binary exists in expected location
- Check permissions on executable

### **Connection Issues**
- MCP server process dies → Check server logs
- Timeout errors → Increase connection timeout in McpClient
- Permission errors → Run with appropriate privileges

### **Validation Failures**  
- Actions still invalid → Check game state synchronization
- Missing actions → Verify action types are properly serialized
- Unexpected errors → Enable detailed MCP logging

The MCP integration provides a **major upgrade** to bot decision-making quality while maintaining all the performance optimizations from the phase-aware system! 🚀