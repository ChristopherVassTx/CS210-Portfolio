# Prop Firm Trading Strategies

A collection of **consistency-focused** trading strategies designed for scaling across multiple prop firm accounts.

## Philosophy

> "Small consistent profits scaled across 20-50 accounts will more than make up for wild swings."

These strategies prioritize:
- **Low drawdown** over high returns
- **High win rate** for psychological ease
- **Consistency** measured daily/weekly
- **Scalability** across multiple accounts
- **Full automation** for hands-off trading during London & NY sessions

## Strategies Included

| Strategy | Type | Best For | Expected Win Rate |
|----------|------|----------|-------------------|
| Session Range Breakout | Breakout | Futures/Forex | 55-60% |
| Mean Reversion Scalper | Mean Reversion | Indices/Forex | 65-70% |
| EMA Pullback | Trend Following | All Markets | 50-55% |

## Project Structure

```
trading_strategies/
├── ninjascript/              # NinjaTrader 8 strategies (PRIMARY)
│   ├── SessionRangeBreakout.cs
│   ├── MeanReversionScalper.cs
│   ├── EMAPullback.cs
│   └── README.md             # NinjaTrader installation guide
├── src/                      # Python backtesting & research
│   ├── strategies/
│   │   ├── base.py
│   │   ├── session_breakout.py
│   │   ├── mean_reversion.py
│   │   └── ema_pullback.py
│   ├── analytics.py
│   └── data_loader.py
├── pinescript/               # TradingView (reference only)
│   ├── session_breakout.pine
│   ├── mean_reversion.pine
│   └── ema_pullback.pine
├── compare_strategies.py
└── requirements.txt
```

## NinjaTrader 8 Setup (Recommended)

### Installation
1. Copy `.cs` files to: `Documents\NinjaTrader 8\bin\Custom\Strategies\`
2. Open NinjaTrader → Tools → NinjaScript Editor
3. Right-click → Compile

### Running Automated (While You're at Work)

1. **Add Strategy to Chart**:
   - Right-click chart → Strategies → Add strategy
   - Select your strategy
   - Check "Enabled" and set to "Auto-Trade"

2. **Configure Sessions** (in strategy parameters):
   - Trade London: 3:00 AM - 11:00 AM ET
   - Trade New York: 9:30 AM - 3:30 PM ET
   - Or both for maximum opportunities

3. **VPS Recommended**:
   - Run NinjaTrader on a Windows VPS for 24/5 operation
   - Ensures execution even when your PC is off
   - Providers: AWS, Azure, or dedicated trading VPS services

### Risk Settings (Important for Prop Firms)

In NinjaTrader Control Center → Options → Strategies:
- Set daily loss limit
- Enable "Disable strategy on connection loss"
- Configure position size per your prop firm rules

## Python Backtesting

Use Python for research and optimization before deploying to NinjaTrader:

```bash
# Install dependencies
pip install -r requirements.txt

# Run strategy comparison
python compare_strategies.py
```

## Key Metrics for Prop Firms

- **Max Daily Drawdown**: Must stay under firm limits (typically 2-5%)
- **Max Total Drawdown**: Usually 6-10% depending on firm
- **Profit Factor**: Target > 1.5
- **Consistency Score**: % of profitable days

## Session Times Reference (Eastern Time)

| Session | Start | End | Characteristics |
|---------|-------|-----|-----------------|
| London | 3:00 AM | 12:00 PM | High volatility, good trends |
| New York | 9:30 AM | 4:00 PM | Most liquid, best fills |
| Overlap | 8:00 AM | 12:00 PM | Highest volume, best moves |

## Scaling Strategy

1. **Start Small**: Test on 1-2 accounts for 2-4 weeks
2. **Validate**: Ensure consistency matches backtests
3. **Scale**: Add accounts gradually (5 at a time)
4. **Monitor**: Track aggregate performance across all accounts
5. **Adjust**: Fine-tune parameters based on live results

## Disclaimer

These strategies are for educational purposes. Past performance does not guarantee future results. Always test thoroughly on demo accounts before risking real capital. Understand your prop firm's rules before deploying any automated strategy.
