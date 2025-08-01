# Human-Bot Alliance Communication Guide

This guide explains how to communicate with your bot ally using chat commands to coordinate strategy in the Dune board game.

## 🤝 How It Works

When you're allied with a bot, you can send strategic instructions through private chat messages. The GemmaBot will understand your commands and incorporate them into its decision-making process.

## 📝 Basic Command Format

Simply send a private message to your bot ally using natural language commands. The bot will automatically parse your instructions and confirm receipt.

## 🎯 Available Commands

### Movement Commands
- `move 5 forces from Arrakeen to Carthag`
- `move forces from stronghold to spice`
- `retreat from battle`

### Combat Commands  
- `attack Blue at Sietch Tabr`
- `attack Arrakeen with 8 forces`
- `defend Carthag`
- `initiate battle`

### Shipment Commands
- `ship 6 forces to Arrakeen`
- `ship maximum forces to spice location`
- `focus on shipping this turn`

### Bidding Commands
- `bid 8 resources on this card`
- `save 5 resources for later`
- `spend up to 12 resources`
- `pass on bidding`

### Strategic Commands
- `focus on winning strongholds`
- `prioritize spice collection`
- `avoid combat this turn`
- `wait this turn`
- `play defensive strategy`

### Alliance Commands
- `ally with Green`
- `break alliance`
- `coordinate with me`

### Card Commands
- `play shield card`
- `keep weapon cards`
- `use treachery if needed`

## 💬 Example Conversations

### Planning a Joint Attack
**You:** "Attack Blue at Carthag with maximum forces"  
**Bot:** "🤖 Blue Bot received instruction: Attack Blue at Carthag"

### Resource Coordination
**You:** "Save 8 resources, I'll handle the bidding this round"  
**Bot:** "🤖 Yellow Bot received instruction: Save 8 resources"

### Defensive Strategy
**You:** "Defend our strongholds, avoid unnecessary battles"  
**Bot:** "🤖 Green Bot received instruction: Focus on defensive strategy"

## ⚡ Advanced Features

### Priority Levels
- **High Priority**: Combat, movement, alliance actions (followed immediately)
- **Medium Priority**: Bidding, resource management (considered strongly) 
- **Low Priority**: Card management (used as guidance)

### Instruction Persistence
- **One-time commands** (attack, move, bid) are cleared after execution
- **Strategic guidance** (focus, avoid, prioritize) persists until overridden
- **General instructions** last up to 10 minutes

### Fallback Behavior
- If your instruction doesn't match available actions, the bot chooses the closest alternative
- Invalid instructions automatically fall back to standard AI behavior
- Bot always validates moves before executing

## 🔧 Setup Requirements

1. **Enable GemmaBot**: Set environment variable `USE_GEMMA_BOT=true`
2. **Form Alliance**: You must be allied with the bot player
3. **Private Chat**: Send instructions via private message to bot ally only

## 📊 Command Examples by Game Phase

### Early Game
- `ship forces to spice locations`
- `focus on resource collection`
- `bid conservatively on cards`

### Mid Game  
- `move forces to threaten strongholds`
- `coordinate attacks with my forces`
- `prioritize winning positions`

### Late Game
- `attack weakest stronghold holder`
- `prevent [Player] from winning`
- `use all resources for final push`

## ⚠️ Important Notes

- Commands only work with bot allies (not human players)
- Bot will confirm receipt of valid instructions
- Instructions are processed in real-time during bot turns
- Complex strategies may require multiple simple commands
- Bot maintains game rule compliance regardless of instructions

## 🎮 Tips for Effective Communication

1. **Be Specific**: "Attack Arrakeen" is better than "attack somewhere"
2. **Consider Timing**: Give instructions before the bot's turn when possible
3. **Coordinate Resources**: Plan who bids on what cards
4. **Communicate Changes**: Update strategy as game situation evolves
5. **Use Simple Language**: Clear, direct commands work best

With this system, human-bot alliances become much more strategic and coordinated, allowing for sophisticated gameplay that rivals human-human alliances!