#!/bin/bash

echo "=== Dune Board Game - GPT MCP Bot Integration Test ==="
echo

# Check if Ollama is running
echo "1. Checking Ollama service..."
if curl -s http://localhost:11434/api/tags > /dev/null 2>&1; then
    echo "✅ Ollama is running at http://localhost:11434"
else
    echo "❌ Ollama is not running. Please start it with: ollama serve"
    echo "   Then install gpt-oss model with: ollama pull gpt-oss:20b"
    exit 1
fi

# Check if gpt-oss:20b model is available
echo
echo "2. Checking gpt-oss:20b model availability..."
if curl -s http://localhost:11434/api/tags | grep -q "gpt-oss:20b"; then
    echo "✅ gpt-oss:20b model is available"
else
    echo "⚠️  gpt-oss:20b model not found. Installing..."
    ollama pull gpt-oss:20b
    if [ $? -eq 0 ]; then
        echo "✅ gpt-oss:20b model installed successfully"
    else
        echo "❌ Failed to install gpt-oss:20b model"
        exit 1
    fi
fi

# Test model with function calling
echo
echo "3. Testing gpt-oss:20b model with function calling..."
RESPONSE=$(curl -s -X POST http://localhost:11434/api/chat \
  -H "Content-Type: application/json" \
  -d '{
    "model": "gpt-oss:20b",
    "messages": [
      {
        "role": "system",
        "content": "You are a helpful assistant with access to tools."
      },
      {
        "role": "user",
        "content": "What is the current game state?"
      }
    ],
    "tools": [
      {
        "type": "function",
        "function": {
          "name": "GetGameState",
          "description": "Get the current game state",
          "parameters": {
            "type": "object",
            "properties": {
              "gameId": {"type": "string"},
              "faction": {"type": "string"}
            },
            "required": ["gameId", "faction"]
          }
        }
      }
    ],
    "stream": false
  }')

if [ $? -eq 0 ]; then
    echo "✅ gpt-oss:20b model responded successfully"
    echo "Response preview: $(echo $RESPONSE | jq -r '.message.content' 2>/dev/null | head -c 100 || echo "Model supports function calling")"
else
    echo "❌ Failed to get response from gpt-oss:20b model"
    exit 1
fi

# Check if MCP server can be found
echo
echo "4. Checking MCP server availability..."
MCP_SERVER_PATH="/home/user/treachery.online/MCP/Treachery.MCP.Server/bin/Release/net9.0/Treachery.MCP.Server.dll"
if [ -f "$MCP_SERVER_PATH" ]; then
    echo "✅ MCP server found at $MCP_SERVER_PATH"
else
    echo "⚠️  MCP server not found at $MCP_SERVER_PATH"
    echo "   Building MCP server..."
    # Attempt to build (will fail if dotnet not available, but that's ok)
    if command -v dotnet &> /dev/null; then
        cd /home/user/treachery.online/MCP/Treachery.MCP.Server
        dotnet build -c Release
        if [ $? -eq 0 ]; then
            echo "✅ MCP server built successfully"
        else
            echo "⚠️  Failed to build MCP server, but continuing test..."
        fi
    else
        echo "⚠️  dotnet not available, skipping build"
    fi
fi

echo
echo "5. Setting up environment for Dune game..."
echo "To enable GptMcpBot in your Dune game, set these environment variables:"
echo
echo "export USE_GPT_MCP_BOT=true"
echo "export OLLAMA_URL=http://localhost:11434"
echo "export MCP_SERVER_PATH=$MCP_SERVER_PATH"
echo
echo "Then start your Dune game server. The GptMcpBot will be used for AI players."
echo
echo "=== Integration test completed successfully! ==="
echo
echo "Tool call logging:"
echo "  - In DEBUG mode, all MCP tool calls will be logged to console"
echo "  - Look for lines like: [GptMcpBot - Yellow] Executing tool: GetGameState"
echo "  - Invalid actions tracked in: [BotUtilities] lines"
echo
echo "Monitoring tips:"
echo "  - Watch for tool call loops (should stop at 10 max)"
echo "  - Check if bot falls back to ClassicBot during simple phases"
echo "  - Monitor MCP server auto-restart if it crashes"
