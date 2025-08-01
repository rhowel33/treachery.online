#!/bin/bash

echo "=== Dune Board Game - Gemma 3 AI Integration Test ==="
echo

# Check if Ollama is running
echo "1. Checking Ollama service..."
if curl -s http://localhost:11434/api/tags > /dev/null 2>&1; then
    echo "✅ Ollama is running at http://localhost:11434"
else
    echo "❌ Ollama is not running. Please start it with: ollama serve"
    echo "   Then install Gemma with: ollama pull gemma2:3b"
    exit 1
fi

# Check if Gemma model is available
echo
echo "2. Checking Gemma 3 model availability..."
if curl -s http://localhost:11434/api/tags | grep -q "gemma2:3b"; then
    echo "✅ Gemma 2:3b model is available"
else
    echo "⚠️  Gemma 2:3b model not found. Installing..."
    ollama pull gemma2:3b
    if [ $? -eq 0 ]; then
        echo "✅ Gemma 2:3b model installed successfully"
    else
        echo "❌ Failed to install Gemma 2:3b model"
        exit 1
    fi
fi

# Test model with a simple prompt
echo
echo "3. Testing Gemma model with a simple game decision..."
RESPONSE=$(curl -s -X POST http://localhost:11434/api/generate \
  -H "Content-Type: application/json" \
  -d '{
    "model": "gemma2:3b",
    "prompt": "You are playing the Dune board game. You have these actions available: Move, Shipment, Battle. You are winning with 2 strongholds and need 1 more to win. What should you do? Respond with only the action name.",
    "stream": false,
    "options": {
      "temperature": 0.1,
      "num_predict": 50
    }
  }')

if [ $? -eq 0 ]; then
    echo "✅ Gemma model responded successfully"
    echo "Response: $(echo $RESPONSE | jq -r '.response' 2>/dev/null || echo "Could not parse response")"
else
    echo "❌ Failed to get response from Gemma model"
    exit 1
fi

echo
echo "4. Setting up environment for Dune game..."
echo "To enable Gemma bot in your Dune game, set these environment variables:"
echo
echo "export USE_GEMMA_BOT=true"
echo "export OLLAMA_URL=http://localhost:11434"
echo "export OLLAMA_MODEL=gemma2:3b"
echo
echo "Then start your Dune game server. The GemmaBot will be used for AI players."
echo
echo "=== Integration test completed successfully! ==="