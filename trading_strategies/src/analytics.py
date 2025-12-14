"""
Analytics module for trading strategy performance analysis.
Focused on metrics important for prop firm trading.
"""

import pandas as pd
import numpy as np
from typing import List, Dict
from dataclasses import dataclass


@dataclass
class PropFirmMetrics:
    """Metrics specifically important for prop firm evaluation."""
    max_daily_drawdown: float
    max_total_drawdown: float
    daily_drawdown_violations: int  # Days exceeding typical 2-3% limit
    total_drawdown_violations: int  # Times exceeding typical 5-6% limit
    consistency_score: float  # % of profitable days
    avg_daily_pnl: float
    daily_pnl_std: float
    sharpe_ratio: float
    calmar_ratio: float
    recovery_factor: float


def calculate_drawdown_series(equity_curve: np.ndarray) -> np.ndarray:
    """Calculate drawdown at each point in the equity curve."""
    running_max = np.maximum.accumulate(equity_curve)
    drawdown = running_max - equity_curve
    return drawdown


def calculate_prop_firm_metrics(
    trades: List,
    initial_balance: float = 100000,
    daily_dd_limit: float = 2.0,  # 2% typical prop firm limit
    total_dd_limit: float = 5.0   # 5% typical prop firm limit
) -> PropFirmMetrics:
    """
    Calculate metrics specifically relevant for prop firm trading.

    Args:
        trades: List of Trade objects from backtest
        initial_balance: Starting account balance
        daily_dd_limit: Daily drawdown limit as percentage
        total_dd_limit: Total drawdown limit as percentage

    Returns:
        PropFirmMetrics dataclass with all metrics
    """
    if not trades:
        return PropFirmMetrics(
            max_daily_drawdown=0, max_total_drawdown=0,
            daily_drawdown_violations=0, total_drawdown_violations=0,
            consistency_score=0, avg_daily_pnl=0, daily_pnl_std=0,
            sharpe_ratio=0, calmar_ratio=0, recovery_factor=0
        )

    # Group trades by day
    daily_pnl = {}
    for trade in trades:
        day = trade.entry_time.date()
        if day not in daily_pnl:
            daily_pnl[day] = 0
        daily_pnl[day] += trade.pnl

    daily_returns = list(daily_pnl.values())
    days = list(daily_pnl.keys())

    # Calculate equity curve
    cumulative_pnl = np.cumsum([0] + daily_returns)
    equity = initial_balance + cumulative_pnl

    # Calculate daily drawdowns
    daily_dd = []
    daily_high = initial_balance
    for i, pnl in enumerate(daily_returns):
        daily_balance = initial_balance + sum(daily_returns[:i+1])
        if pnl < 0:
            dd_pct = abs(pnl) / daily_high * 100
            daily_dd.append(dd_pct)
        else:
            daily_dd.append(0)
        daily_high = max(daily_high, daily_balance)

    # Calculate total drawdown
    drawdown = calculate_drawdown_series(np.array(equity))
    drawdown_pct = drawdown / np.maximum.accumulate(equity) * 100

    # Count violations
    daily_violations = sum(1 for dd in daily_dd if dd > daily_dd_limit)
    total_violations = sum(1 for dd in drawdown_pct if dd > total_dd_limit)

    # Consistency metrics
    profitable_days = sum(1 for pnl in daily_returns if pnl > 0)
    consistency = profitable_days / len(daily_returns) * 100 if daily_returns else 0

    # Risk-adjusted returns
    avg_daily = np.mean(daily_returns)
    std_daily = np.std(daily_returns) if len(daily_returns) > 1 else 0.001

    # Sharpe Ratio (annualized, assuming 252 trading days)
    sharpe = (avg_daily / std_daily) * np.sqrt(252) if std_daily > 0 else 0

    # Calmar Ratio (annualized return / max drawdown)
    total_return = sum(daily_returns)
    annualized_return = (total_return / len(daily_returns)) * 252
    max_dd = max(drawdown) if len(drawdown) > 0 else 0.001
    calmar = annualized_return / max_dd if max_dd > 0 else 0

    # Recovery Factor (total profit / max drawdown)
    recovery = total_return / max_dd if max_dd > 0 else 0

    return PropFirmMetrics(
        max_daily_drawdown=max(daily_dd) if daily_dd else 0,
        max_total_drawdown=max(drawdown_pct) if len(drawdown_pct) > 0 else 0,
        daily_drawdown_violations=daily_violations,
        total_drawdown_violations=total_violations,
        consistency_score=consistency,
        avg_daily_pnl=avg_daily,
        daily_pnl_std=std_daily,
        sharpe_ratio=sharpe,
        calmar_ratio=calmar,
        recovery_factor=recovery
    )


def compare_strategies(results: List[Dict]) -> pd.DataFrame:
    """
    Create a comparison table of multiple strategies.

    Args:
        results: List of strategy statistics dictionaries

    Returns:
        DataFrame comparing all strategies
    """
    comparison = pd.DataFrame(results)

    # Reorder columns for readability
    column_order = [
        'strategy', 'total_trades', 'win_rate', 'profit_factor',
        'total_pnl', 'average_trade', 'consistency_score',
        'max_drawdown', 'avg_winner', 'avg_loser'
    ]

    available_columns = [c for c in column_order if c in comparison.columns]
    comparison = comparison[available_columns]

    return comparison


def format_comparison_table(df: pd.DataFrame) -> str:
    """Format comparison DataFrame as a nice string table."""
    # Create formatted strings
    lines = []
    lines.append("=" * 90)
    lines.append("STRATEGY COMPARISON - PROP FIRM METRICS")
    lines.append("=" * 90)

    # Header
    header = f"{'Strategy':<25} {'Trades':>8} {'Win%':>8} {'PF':>8} {'Consist%':>10} {'MaxDD':>10}"
    lines.append(header)
    lines.append("-" * 90)

    # Data rows
    for _, row in df.iterrows():
        line = f"{row['strategy']:<25} {row['total_trades']:>8} {row['win_rate']:>7.1f}% {row['profit_factor']:>8.2f} {row['consistency_score']:>9.1f}% ${row['max_drawdown']:>9.2f}"
        lines.append(line)

    lines.append("=" * 90)

    # Legend
    lines.append("\nKey Metrics for Prop Firms:")
    lines.append("  - Win%: Higher is better for psychology (target: >50%)")
    lines.append("  - PF (Profit Factor): Gross profit / Gross loss (target: >1.5)")
    lines.append("  - Consist%: Percentage of profitable trading days (target: >55%)")
    lines.append("  - MaxDD: Maximum drawdown (keep under firm limits)")

    return "\n".join(lines)


def generate_scaling_projection(
    stats: Dict,
    num_accounts: int = 25,
    account_size: float = 100000,
    payout_rate: float = 0.8  # 80% payout typical for prop firms
) -> Dict:
    """
    Project potential earnings when scaling across multiple accounts.

    Args:
        stats: Strategy statistics dictionary
        num_accounts: Number of prop firm accounts
        account_size: Size per account
        payout_rate: Percentage of profits you keep

    Returns:
        Dictionary with scaling projections
    """
    # Assuming stats are from a period (adjust as needed)
    avg_trade = stats.get('average_trade', 0)
    trades_per_day = stats.get('total_trades', 0) / max(stats.get('trading_days', 1), 1)
    consistency = stats.get('consistency_score', 0) / 100

    # Daily projections per account
    daily_trades = trades_per_day
    daily_profit = avg_trade * daily_trades

    # Monthly projections (22 trading days)
    monthly_profit_per_account = daily_profit * 22 * consistency
    monthly_total = monthly_profit_per_account * num_accounts
    monthly_payout = monthly_total * payout_rate

    # Annual projections
    annual_total = monthly_total * 12
    annual_payout = monthly_payout * 12

    return {
        'num_accounts': num_accounts,
        'account_size': account_size,
        'daily_profit_per_account': daily_profit,
        'monthly_profit_per_account': monthly_profit_per_account,
        'monthly_total_gross': monthly_total,
        'monthly_payout': monthly_payout,
        'annual_gross': annual_total,
        'annual_payout': annual_payout,
        'effective_daily_per_account': monthly_payout / (22 * num_accounts)
    }


def format_scaling_projection(projection: Dict, strategy_name: str) -> str:
    """Format scaling projection as readable string."""
    lines = []
    lines.append("=" * 60)
    lines.append(f"SCALING PROJECTION: {strategy_name}")
    lines.append("=" * 60)
    lines.append(f"Accounts: {projection['num_accounts']}")
    lines.append(f"Account Size: ${projection['account_size']:,.0f}")
    lines.append("-" * 60)
    lines.append(f"Daily Profit/Account:    ${projection['daily_profit_per_account']:>12,.2f}")
    lines.append(f"Monthly Profit/Account:  ${projection['monthly_profit_per_account']:>12,.2f}")
    lines.append("-" * 60)
    lines.append(f"Monthly Total (Gross):   ${projection['monthly_total_gross']:>12,.2f}")
    lines.append(f"Monthly Payout (80%):    ${projection['monthly_payout']:>12,.2f}")
    lines.append("-" * 60)
    lines.append(f"Annual Gross:            ${projection['annual_gross']:>12,.2f}")
    lines.append(f"Annual Payout:           ${projection['annual_payout']:>12,.2f}")
    lines.append("=" * 60)
    lines.append("\n* Projections assume consistent execution across all accounts")
    lines.append("* Actual results may vary based on market conditions")

    return "\n".join(lines)
