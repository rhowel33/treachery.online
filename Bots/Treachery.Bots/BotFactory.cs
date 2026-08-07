/*
 * Copyright (C) 2020-2025 Ronald Ossendrijver (admin@treachery.online)
 * This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version. This
 * program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License for more details. You should have
 * received a copy of the GNU General Public License along with this program. If not, see <http://www.gnu.org/licenses/>.
 */

namespace Treachery.Bots;

/// <summary>
/// Creates bots. When the environment variable TREACHERY_OLLAMA_MODEL is set (e.g. "llama3.1:8b"),
/// LLM-powered bots are created that use the Ollama server at TREACHERY_OLLAMA_URL
/// (default http://localhost:11434); otherwise the classic heuristic bots are used.
/// </summary>
public static class BotFactory
{
    public static IBot CreateBot(Game game, Player player, BotParameters parameters)
    {
        var model = Environment.GetEnvironmentVariable("TREACHERY_OLLAMA_MODEL");

        if (string.IsNullOrWhiteSpace(model))
            return new ClassicBot(game, player, parameters);

        var url = Environment.GetEnvironmentVariable("TREACHERY_OLLAMA_URL") ?? "http://localhost:11434";
        return new OllamaBot(game, player, parameters, new OllamaClient(url, model));
    }
}
