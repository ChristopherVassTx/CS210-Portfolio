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
    /// EMA Pullback Strategy
    /// Trend following strategy that enters on pullbacks to EMA in trending markets.
    /// Optimized for prop firm consistency with automated London/NY session trading.
    /// </summary>
    public class EMAPullback : Strategy
    {
        #region Variables
        private EMA emaFast;
        private EMA emaSlow;
        private EMA emaTrend;
        private int tradesToday = 0;
        private DateTime lastTradeDate = DateTime.MinValue;
        private bool inUptrendPullback = false;
        private bool inDowntrendPullback = false;
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"EMA Pullback - Trend following strategy with pullback entries. Optimized for prop firm trading.";
                Name = "EMAPullback";
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

                // Default parameters
                FastEMAPeriod = 9;
                SlowEMAPeriod = 21;
                TrendEMAPeriod = 50;
                PullbackThresholdPct = 0.10;
                MinTrendStrengthPct = 0.15;

                // Risk Management
                StopLossTicks = 24;
                TakeProfitTicks = 36;
                MaxTradesPerDay = 3;

                // Session settings (Eastern Time)
                TradeLondon = true;
                TradeNewYork = true;
                LondonStartHour = 3;
                LondonEndHour = 11;
                NYStartHour = 9;
                NYStartMinute = 45;
                NYEndHour = 15;
                NYEndMinute = 15;
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

                AddChartIndicator(emaFast);
                AddChartIndicator(emaSlow);
                AddChartIndicator(emaTrend);

                // Set colors for visual distinction
                emaFast.Plots[0].Brush = Brushes.Blue;
                emaSlow.Plots[0].Brush = Brushes.Orange;
                emaTrend.Plots[0].Brush = Brushes.Purple;
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
                inUptrendPullback = false;
                inDowntrendPullback = false;
                lastTradeDate = Time[0].Date;
            }

            // Check if we're in a valid trading session
            if (!IsInTradingSession())
                return;

            // Determine trend
            bool uptrend = emaFast[0] > emaSlow[0] && emaSlow[0] > emaTrend[0];
            bool downtrend = emaFast[0] < emaSlow[0] && emaSlow[0] < emaTrend[0];

            // Calculate trend strength (EMA separation)
            double emaSeparation = Math.Abs(emaFast[0] - emaSlow[0]) / emaSlow[0] * 100;
            bool strongTrend = emaSeparation >= MinTrendStrengthPct;

            // Calculate distance from fast EMA
            double distanceFromEMA = Math.Abs(Close[0] - emaFast[0]) / emaFast[0] * 100;

            // Track pullback state
            if (uptrend && strongTrend)
            {
                // Detect start of pullback (price moves toward EMA)
                if (Close[1] > emaFast[1] && Close[0] <= emaFast[0] * (1 + PullbackThresholdPct / 100))
                {
                    inUptrendPullback = true;
                }
            }
            else
            {
                inUptrendPullback = false;
            }

            if (downtrend && strongTrend)
            {
                // Detect start of pullback (price moves toward EMA)
                if (Close[1] < emaFast[1] && Close[0] >= emaFast[0] * (1 - PullbackThresholdPct / 100))
                {
                    inDowntrendPullback = true;
                }
            }
            else
            {
                inDowntrendPullback = false;
            }

            // Skip if max trades hit or already in position
            if (tradesToday >= MaxTradesPerDay)
                return;

            if (Position.MarketPosition != MarketPosition.Flat)
                return;

            // LONG: Uptrend + pullback completed (price bounced off EMA)
            bool longCondition = uptrend && strongTrend && inUptrendPullback &&
                                Close[0] > emaFast[0] &&
                                Close[1] <= emaFast[1] &&
                                Close[0] > Close[1];

            // SHORT: Downtrend + pullback completed (price rejected from EMA)
            bool shortCondition = downtrend && strongTrend && inDowntrendPullback &&
                                 Close[0] < emaFast[0] &&
                                 Close[1] >= emaFast[1] &&
                                 Close[0] < Close[1];

            if (longCondition)
            {
                EnterLong("PullbackLong");
                tradesToday++;
                inUptrendPullback = false;
                Print($"LONG Entry @ {Close[0]} - EMA Pullback in uptrend (Strength: {emaSeparation:F2}%)");
            }
            else if (shortCondition)
            {
                EnterShort("PullbackShort");
                tradesToday++;
                inDowntrendPullback = false;
                Print($"SHORT Entry @ {Close[0]} - EMA Pullback in downtrend (Strength: {emaSeparation:F2}%)");
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
        [Display(Name = "Fast EMA Period", Order = 1, GroupName = "Strategy Parameters")]
        public int FastEMAPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(10, 50)]
        [Display(Name = "Slow EMA Period", Order = 2, GroupName = "Strategy Parameters")]
        public int SlowEMAPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(30, 200)]
        [Display(Name = "Trend EMA Period", Order = 3, GroupName = "Strategy Parameters")]
        public int TrendEMAPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.05, 0.50)]
        [Display(Name = "Pullback Threshold %", Order = 4, GroupName = "Strategy Parameters")]
        public double PullbackThresholdPct { get; set; }

        [NinjaScriptProperty]
        [Range(0.05, 0.50)]
        [Display(Name = "Min Trend Strength %", Order = 5, GroupName = "Strategy Parameters")]
        public double MinTrendStrengthPct { get; set; }

        [NinjaScriptProperty]
        [Range(5, 100)]
        [Display(Name = "Stop Loss (Ticks)", Order = 1, GroupName = "Risk Management")]
        public int StopLossTicks { get; set; }

        [NinjaScriptProperty]
        [Range(5, 200)]
        [Display(Name = "Take Profit (Ticks)", Order = 2, GroupName = "Risk Management")]
        public int TakeProfitTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Max Trades Per Day", Order = 3, GroupName = "Risk Management")]
        public int MaxTradesPerDay { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Trade London Session", Order = 1, GroupName = "Session Settings")]
        public bool TradeLondon { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Trade New York Session", Order = 2, GroupName = "Session Settings")]
        public bool TradeNewYork { get; set; }

        [NinjaScriptProperty]
        [Range(0, 23)]
        [Display(Name = "London Start Hour (ET)", Order = 3, GroupName = "Session Settings")]
        public int LondonStartHour { get; set; }

        [NinjaScriptProperty]
        [Range(0, 23)]
        [Display(Name = "London End Hour (ET)", Order = 4, GroupName = "Session Settings")]
        public int LondonEndHour { get; set; }

        [NinjaScriptProperty]
        [Range(0, 23)]
        [Display(Name = "NY Start Hour (ET)", Order = 5, GroupName = "Session Settings")]
        public int NYStartHour { get; set; }

        [NinjaScriptProperty]
        [Range(0, 59)]
        [Display(Name = "NY Start Minute (ET)", Order = 6, GroupName = "Session Settings")]
        public int NYStartMinute { get; set; }

        [NinjaScriptProperty]
        [Range(0, 23)]
        [Display(Name = "NY End Hour (ET)", Order = 7, GroupName = "Session Settings")]
        public int NYEndHour { get; set; }

        [NinjaScriptProperty]
        [Range(0, 59)]
        [Display(Name = "NY End Minute (ET)", Order = 8, GroupName = "Session Settings")]
        public int NYEndMinute { get; set; }
        #endregion
    }
}
