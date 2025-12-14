# NinjaTrader 8 Automated Strategies

Fully automated trading strategies for NinjaTrader 8, optimized for prop firm consistency.

## Installation

1. Open NinjaTrader 8
2. Go to **Tools → Import → NinjaScript Add-On**
3. Select the `.cs` file you want to import
4. Or manually copy to: `Documents\NinjaTrader 8\bin\Custom\Strategies\`
5. Compile via **New → NinjaScript Editor → Right-click → Compile**

## Session Times (Eastern Time)

| Session | Start | End | Notes |
|---------|-------|-----|-------|
| London | 3:00 AM | 12:00 PM | High volatility overlap with Asia close |
| New York | 9:30 AM | 4:00 PM | Most liquid, best for US futures |
| Overlap | 8:00 AM | 12:00 PM | Highest volume period |

## Strategies Included

### 1. SessionRangeBreakout
- Builds range during first 30 minutes of session
- Trades breakouts from that range
- Best for trending days
- **Recommended: ES, NQ futures**

### 2. MeanReversionScalper
- Trades deviations from EMA with RSI confirmation
- High win rate (65-70% target)
- Quick scalps for consistent small profits
- **Recommended: ES, NQ, CL futures**

### 3. EMAPullback
- Trend following with pullback entries
- Trades with the trend for higher probability
- Works across multiple instruments
- **Recommended: All liquid futures**

## Running Automated

To run these strategies while you're at work:

1. **Enable Strategy**:
   - Right-click chart → Strategies → Add strategy
   - Enable "Auto-Trade" checkbox
   - Set account and position size

2. **VPS Recommended**:
   - Run NinjaTrader on a VPS for 24/5 operation
   - Ensures execution even if home PC is off

3. **Risk Settings** (in NinjaTrader):
   - Set daily loss limit in Control Center → Options → Strategies
   - Enable "Disable strategy on close" if desired

## Prop Firm Settings

Default parameters are set conservatively for prop firms:
- Max 2-5 trades per day depending on strategy
- Tight stop losses (0.25-0.5%)
- Reasonable take profits (0.35-0.6%)
- Auto-close at session end

Adjust based on your prop firm's specific rules.
