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
    /// EMA Pullback Strategy V2 - FIXED VERSION
    /// More aggressive entry detection for trending days.
    /// Will catch multiple entries on strong trend days like 500pt moves.
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
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"EMA Pullback V2 - Fixed for real trading. Catches multiple entries on trend days.";
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
                ADXThreshold = 25;          // Only trade when ADX > 25 (confirms real trend)

                // Pullback settings
                PullbackToEMAPercent = 0.15; // Price within 0.15% of slow EMA counts as touching

                // Risk Management - adjusted for MNQ
                Contracts = 1;              // Number of contracts to trade
                StopLossTicks = 40;         // ~10 points on MNQ
                TakeProfitTicks = 60;       // ~15 points on MNQ
                MaxTradesPerDay = 5;        // Allow more trades on trend days
                CooldownBars = 6;           // Wait 6 bars (30 min on 5-min chart) after each trade

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

            // Reset on new day
            if (Time[0].Date != lastTradeDate.Date)
            {
                tradesToday = 0;
                lastTradeDate = Time[0].Date;
                lastTradeBar = 0;
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
            // COOLDOWN CHECK - Don't re-enter too quickly after a trade
            // ===================
            if (CurrentBar - lastTradeBar < CooldownBars)
                return;

            // ===================
            // ENTRY LOGIC - SHORTS (Downtrend)
            // Price must be BELOW slow EMA, pull UP to touch it, then reject
            // ===================
            if (strongDowntrend)
            {
                // Price must close BELOW the slow EMA (we're in a downtrend, price should be under)
                bool priceBelowSlowEMA = Close[0] < emaSlow[0];

                // The HIGH of current or recent bar touched/exceeded the slow EMA (the pullback UP)
                bool pulledUpToEMA = High[0] >= emaSlow[0] * (1 - PullbackToEMAPercent / 100) ||
                                     High[1] >= emaSlow[1] * (1 - PullbackToEMAPercent / 100);

                // Current bar is rejecting (red candle, closing near lows)
                bool rejecting = Close[0] < Open[0] && Close[0] < (High[0] + Low[0]) / 2;

                if (priceBelowSlowEMA && pulledUpToEMA && rejecting)
                {
                    EnterShort(Contracts, "PBShort");
                    tradesToday++;
                    lastTradeBar = CurrentBar;

                    Print($"{Time[0]} | SHORT @ {Close[0]:F2} | Pullback rejection | Trade #{tradesToday}");
                }
            }

            // ===================
            // ENTRY LOGIC - LONGS (Uptrend)
            // Price must be ABOVE slow EMA, pull DOWN to touch it, then bounce
            // ===================
            if (strongUptrend)
            {
                // Price must close ABOVE the slow EMA (we're in an uptrend, price should be over)
                bool priceAboveSlowEMA = Close[0] > emaSlow[0];

                // The LOW of current or recent bar touched/dipped to the slow EMA (the pullback DOWN)
                bool pulledDownToEMA = Low[0] <= emaSlow[0] * (1 + PullbackToEMAPercent / 100) ||
                                       Low[1] <= emaSlow[1] * (1 + PullbackToEMAPercent / 100);

                // Current bar is bouncing (green candle, closing near highs)
                bool bouncing = Close[0] > Open[0] && Close[0] > (High[0] + Low[0]) / 2;

                if (priceAboveSlowEMA && pulledDownToEMA && bouncing)
                {
                    EnterLong(Contracts, "PBLong");
                    tradesToday++;
                    lastTradeBar = CurrentBar;

                    Print($"{Time[0]} | LONG @ {Close[0]:F2} | Pullback bounce | Trade #{tradesToday}");
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
