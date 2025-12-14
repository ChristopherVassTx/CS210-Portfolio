"""
Data loader and sample data generator for backtesting.
Generates realistic OHLCV data for testing strategies.
"""

import pandas as pd
import numpy as np
from datetime import datetime, timedelta


def generate_sample_data(
    symbol: str = "ES",
    days: int = 252,  # 1 trading year
    timeframe_minutes: int = 5,
    base_price: float = 4500.0,
    volatility: float = 0.001,
    seed: int = 42
) -> pd.DataFrame:
    """
    Generate realistic OHLCV data for backtesting.

    Args:
        symbol: Instrument symbol
        days: Number of trading days
        timeframe_minutes: Candle timeframe in minutes
        base_price: Starting price
        volatility: Price volatility (as decimal)
        seed: Random seed for reproducibility

    Returns:
        DataFrame with columns: datetime, open, high, low, close, volume
    """
    np.random.seed(seed)

    # Trading hours: 9:30 AM to 4:00 PM (6.5 hours = 390 minutes)
    candles_per_day = 390 // timeframe_minutes
    total_candles = days * candles_per_day

    # Generate price movement using geometric Brownian motion
    returns = np.random.normal(0, volatility, total_candles)

    # Add some trending behavior
    trend = np.sin(np.linspace(0, 4 * np.pi, total_candles)) * volatility * 0.5
    returns = returns + trend

    # Generate prices
    prices = base_price * np.cumprod(1 + returns)

    # Generate OHLCV data
    data = []
    start_date = datetime(2024, 1, 2, 9, 30)  # Start of year

    candle_idx = 0
    current_date = start_date

    for day in range(days):
        # Skip weekends
        while current_date.weekday() >= 5:
            current_date += timedelta(days=1)

        day_start = current_date.replace(hour=9, minute=30)

        for candle in range(candles_per_day):
            if candle_idx >= total_candles:
                break

            candle_time = day_start + timedelta(minutes=candle * timeframe_minutes)

            # Generate OHLC from close price
            close = prices[candle_idx]

            # Add intra-candle volatility
            candle_range = close * volatility * np.random.uniform(0.5, 2.0)

            # Randomly decide if bullish or bearish candle
            if np.random.random() > 0.5:
                open_price = close - np.random.uniform(0, candle_range * 0.7)
                high = close + np.random.uniform(0, candle_range * 0.3)
                low = open_price - np.random.uniform(0, candle_range * 0.3)
            else:
                open_price = close + np.random.uniform(0, candle_range * 0.7)
                high = open_price + np.random.uniform(0, candle_range * 0.3)
                low = close - np.random.uniform(0, candle_range * 0.3)

            # Volume varies by time of day (higher at open/close)
            hour = candle_time.hour
            if hour == 9 or hour == 15:
                vol_multiplier = 2.0
            elif hour == 10 or hour == 14:
                vol_multiplier = 1.5
            else:
                vol_multiplier = 1.0

            volume = int(np.random.uniform(1000, 5000) * vol_multiplier)

            data.append({
                'datetime': candle_time,
                'open': round(open_price, 2),
                'high': round(high, 2),
                'low': round(low, 2),
                'close': round(close, 2),
                'volume': volume
            })

            candle_idx += 1

        current_date += timedelta(days=1)

    df = pd.DataFrame(data)
    df.set_index('datetime', inplace=True)

    return df


def add_session_markers(df: pd.DataFrame) -> pd.DataFrame:
    """
    Add session markers for different trading sessions.
    Useful for session-based strategies.
    """
    df = df.copy()

    # Define sessions (in local time, adjust as needed)
    df['hour'] = df.index.hour
    df['minute'] = df.index.minute

    # Session classification
    conditions = [
        (df['hour'] < 9) | ((df['hour'] == 9) & (df['minute'] < 30)),  # Pre-market
        (df['hour'] >= 9) & (df['hour'] < 12),  # Morning session
        (df['hour'] >= 12) & (df['hour'] < 14),  # Lunch session
        (df['hour'] >= 14) & (df['hour'] < 16),  # Afternoon session
    ]
    choices = ['pre_market', 'morning', 'lunch', 'afternoon']
    df['session'] = np.select(conditions, choices, default='after_hours')

    # Mark session opens
    df['is_session_open'] = df['hour'].diff() != 0

    # Drop helper columns
    df.drop(['hour', 'minute'], axis=1, inplace=True)

    return df


if __name__ == "__main__":
    # Test data generation
    df = generate_sample_data(days=30)
    print(f"Generated {len(df)} candles")
    print(df.head(10))
    print(f"\nPrice range: ${df['low'].min():.2f} - ${df['high'].max():.2f}")
