#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    /// <summary>
    /// EMA Pullback Strategy V2 - With Drawdown Protection
    /// - 4PM Hard Kill Switch
    /// - Daily Loss Limit
    /// - Proper pullback entries only
    /// </summary>
    public class EMAPullbackV2 : Strategy
    {
        #region Variables
        private EMA emaFast;
        private EMA emaSlow;
        private EMA emaTrend;
        private ADX adx;
        private int tradesToday = 0;
        private DateTime lastTradeDate = DateTime.MinValue;
        private int lastTradeBar = 0;
        private double dailyPnL = 0;
        private bool dailyLossLimitHit = false;
        private bool hardKillHit = false;
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"EMA Pullback V2 - With Drawdown Protection. 4PM kill switch + daily loss limit.";
                Name = "EMAPullbackV2";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IsFillLimitOnTouch = false;
                MaximumBarsLookBack = MaximumBarsLookBack.TwoHundredFiftySix;
                OrderFillResolution = OrderFillResolution.Standard;
                Slippage = 2;
                StartBehavior = StartBehavior.WaitUntilFlat;
                TimeInForce = TimeInForce.Day;
                TraceOrders = false;
                RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose;
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade = 60;
                IsInstantiatedOnEachOptimizationIteration = true;

                // EMA Settings
                FastEMAPeriod = 9;
                SlowEMAPeriod = 21;
                TrendEMAPeriod = 50;

                // Trend Filter - ADX
                ADXPeriod = 14;
                ADXThreshold = 25;

                // Pullback settings
                PullbackToEMAPercent = 0.15;

                // Risk Management - TIGHTENED
                Contracts = 1;
                StopLossTicks = 40;
                TakeProfitTicks = 60;
                MaxTradesPerDay = 2;        // REDUCED from 5 to 2
                CooldownBars = 6;

                // DRAWDOWN PROTECTION
                DailyLossLimit = 500;       // Stop trading after $500 daily loss
                HardKillHour = 16;          // 4 PM ET
                HardKillMinute = 0;

                // Session settings (Eastern Time)
                TradeLondon = true;
                TradeNewYork = true;
                LondonStartHour = 3;
                LondonEndHour = 11;
                NYStartHour = 9;
                NYStartMinute = 30;
                NYEndHour = 15;
                NYEndMinute = 45;
            }
            else if (State == State.Configure)
            {
                SetStopLoss(CalculationMode.Ticks, StopLossTicks);
                SetProfitTarget(CalculationMode.Ticks, TakeProfitTicks);
            }
            else if (State == State.DataLoaded)
            {
                emaFast = EMA(Close, FastEMAPeriod);
                emaSlow = EMA(Close, SlowEMAPeriod);
                emaTrend = EMA(Close, TrendEMAPeriod);
                adx = ADX(Close, ADXPeriod);

                AddChartIndicator(emaFast);
                AddChartIndicator(emaSlow);
                AddChartIndicator(emaTrend);
                AddChartIndicator(adx);

                emaFast.Plots[0].Brush = Brushes.DodgerBlue;
                emaSlow.Plots[0].Brush = Brushes.Orange;
                emaTrend.Plots[0].Brush = Brushes.Magenta;
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < BarsRequiredToTrade)
                return;

            // ===================
            // 4PM HARD KILL SWITCH - Flatten everything and stop
            // ===================
            if (Time[0].Hour >= HardKillHour && Time[0].Minute >= HardKillMinute)
            {
                if (!hardKillHit && Position.MarketPosition != MarketPosition.Flat)
                {
                    if (Position.MarketPosition == MarketPosition.Long)
                        ExitLong("4PM Kill");
                    else if (Position.MarketPosition == MarketPosition.Short)
                        ExitShort("4PM Kill");

                    Print($"{Time[0]} | 4PM HARD KILL - Flattening all positions");
                }
                hardKillHit = true;
                return; // No more trading today
            }

            // Reset on new day
            if (Time[0].Date != lastTradeDate.Date)
            {
                tradesToday = 0;
                lastTradeDate = Time[0].Date;
                lastTradeBar = 0;
                dailyPnL = 0;
                dailyLossLimitHit = false;
                hardKillHit = false;
            }

            // ===================
            // DAILY LOSS LIMIT CHECK
            // ===================
            if (dailyLossLimitHit)
            {
                // Still need to manage open position
                if (Position.MarketPosition != MarketPosition.Flat)
                    return; // Let stop/target manage exit
                return; // No new trades
            }

            if (dailyPnL <= -DailyLossLimit)
            {
                if (!dailyLossLimitHit)
                {
                    Print($"{Time[0]} | DAILY LOSS LIMIT HIT - P&L: ${dailyPnL:F2} - No more trading today");
                    dailyLossLimitHit = true;
                }
                return;
            }

            // Check if we're in a valid trading session
            if (!IsInTradingSession())
                return;

            // Determine trend using EMA stack
            bool strongDowntrend = emaFast[0] < emaSlow[0] && emaSlow[0] < emaTrend[0];
            bool strongUptrend = emaFast[0] > emaSlow[0] && emaSlow[0] > emaTrend[0];

            // Skip if no clear trend
            if (!strongDowntrend && !strongUptrend)
                return;

            // ADX FILTER - Skip if market is choppy (ADX below threshold)
            if (adx[0] < ADXThreshold)
                return;

            // Skip if max trades hit or already in position
            if (tradesToday >= MaxTradesPerDay)
                return;

            if (Position.MarketPosition != MarketPosition.Flat)
                return;

            // ===================
            // COOLDOWN CHECK
            // ===================
            if (CurrentBar - lastTradeBar < CooldownBars)
                return;

            // ===================
            // ENTRY LOGIC - SHORTS (Downtrend)
            // ===================
            if (strongDowntrend)
            {
                bool priceBelowSlowEMA = Close[0] < emaSlow[0];
                bool pulledUpToEMA = High[0] >= emaSlow[0] * (1 - PullbackToEMAPercent / 100) ||
                                     High[1] >= emaSlow[1] * (1 - PullbackToEMAPercent / 100);
                bool rejecting = Close[0] < Open[0] && Close[0] < (High[0] + Low[0]) / 2;

                if (priceBelowSlowEMA && pulledUpToEMA && rejecting)
                {
                    EnterShort(Contracts, "PBShort");
                    tradesToday++;
                    lastTradeBar = CurrentBar;
                    Print($"{Time[0]} | SHORT @ {Close[0]:F2} | Trade #{tradesToday} | Daily P&L: ${dailyPnL:F2}");
                }
            }

            // ===================
            // ENTRY LOGIC - LONGS (Uptrend)
            // ===================
            if (strongUptrend)
            {
                bool priceAboveSlowEMA = Close[0] > emaSlow[0];
                bool pulledDownToEMA = Low[0] <= emaSlow[0] * (1 + PullbackToEMAPercent / 100) ||
                                       Low[1] <= emaSlow[1] * (1 + PullbackToEMAPercent / 100);
                bool bouncing = Close[0] > Open[0] && Close[0] > (High[0] + Low[0]) / 2;

                if (priceAboveSlowEMA && pulledDownToEMA && bouncing)
                {
                    EnterLong(Contracts, "PBLong");
                    tradesToday++;
                    lastTradeBar = CurrentBar;
                    Print($"{Time[0]} | LONG @ {Close[0]:F2} | Trade #{tradesToday} | Daily P&L: ${dailyPnL:F2}");
                }
            }
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
        {
            // Track daily P&L from closed trades
            if (Position.MarketPosition == MarketPosition.Flat && SystemPerformance.AllTrades.Count > 0)
            {
                Trade lastTrade = SystemPerformance.AllTrades[SystemPerformance.AllTrades.Count - 1];
                if (lastTrade.Exit.Time.Date == Time[0].Date)
                {
                    // Recalculate daily P&L
                    dailyPnL = 0;
                    foreach (Trade trade in SystemPerformance.AllTrades)
                    {
                        if (trade.Exit.Time.Date == Time[0].Date)
                        {
                            dailyPnL += trade.ProfitCurrency;
                        }
                    }
                    Print($"{Time[0]} | Trade closed | Daily P&L: ${dailyPnL:F2}");
                }
            }
        }

        private bool IsInTradingSession()
        {
            int hour = Time[0].Hour;
            int minute = Time[0].Minute;
            double timeValue = hour + minute / 60.0;

            bool inLondon = TradeLondon &&
                           timeValue >= LondonStartHour &&
                           timeValue < LondonEndHour;

            bool inNY = TradeNewYork &&
                       timeValue >= (NYStartHour + NYStartMinute / 60.0) &&
                       timeValue < (NYEndHour + NYEndMinute / 60.0);

            return inLondon || inNY;
        }

        #region Properties
        [NinjaScriptProperty]
        [Range(3, 20)]
        [Display(Name = "Fast EMA Period", Order = 1, GroupName = "1. EMAs")]
        public int FastEMAPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(10, 50)]
        [Display(Name = "Slow EMA Period", Order = 2, GroupName = "1. EMAs")]
        public int SlowEMAPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(30, 200)]
        [Display(Name = "Trend EMA Period", Order = 3, GroupName = "1. EMAs")]
        public int TrendEMAPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(7, 30)]
        [Display(Name = "ADX Period", Order = 4, GroupName = "1. EMAs")]
        public int ADXPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(15, 40)]
        [Display(Name = "ADX Threshold", Description = "Minimum ADX to confirm trend (25 = standard)", Order = 5, GroupName = "1. EMAs")]
        public int ADXThreshold { get; set; }

        [NinjaScriptProperty]
        [Range(0.05, 0.5)]
        [Display(Name = "Pullback to EMA %", Description = "How close price wick must get to slow EMA to count as touch", Order = 1, GroupName = "2. Pullback Detection")]
        public double PullbackToEMAPercent { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Contracts", Description = "Number of contracts to trade", Order = 0, GroupName = "3. Risk Management")]
        public int Contracts { get; set; }

        [NinjaScriptProperty]
        [Range(10, 200)]
        [Display(Name = "Stop Loss (Ticks)", Order = 1, GroupName = "3. Risk Management")]
        public int StopLossTicks { get; set; }

        [NinjaScriptProperty]
        [Range(10, 300)]
        [Display(Name = "Take Profit (Ticks)", Order = 2, GroupName = "3. Risk Management")]
        public int TakeProfitTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Max Trades Per Day", Order = 3, GroupName = "3. Risk Management")]
        public int MaxTradesPerDay { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Cooldown Bars", Description = "Bars to wait after each trade before next entry", Order = 4, GroupName = "3. Risk Management")]
        public int CooldownBars { get; set; }

        [NinjaScriptProperty]
        [Range(100, 5000)]
        [Display(Name = "Daily Loss Limit ($)", Description = "Stop trading after this daily loss", Order = 5, GroupName = "3. Risk Management")]
        public double DailyLossLimit { get; set; }

        [NinjaScriptProperty]
        [Range(0, 23)]
        [Display(Name = "Hard Kill Hour (ET)", Description = "Hour to flatten and stop (e.g., 16 = 4PM)", Order = 6, GroupName = "3. Risk Management")]
        public int HardKillHour { get; set; }

        [NinjaScriptProperty]
        [Range(0, 59)]
        [Display(Name = "Hard Kill Minute (ET)", Order = 7, GroupName = "3. Risk Management")]
        public int HardKillMinute { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Trade London Session", Order = 1, GroupName = "4. Sessions")]
        public bool TradeLondon { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Trade New York Session", Order = 2, GroupName = "4. Sessions")]
        public bool TradeNewYork { get; set; }

        [NinjaScriptProperty]
        [Range(0, 23)]
        [Display(Name = "London Start Hour (ET)", Order = 3, GroupName = "4. Sessions")]
        public int LondonStartHour { get; set; }

        [NinjaScriptProperty]
        [Range(0, 23)]
        [Display(Name = "London End Hour (ET)", Order = 4, GroupName = "4. Sessions")]
        public int LondonEndHour { get; set; }

        [NinjaScriptProperty]
        [Range(0, 23)]
        [Display(Name = "NY Start Hour (ET)", Order = 5, GroupName = "4. Sessions")]
        public int NYStartHour { get; set; }

        [NinjaScriptProperty]
        [Range(0, 59)]
        [Display(Name = "NY Start Minute (ET)", Order = 6, GroupName = "4. Sessions")]
        public int NYStartMinute { get; set; }

        [NinjaScriptProperty]
        [Range(0, 23)]
        [Display(Name = "NY End Hour (ET)", Order = 7, GroupName = "4. Sessions")]
        public int NYEndHour { get; set; }

        [NinjaScriptProperty]
        [Range(0, 59)]
        [Display(Name = "NY End Minute (ET)", Order = 8, GroupName = "4. Sessions")]
        public int NYEndMinute { get; set; }
        #endregion
    }
}
