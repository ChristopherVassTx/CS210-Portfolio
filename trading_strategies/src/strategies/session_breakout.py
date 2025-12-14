"""
Session Range Breakout Strategy

This strategy identifies the high and low of an initial trading range
(first 30-60 minutes) and trades breakouts from that range.

Ideal for prop firms because:
- Clear entry/exit rules
- Defined risk (stop at opposite side of range)
- Works well in trending days
- Limited trades per day (1-2 max)
"""

import pandas as pd
import numpy as np
from .base import BaseStrategy, StrategyConfig


class SessionBreakoutStrategy(BaseStrategy):
    """
    Trade breakouts from the initial session range.

    Parameters:
    - range_minutes: Minutes to establish the range (default 30)
    - breakout_buffer: Extra points beyond range for entry
    - use_atr_filter: Only trade if range is reasonable vs ATR
    """

    def __init__(
        self,
        range_minutes: int = 30,
        breakout_buffer_pct: float = 0.02,  # 0.02% buffer
        min_range_pct: float = 0.1,  # Minimum range as % of price
        max_range_pct: float = 0.5,  # Maximum range as % of price
        stop_loss_pct: float = 0.3,
        take_profit_pct: float = 0.6,
    ):
        config = StrategyConfig(
            name="Session Range Breakout",
            stop_loss_pct=stop_loss_pct,
            take_profit_pct=take_profit_pct,
            max_trades_per_day=2,  # Limit to 2 trades per day
            trade_start_hour=10,  # Start after range is established
            trade_start_minute=0,
            trade_end_hour=15,
            trade_end_minute=0,
            close_at_session_end=True
        )
        super().__init__(config)

        self.range_minutes = range_minutes
        self.breakout_buffer_pct = breakout_buffer_pct
        self.min_range_pct = min_range_pct
        self.max_range_pct = max_range_pct

    def generate_signals(self, df: pd.DataFrame) -> pd.DataFrame:
        """
        Generate breakout signals based on session range.
        """
        df = df.copy()
        df['signal'] = 0
        df['range_high'] = np.nan
        df['range_low'] = np.nan

        # Group by date to calculate daily ranges
        df['date'] = df.index.date

        for date in df['date'].unique():
            day_mask = df['date'] == date
            day_data = df[day_mask]

            if len(day_data) < 10:  # Need enough data
                continue

            # Find range establishment period (first N minutes from 9:30)
            range_start = day_data.index[0]
            range_end_time = range_start + pd.Timedelta(minutes=self.range_minutes)

            range_mask = day_data.index <= range_end_time
            range_data = day_data[range_mask]

            if len(range_data) < 3:
                continue

            # Calculate range high and low
            range_high = range_data['high'].max()
            range_low = range_data['low'].min()
            range_size = range_high - range_low
            range_pct = (range_size / range_low) * 100

            # Filter: range must be reasonable (not too tight, not too wide)
            if range_pct < self.min_range_pct or range_pct > self.max_range_pct:
                continue

            # Calculate breakout levels with buffer
            buffer = range_low * (self.breakout_buffer_pct / 100)
            breakout_high = range_high + buffer
            breakout_low = range_low - buffer

            # Store range levels for visualization
            df.loc[day_mask, 'range_high'] = range_high
            df.loc[day_mask, 'range_low'] = range_low

            # Generate signals for the rest of the day
            trading_mask = (df['date'] == date) & (df.index > range_end_time)
            trading_data = df[trading_mask]

            breakout_triggered = False

            for idx in trading_data.index:
                if breakout_triggered:
                    break

                row = df.loc[idx]

                # Long breakout: close above range high
                if row['close'] > breakout_high:
                    df.loc[idx, 'signal'] = 1
                    breakout_triggered = True

                # Short breakout: close below range low
                elif row['close'] < breakout_low:
                    df.loc[idx, 'signal'] = -1
                    breakout_triggered = True

        df.drop('date', axis=1, inplace=True)
        return df


# Allow running as standalone for testing
if __name__ == "__main__":
    from ..data_loader import generate_sample_data

    print("Testing Session Range Breakout Strategy")
    print("=" * 50)

    # Generate test data
    df = generate_sample_data(days=60, seed=42)

    # Create and run strategy
    strategy = SessionBreakoutStrategy()
    results = strategy.backtest(df)

    # Print results
    stats = strategy.get_statistics()
    print(f"\nStrategy: {stats['strategy']}")
    print(f"Total Trades: {stats['total_trades']}")
    print(f"Win Rate: {stats['win_rate']:.1f}%")
    print(f"Profit Factor: {stats['profit_factor']:.2f}")
    print(f"Consistency Score: {stats['consistency_score']:.1f}%")
    print(f"Max Drawdown: ${stats['max_drawdown']:.2f}")
