"""
Base Strategy class that all strategies inherit from.
Provides common functionality for backtesting and trade management.
"""

from abc import ABC, abstractmethod
from dataclasses import dataclass, field
from typing import List, Optional
import pandas as pd
import numpy as np


@dataclass
class Trade:
    """Represents a single trade."""
    entry_time: pd.Timestamp
    entry_price: float
    direction: str  # 'long' or 'short'
    size: float = 1.0
    stop_loss: Optional[float] = None
    take_profit: Optional[float] = None
    exit_time: Optional[pd.Timestamp] = None
    exit_price: Optional[float] = None
    exit_reason: Optional[str] = None  # 'stop_loss', 'take_profit', 'signal', 'end_of_day'

    @property
    def pnl(self) -> float:
        """Calculate profit/loss for the trade."""
        if self.exit_price is None:
            return 0.0

        if self.direction == 'long':
            return (self.exit_price - self.entry_price) * self.size
        else:
            return (self.entry_price - self.exit_price) * self.size

    @property
    def pnl_percent(self) -> float:
        """Calculate profit/loss as percentage."""
        if self.exit_price is None:
            return 0.0
        return (self.pnl / self.entry_price) * 100

    @property
    def is_winner(self) -> bool:
        """Check if trade was profitable."""
        return self.pnl > 0


@dataclass
class StrategyConfig:
    """Configuration for strategy parameters."""
    name: str
    # Risk management
    stop_loss_pct: float = 0.5  # 0.5% stop loss
    take_profit_pct: float = 1.0  # 1% take profit
    max_trades_per_day: int = 3
    risk_per_trade: float = 1.0  # % of account to risk
    # Session management
    trade_start_hour: int = 9
    trade_start_minute: int = 45
    trade_end_hour: int = 15
    trade_end_minute: int = 30
    close_at_session_end: bool = True


class BaseStrategy(ABC):
    """
    Abstract base class for all trading strategies.

    Designed for prop firm consistency:
    - Strict risk management
    - Daily drawdown limits
    - Session-based trading
    """

    def __init__(self, config: StrategyConfig):
        self.config = config
        self.trades: List[Trade] = []
        self.current_position: Optional[Trade] = None
        self.daily_pnl: float = 0.0
        self.total_pnl: float = 0.0

    @abstractmethod
    def generate_signals(self, df: pd.DataFrame) -> pd.DataFrame:
        """
        Generate trading signals based on strategy logic.

        Args:
            df: OHLCV DataFrame

        Returns:
            DataFrame with 'signal' column: 1 (long), -1 (short), 0 (no signal)
        """
        pass

    def can_trade(self, timestamp: pd.Timestamp) -> bool:
        """Check if we're within trading hours."""
        hour = timestamp.hour
        minute = timestamp.minute

        start_ok = (hour > self.config.trade_start_hour or
                   (hour == self.config.trade_start_hour and minute >= self.config.trade_start_minute))
        end_ok = (hour < self.config.trade_end_hour or
                 (hour == self.config.trade_end_hour and minute <= self.config.trade_end_minute))

        return start_ok and end_ok

    def should_close_position(self, timestamp: pd.Timestamp) -> bool:
        """Check if we should close position due to session end."""
        if not self.config.close_at_session_end:
            return False

        hour = timestamp.hour
        minute = timestamp.minute

        return (hour > self.config.trade_end_hour or
               (hour == self.config.trade_end_hour and minute >= self.config.trade_end_minute))

    def check_stop_loss(self, current_price: float) -> bool:
        """Check if stop loss is hit."""
        if self.current_position is None or self.current_position.stop_loss is None:
            return False

        if self.current_position.direction == 'long':
            return current_price <= self.current_position.stop_loss
        else:
            return current_price >= self.current_position.stop_loss

    def check_take_profit(self, current_price: float) -> bool:
        """Check if take profit is hit."""
        if self.current_position is None or self.current_position.take_profit is None:
            return False

        if self.current_position.direction == 'long':
            return current_price >= self.current_position.take_profit
        else:
            return current_price <= self.current_position.take_profit

    def open_position(self, timestamp: pd.Timestamp, price: float, direction: str):
        """Open a new position."""
        if direction == 'long':
            stop_loss = price * (1 - self.config.stop_loss_pct / 100)
            take_profit = price * (1 + self.config.take_profit_pct / 100)
        else:
            stop_loss = price * (1 + self.config.stop_loss_pct / 100)
            take_profit = price * (1 - self.config.take_profit_pct / 100)

        self.current_position = Trade(
            entry_time=timestamp,
            entry_price=price,
            direction=direction,
            stop_loss=stop_loss,
            take_profit=take_profit
        )

    def close_position(self, timestamp: pd.Timestamp, price: float, reason: str):
        """Close current position."""
        if self.current_position is None:
            return

        self.current_position.exit_time = timestamp
        self.current_position.exit_price = price
        self.current_position.exit_reason = reason

        self.trades.append(self.current_position)
        self.daily_pnl += self.current_position.pnl
        self.total_pnl += self.current_position.pnl

        self.current_position = None

    def backtest(self, df: pd.DataFrame) -> pd.DataFrame:
        """
        Run backtest on historical data.

        Args:
            df: OHLCV DataFrame

        Returns:
            DataFrame with signals and equity curve
        """
        # Reset state
        self.trades = []
        self.current_position = None
        self.daily_pnl = 0.0
        self.total_pnl = 0.0

        # Generate signals
        df = self.generate_signals(df.copy())

        # Track equity
        equity = [0.0]
        current_day = None
        trades_today = 0

        for i, (timestamp, row) in enumerate(df.iterrows()):
            # Reset daily counters
            if current_day != timestamp.date():
                current_day = timestamp.date()
                self.daily_pnl = 0.0
                trades_today = 0

            # Check for position management
            if self.current_position is not None:
                # Check stop loss (using low for longs, high for shorts)
                if self.current_position.direction == 'long':
                    if row['low'] <= self.current_position.stop_loss:
                        self.close_position(timestamp, self.current_position.stop_loss, 'stop_loss')
                else:
                    if row['high'] >= self.current_position.stop_loss:
                        self.close_position(timestamp, self.current_position.stop_loss, 'stop_loss')

                # Check take profit
                if self.current_position is not None:
                    if self.current_position.direction == 'long':
                        if row['high'] >= self.current_position.take_profit:
                            self.close_position(timestamp, self.current_position.take_profit, 'take_profit')
                    else:
                        if row['low'] <= self.current_position.take_profit:
                            self.close_position(timestamp, self.current_position.take_profit, 'take_profit')

                # Check session end
                if self.current_position is not None and self.should_close_position(timestamp):
                    self.close_position(timestamp, row['close'], 'end_of_day')

            # Check for new signals
            if (self.current_position is None and
                self.can_trade(timestamp) and
                trades_today < self.config.max_trades_per_day):

                signal = row.get('signal', 0)

                if signal == 1:  # Long signal
                    self.open_position(timestamp, row['close'], 'long')
                    trades_today += 1
                elif signal == -1:  # Short signal
                    self.open_position(timestamp, row['close'], 'short')
                    trades_today += 1

            equity.append(self.total_pnl)

        df['equity'] = equity[1:]

        return df

    def get_statistics(self) -> dict:
        """Calculate performance statistics."""
        if not self.trades:
            return {'error': 'No trades to analyze'}

        pnls = [t.pnl for t in self.trades]
        winners = [t for t in self.trades if t.is_winner]
        losers = [t for t in self.trades if not t.is_winner]

        # Calculate drawdown
        equity_curve = np.cumsum(pnls)
        running_max = np.maximum.accumulate(equity_curve)
        drawdown = running_max - equity_curve

        # Group by day for consistency metrics
        daily_pnl = {}
        for trade in self.trades:
            day = trade.entry_time.date()
            if day not in daily_pnl:
                daily_pnl[day] = 0
            daily_pnl[day] += trade.pnl

        daily_returns = list(daily_pnl.values())
        profitable_days = sum(1 for d in daily_returns if d > 0)

        # Calculate metrics
        total_pnl = sum(pnls)
        gross_profit = sum(t.pnl for t in winners) if winners else 0
        gross_loss = abs(sum(t.pnl for t in losers)) if losers else 0.001

        return {
            'strategy': self.config.name,
            'total_trades': len(self.trades),
            'winners': len(winners),
            'losers': len(losers),
            'win_rate': len(winners) / len(self.trades) * 100,
            'total_pnl': total_pnl,
            'average_trade': total_pnl / len(self.trades),
            'profit_factor': gross_profit / gross_loss if gross_loss > 0 else float('inf'),
            'max_drawdown': max(drawdown) if len(drawdown) > 0 else 0,
            'trading_days': len(daily_pnl),
            'profitable_days': profitable_days,
            'consistency_score': profitable_days / len(daily_pnl) * 100 if daily_pnl else 0,
            'avg_winner': gross_profit / len(winners) if winners else 0,
            'avg_loser': gross_loss / len(losers) if losers else 0,
        }
