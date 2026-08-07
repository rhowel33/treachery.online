/*
 * Copyright (C) 2020-2025 Ronald Ossendrijver (admin@treachery.online)
 * This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version. This
 * program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License for more details. You should have
 * received a copy of the GNU General Public License along with this program. If not, see <http://www.gnu.org/licenses/>.
 */

using System.Threading.Tasks;

namespace Treachery.Shared.Model;

public interface IBot
{
    public Task<GameEvent?> DetermineHighestPriorityInPhaseAction(List<Type> events);
    public Task<GameEvent?> DetermineHighPriorityInPhaseAction(List<Type> events);
    public Task<GameEvent?> DetermineMiddlePriorityInPhaseAction(List<Type> events);
    public Task<GameEvent?> DetermineLowPriorityInPhaseAction(List<Type> events);
    public Task<GameEvent?> DetermineEndPhaseAction(List<Type> events);
    public void SetGameAndPlayer(Game gameGame, Player player);
}