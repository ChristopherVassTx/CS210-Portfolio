"""
Mean Reversion Scalper Strategy

This strategy trades price deviations from a moving average,
betting that price will return to the mean.

Ideal for prop firms because:
- High win rate (typically 65-70%)
- Small, consistent profits
- Quick trades (reduced exposure)
- Works well in ranging/choppy markets
"""

import pandas as pd
import numpy as np
from .base import BaseStrategy, StrategyConfig


class MeanReversionStrategy(BaseStrategy):
    """
    Trade mean reversion using EMA and standard deviation bands.

    Entry: Price deviates significantly from EMA (outside bands)
    Exit: Price returns to EMA or hits stop/target

    Parameters:
    - ema_period: Period for the moving average
    - std_period: Period for standard deviation calculation
    - entry_std: Number of std devs for entry signal
    - exit_std: Number of std devs for exit (closer to mean)
    """

    def __init__(
        self,
        ema_period: int = 20,
        std_period: int = 20,
        entry_std: float = 2.0,
        exit_std: float = 0.5,
        stop_loss_pct: float = 0.25,  # Tight stop for scalping
        take_profit_pct: float = 0.35,  # Small but consistent target
        require_volume_confirmation: bool = True
    ):
        config = StrategyConfig(
            name="Mean Reversion Scalper",
            stop_loss_pct=stop_loss_pct,
            take_profit_pct=take_profit_pct,
            max_trades_per_day=5,  # More trades allowed for scalping
            trade_start_hour=9,
            trade_start_minute=45,
            trade_end_hour=15,
            trade_end_minute=30,
            close_at_session_end=True
        )
        super().__init__(config)

        self.ema_period = ema_period
        self.std_period = std_period
        self.entry_std = entry_std
        self.exit_std = exit_std
        self.require_volume_confirmation = require_volume_confirmation

    def generate_signals(self, df: pd.DataFrame) -> pd.DataFrame:
        """
        Generate mean reversion signals.
        """
        df = df.copy()

        # Calculate EMA
        df['ema'] = df['close'].ewm(span=self.ema_period, adjust=False).mean()

        # Calculate rolling standard deviation
        df['std'] = df['close'].rolling(window=self.std_period).std()

        # Calculate bands
        df['upper_band'] = df['ema'] + (df['std'] * self.entry_std)
        df['lower_band'] = df['ema'] - (df['std'] * self.entry_std)

        # Calculate exit bands (closer to mean)
        df['upper_exit'] = df['ema'] + (df['std'] * self.exit_std)
        df['lower_exit'] = df['ema'] - (df['std'] * self.exit_std)

        # Calculate volume moving average for confirmation
        df['volume_ma'] = df['volume'].rolling(window=20).mean()

        # Calculate RSI for additional confirmation
        delta = df['close'].diff()
        gain = (delta.where(delta > 0, 0)).rolling(window=14).mean()
        loss = (-delta.where(delta < 0, 0)).rolling(window=14).mean()
        rs = gain / loss
        df['rsi'] = 100 - (100 / (1 + rs))

        # Generate signals
        df['signal'] = 0

        for i in range(self.ema_period, len(df)):
            row = df.iloc[i]
            prev_row = df.iloc[i-1]

            # Skip if bands are not calculated
            if pd.isna(row['upper_band']) or pd.isna(row['lower_band']):
                continue

            # Volume confirmation (optional)
            volume_ok = True
            if self.require_volume_confirmation:
                volume_ok = row['volume'] > row['volume_ma'] * 0.8

            # Long signal: Price below lower band and showing reversal
            # RSI oversold adds confirmation
            if (row['close'] < row['lower_band'] and
                prev_row['close'] >= prev_row['lower_band'] and
                row['rsi'] < 35 and
                volume_ok):
                df.iloc[i, df.columns.get_loc('signal')] = 1

            # Short signal: Price above upper band and showing reversal
            # RSI overbought adds confirmation
            elif (row['close'] > row['upper_band'] and
                  prev_row['close'] <= prev_row['upper_band'] and
                  row['rsi'] > 65 and
                  volume_ok):
                df.iloc[i, df.columns.get_loc('signal')] = -1

        return df


# Allow running as standalone for testing
if __name__ == "__main__":
    from ..data_loader import generate_sample_data

    print("Testing Mean Reversion Strategy")
    print("=" * 50)

    # Generate test data
    df = generate_sample_data(days=60, seed=42)

    # Create and run strategy
    strategy = MeanReversionStrategy()
    results = strategy.backtest(df)

    # Print results
    stats = strategy.get_statistics()
    print(f"\nStrategy: {stats['strategy']}")
    print(f"Total Trades: {stats['total_trades']}")
    print(f"Win Rate: {stats['win_rate']:.1f}%")
    print(f"Profit Factor: {stats['profit_factor']:.2f}")
    print(f"Consistency Score: {stats['consistency_score']:.1f}%")
    print(f"Max Drawdown: ${stats['max_drawdown']:.2f}")
