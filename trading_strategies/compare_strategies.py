#!/usr/bin/env python3
"""
Compare all trading strategies and generate performance report.
Run this to see which strategy works best for prop firm trading.
"""

import sys
import os

# Add src to path
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from src.data_loader import generate_sample_data
from src.strategies import (
    SessionBreakoutStrategy,
    MeanReversionStrategy,
    EMAPullbackStrategy
)
from src.analytics import (
    compare_strategies,
    format_comparison_table,
    calculate_prop_firm_metrics,
    generate_scaling_projection,
    format_scaling_projection
)


def main():
    print("\n" + "=" * 70)
    print("  PROP FIRM TRADING STRATEGY COMPARISON")
    print("  Optimized for Consistency & Scalability")
    print("=" * 70 + "\n")

    # Generate sample data (1 year of 5-minute data)
    print("Generating sample market data (1 year, 5-min candles)...")
    df = generate_sample_data(
        symbol="ES",
        days=252,
        timeframe_minutes=5,
        base_price=4500.0,
        volatility=0.0012,
        seed=42
    )
    print(f"Generated {len(df):,} candles\n")

    # Initialize strategies
    strategies = [
        SessionBreakoutStrategy(
            range_minutes=30,
            stop_loss_pct=0.3,
            take_profit_pct=0.6
        ),
        MeanReversionStrategy(
            ema_period=20,
            entry_std=2.0,
            stop_loss_pct=0.25,
            take_profit_pct=0.35
        ),
        EMAPullbackStrategy(
            fast_ema=9,
            slow_ema=21,
            trend_ema=50,
            stop_loss_pct=0.35,
            take_profit_pct=0.5
        )
    ]

    # Run backtests
    results = []
    all_stats = []

    print("Running backtests...")
    print("-" * 70)

    for strategy in strategies:
        print(f"  Testing: {strategy.config.name}...", end=" ")

        # Run backtest
        df_result = strategy.backtest(df.copy())

        # Get statistics
        stats = strategy.get_statistics()
        results.append(stats)

        # Calculate prop firm specific metrics
        pf_metrics = calculate_prop_firm_metrics(strategy.trades)

        all_stats.append({
            'strategy': strategy,
            'stats': stats,
            'pf_metrics': pf_metrics
        })

        print(f"Done ({stats['total_trades']} trades)")

    print("-" * 70 + "\n")

    # Display comparison table
    comparison_df = compare_strategies(results)
    print(format_comparison_table(comparison_df))

    # Detailed prop firm metrics
    print("\n" + "=" * 70)
    print("DETAILED PROP FIRM ANALYSIS")
    print("=" * 70)

    for item in all_stats:
        stats = item['stats']
        pf = item['pf_metrics']
        name = stats['strategy']

        print(f"\n{name}")
        print("-" * 40)
        print(f"  Max Daily Drawdown:     {pf.max_daily_drawdown:>8.2f}%")
        print(f"  Max Total Drawdown:     {pf.max_total_drawdown:>8.2f}%")
        print(f"  Daily DD Violations:    {pf.daily_drawdown_violations:>8}")
        print(f"  Sharpe Ratio:           {pf.sharpe_ratio:>8.2f}")
        print(f"  Calmar Ratio:           {pf.calmar_ratio:>8.2f}")
        print(f"  Recovery Factor:        {pf.recovery_factor:>8.2f}")

    # Scaling projections
    print("\n" + "=" * 70)
    print("SCALING PROJECTIONS (25 Accounts @ $100k each)")
    print("=" * 70)

    for item in all_stats:
        stats = item['stats']
        projection = generate_scaling_projection(
            stats,
            num_accounts=25,
            account_size=100000,
            payout_rate=0.8
        )
        print(format_scaling_projection(projection, stats['strategy']))
        print()

    # Recommendation
    print("=" * 70)
    print("RECOMMENDATION FOR YOUR GOALS")
    print("=" * 70)

    # Find best strategy by consistency
    best_consistency = max(all_stats, key=lambda x: x['stats']['consistency_score'])
    best_pf = max(all_stats, key=lambda x: x['stats']['profit_factor'])
    lowest_dd = min(all_stats, key=lambda x: x['pf_metrics'].max_total_drawdown)

    print(f"""
    For scaling across multiple prop firm accounts, consider:

    1. MOST CONSISTENT: {best_consistency['stats']['strategy']}
       - {best_consistency['stats']['consistency_score']:.1f}% profitable days
       - Best for maintaining multiple accounts without violations

    2. BEST RISK/REWARD: {best_pf['stats']['strategy']}
       - Profit Factor: {best_pf['stats']['profit_factor']:.2f}
       - Optimal balance of wins vs losses

    3. LOWEST RISK: {lowest_dd['stats']['strategy']}
       - Max Drawdown: {lowest_dd['pf_metrics'].max_total_drawdown:.2f}%
       - Safest for strict prop firm rules

    IMPORTANT: These results are from simulated data. Always:
    - Forward test on demo accounts first
    - Start with 1-2 accounts before scaling
    - Monitor performance and adjust parameters
    - Understand that past results don't guarantee future performance
    """)


if __name__ == "__main__":
    main()
