"""
EMA Pullback Strategy

This strategy trades pullbacks to a moving average in a trending market.
It identifies the trend direction and enters on retracements.

Ideal for prop firms because:
- Trades with the trend (higher probability)
- Clear entry points
- Defined risk management
- Works across multiple timeframes and instruments
"""

import pandas as pd
import numpy as np
from .base import BaseStrategy, StrategyConfig


class EMAPullbackStrategy(BaseStrategy):
    """
    Trade pullbacks to EMA in trending conditions.

    Entry: Price pulls back to EMA in direction of trend
    Exit: Target hit, stop hit, or trend reversal

    Parameters:
    - fast_ema: Fast EMA for pullback entry
    - slow_ema: Slow EMA for trend direction
    - trend_ema: Longer EMA for overall trend filter
    - pullback_threshold: How close price must get to EMA
    """

    def __init__(
        self,
        fast_ema: int = 9,
        slow_ema: int = 21,
        trend_ema: int = 50,
        pullback_threshold_pct: float = 0.1,  # Within 0.1% of EMA
        min_trend_strength: float = 0.15,  # Minimum EMA separation
        stop_loss_pct: float = 0.35,
        take_profit_pct: float = 0.5,
    ):
        config = StrategyConfig(
            name="EMA Pullback",
            stop_loss_pct=stop_loss_pct,
            take_profit_pct=take_profit_pct,
            max_trades_per_day=3,
            trade_start_hour=9,
            trade_start_minute=45,
            trade_end_hour=15,
            trade_end_minute=15,
            close_at_session_end=True
        )
        super().__init__(config)

        self.fast_ema = fast_ema
        self.slow_ema = slow_ema
        self.trend_ema = trend_ema
        self.pullback_threshold_pct = pullback_threshold_pct
        self.min_trend_strength = min_trend_strength

    def generate_signals(self, df: pd.DataFrame) -> pd.DataFrame:
        """
        Generate pullback signals in trending conditions.
        """
        df = df.copy()

        # Calculate EMAs
        df['ema_fast'] = df['close'].ewm(span=self.fast_ema, adjust=False).mean()
        df['ema_slow'] = df['close'].ewm(span=self.slow_ema, adjust=False).mean()
        df['ema_trend'] = df['close'].ewm(span=self.trend_ema, adjust=False).mean()

        # Calculate trend direction
        # Uptrend: fast > slow > trend
        # Downtrend: fast < slow < trend
        df['uptrend'] = (df['ema_fast'] > df['ema_slow']) & (df['ema_slow'] > df['ema_trend'])
        df['downtrend'] = (df['ema_fast'] < df['ema_slow']) & (df['ema_slow'] < df['ema_trend'])

        # Calculate EMA separation (trend strength)
        df['ema_separation'] = abs(df['ema_fast'] - df['ema_slow']) / df['ema_slow'] * 100

        # Calculate distance from fast EMA
        df['distance_from_ema'] = abs(df['close'] - df['ema_fast']) / df['ema_fast'] * 100

        # Calculate momentum (rate of change)
        df['momentum'] = df['close'].pct_change(5) * 100

        # Track if we're in a pullback
        df['in_pullback'] = False

        # Generate signals
        df['signal'] = 0

        in_uptrend_pullback = False
        in_downtrend_pullback = False

        for i in range(self.trend_ema + 5, len(df)):
            row = df.iloc[i]
            prev_row = df.iloc[i-1]

            # Skip if not enough trend strength
            if row['ema_separation'] < self.min_trend_strength:
                in_uptrend_pullback = False
                in_downtrend_pullback = False
                continue

            # UPTREND LOGIC
            if row['uptrend']:
                # Detect start of pullback (price moves toward EMA)
                if (prev_row['close'] > prev_row['ema_fast'] and
                    row['close'] <= row['ema_fast'] * (1 + self.pullback_threshold_pct/100)):
                    in_uptrend_pullback = True

                # Entry: Price bounces off EMA (touches and moves away)
                if in_uptrend_pullback:
                    # Price touched EMA and is now moving up
                    if (row['close'] > row['ema_fast'] and
                        prev_row['close'] <= prev_row['ema_fast'] and
                        row['close'] > prev_row['close']):
                        df.iloc[i, df.columns.get_loc('signal')] = 1
                        in_uptrend_pullback = False

            else:
                in_uptrend_pullback = False

            # DOWNTREND LOGIC
            if row['downtrend']:
                # Detect start of pullback (price moves toward EMA)
                if (prev_row['close'] < prev_row['ema_fast'] and
                    row['close'] >= row['ema_fast'] * (1 - self.pullback_threshold_pct/100)):
                    in_downtrend_pullback = True

                # Entry: Price bounces off EMA (touches and moves away)
                if in_downtrend_pullback:
                    # Price touched EMA and is now moving down
                    if (row['close'] < row['ema_fast'] and
                        prev_row['close'] >= prev_row['ema_fast'] and
                        row['close'] < prev_row['close']):
                        df.iloc[i, df.columns.get_loc('signal')] = -1
                        in_downtrend_pullback = False

            else:
                in_downtrend_pullback = False

        return df


# Allow running as standalone for testing
if __name__ == "__main__":
    from ..data_loader import generate_sample_data

    print("Testing EMA Pullback Strategy")
    print("=" * 50)

    # Generate test data
    df = generate_sample_data(days=60, seed=42)

    # Create and run strategy
    strategy = EMAPullbackStrategy()
    results = strategy.backtest(df)

    # Print results
    stats = strategy.get_statistics()
    print(f"\nStrategy: {stats['strategy']}")
    print(f"Total Trades: {stats['total_trades']}")
    print(f"Win Rate: {stats['win_rate']:.1f}%")
    print(f"Profit Factor: {stats['profit_factor']:.2f}")
    print(f"Consistency Score: {stats['consistency_score']:.1f}%")
    print(f"Max Drawdown: ${stats['max_drawdown']:.2f}")
