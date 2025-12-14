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
    /// Session Range Breakout Strategy
    /// Builds a range during the first N minutes of a session, then trades breakouts.
    /// Optimized for prop firm consistency with automated London/NY session trading.
    /// </summary>
    public class SessionRangeBreakout : Strategy
    {
        #region Variables
        private double rangeHigh = 0;
        private double rangeLow = double.MaxValue;
        private bool rangeEstablished = false;
        private int tradesToday = 0;
        private DateTime lastTradeDate = DateTime.MinValue;
        private DateTime rangeStartTime;
        private DateTime rangeEndTime;
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Session Range Breakout - Trades breakouts from initial session range. Optimized for prop firm trading.";
                Name = "SessionRangeBreakout";
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
                BarsRequiredToTrade = 20;
                IsInstantiatedOnEachOptimizationIteration = true;

                // Default parameters
                RangeMinutes = 30;
                BreakoutBufferTicks = 4;
                StopLossTicks = 20;
                TakeProfitTicks = 40;
                MaxTradesPerDay = 2;

                // Session settings (Eastern Time)
                TradeLondon = true;
                TradeNewYork = true;
                LondonStartHour = 3;
                LondonEndHour = 11;
                NYStartHour = 9;
                NYStartMinute = 30;
                NYEndHour = 15;
                NYEndMinute = 30;
            }
            else if (State == State.Configure)
            {
                SetStopLoss(CalculationMode.Ticks, StopLossTicks);
                SetProfitTarget(CalculationMode.Ticks, TakeProfitTicks);
            }
            else if (State == State.DataLoaded)
            {
                ClearOutputWindow();
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < BarsRequiredToTrade)
                return;

            // Reset on new day
            if (Time[0].Date != lastTradeDate.Date)
            {
                ResetForNewDay();
                lastTradeDate = Time[0].Date;
            }

            // Check if we're in a valid trading session
            if (!IsInTradingSession())
                return;

            // Build or use the range
            if (!rangeEstablished)
            {
                BuildRange();
            }
            else
            {
                CheckForBreakout();
            }
        }

        private void ResetForNewDay()
        {
            rangeHigh = 0;
            rangeLow = double.MaxValue;
            rangeEstablished = false;
            tradesToday = 0;
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

        private bool IsInRangeBuildingPeriod()
        {
            // Determine which session we're in and calculate range period
            int hour = Time[0].Hour;
            int minute = Time[0].Minute;
            double timeValue = hour + minute / 60.0;

            DateTime sessionStart;

            if (TradeLondon && timeValue >= LondonStartHour && timeValue < LondonEndHour)
            {
                sessionStart = Time[0].Date.AddHours(LondonStartHour);
            }
            else if (TradeNewYork && timeValue >= (NYStartHour + NYStartMinute / 60.0))
            {
                sessionStart = Time[0].Date.AddHours(NYStartHour).AddMinutes(NYStartMinute);
            }
            else
            {
                return false;
            }

            return Time[0] >= sessionStart && Time[0] < sessionStart.AddMinutes(RangeMinutes);
        }

        private void BuildRange()
        {
            if (!IsInRangeBuildingPeriod())
            {
                // Range period is over, lock in the range
                if (rangeLow < double.MaxValue && rangeHigh > 0)
                {
                    rangeEstablished = true;
                    Print($"Range established: High={rangeHigh}, Low={rangeLow}, Range={rangeHigh - rangeLow} ticks");

                    // Draw range on chart
                    Draw.HorizontalLine(this, "RangeHigh", rangeHigh, Brushes.Green);
                    Draw.HorizontalLine(this, "RangeLow", rangeLow, Brushes.Red);
                }
                return;
            }

            // Build the range
            if (High[0] > rangeHigh)
                rangeHigh = High[0];
            if (Low[0] < rangeLow)
                rangeLow = Low[0];
        }

        private void CheckForBreakout()
        {
            if (Position.MarketPosition != MarketPosition.Flat)
                return;

            if (tradesToday >= MaxTradesPerDay)
                return;

            double breakoutHighLevel = rangeHigh + BreakoutBufferTicks * TickSize;
            double breakoutLowLevel = rangeLow - BreakoutBufferTicks * TickSize;

            // Long breakout
            if (Close[0] > breakoutHighLevel && Close[1] <= breakoutHighLevel)
            {
                EnterLong("LongBreakout");
                tradesToday++;
                Print($"LONG Entry @ {Close[0]} - Range breakout to upside");
            }
            // Short breakout
            else if (Close[0] < breakoutLowLevel && Close[1] >= breakoutLowLevel)
            {
                EnterShort("ShortBreakout");
                tradesToday++;
                Print($"SHORT Entry @ {Close[0]} - Range breakout to downside");
            }
        }

        #region Properties
        [NinjaScriptProperty]
        [Range(10, 120)]
        [Display(Name = "Range Building Minutes", Order = 1, GroupName = "Strategy Parameters")]
        public int RangeMinutes { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Breakout Buffer (Ticks)", Order = 2, GroupName = "Strategy Parameters")]
        public int BreakoutBufferTicks { get; set; }

        [NinjaScriptProperty]
        [Range(5, 100)]
        [Display(Name = "Stop Loss (Ticks)", Order = 3, GroupName = "Risk Management")]
        public int StopLossTicks { get; set; }

        [NinjaScriptProperty]
        [Range(5, 200)]
        [Display(Name = "Take Profit (Ticks)", Order = 4, GroupName = "Risk Management")]
        public int TakeProfitTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Max Trades Per Day", Order = 5, GroupName = "Risk Management")]
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
