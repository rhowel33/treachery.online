# Gemma 3 AI Integration Setup

This guide explains how to configure the Dune board game to use Gemma 3 via Ollama for AI decisions instead of the rule-based ClassicBot.

## Prerequisites

1. **Install Ollama**: Download and install from [https://ollama.ai](https://ollama.ai)
2. **Pull Gemma 3 model**: Run `ollama pull gemma2:3b` in your terminal
3. **Start Ollama server**: Run `ollama serve` (usually starts on http://localhost:11434)

## Configuration

Set these environment variables to enable Gemma bot:

```bash
# Enable Gemma bot (required)
export USE_GEMMA_BOT=true

# Configure Ollama connection (optional, defaults shown)
export OLLAMA_URL=http://localhost:11434
export OLLAMA_MODEL=gemma2:3b
```

### For Development (Visual Studio/VS Code)

Add to your `launchSettings.json`:

```json
{
  "environmentVariables": {
    "USE_GEMMA_BOT": "true",
    "OLLAMA_URL": "http://localhost:11434",
    "OLLAMA_MODEL": "gemma2:3b"
  }
}
```

### For Production/Docker

Add to your environment configuration or docker-compose.yml:

```yaml
environment:
  - USE_GEMMA_BOT=true
  - OLLAMA_URL=http://ollama:11434
  - OLLAMA_MODEL=gemma2:3b
```

## How It Works

1. **Hybrid Approach**: GemmaBot uses the LLM to choose which action to take, but falls back to ClassicBot for action parameter details
2. **Game State Context**: The LLM receives detailed game state information including:
   - Current turn and phase
   - Player resources and positions
   - Victory conditions status
   - Available actions
3. **Validation**: All LLM decisions are validated; invalid choices fall back to ClassicBot
4. **Performance**: Responses are typically 1-3 seconds depending on your hardware

## Troubleshooting

- **Connection Issues**: Ensure Ollama is running and accessible at the configured URL
- **Model Issues**: Verify the model is pulled with `ollama list`
- **Fallback Behavior**: If Gemma fails, the game automatically uses ClassicBot
- **Logs**: Check console output for GemmaBot initialization and decision messages

## Model Recommendations

- **gemma2:3b**: Fast responses, good strategic decisions (recommended)
- **gemma2:9b**: Better strategic thinking, slower responses
- **llama3.1:8b**: Alternative model with good game reasoning

## Performance Notes

- First decision per game may be slower due to model loading
- Consider using smaller models (3b) for real-time gameplay
- Larger models (9b+) provide better strategic reasoning but slower responses