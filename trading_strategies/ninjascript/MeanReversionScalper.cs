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
    /// Mean Reversion Scalper Strategy
    /// Trades price deviations from EMA with RSI confirmation.
    /// High win rate strategy optimized for prop firm consistency.
    /// Automated for London and NY session trading.
    /// </summary>
    public class MeanReversionScalper : Strategy
    {
        #region Variables
        private EMA ema;
        private RSI rsi;
        private StdDev stdDev;
        private int tradesToday = 0;
        private DateTime lastTradeDate = DateTime.MinValue;
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Mean Reversion Scalper - High win rate scalping strategy using EMA bands and RSI. Optimized for prop firms.";
                Name = "MeanReversionScalper";
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
                BarsRequiredToTrade = 30;
                IsInstantiatedOnEachOptimizationIteration = true;

                // Default parameters - optimized for consistency
                EMAPeriod = 20;
                StdDevPeriod = 20;
                EntryStdDev = 2.0;
                RSIPeriod = 14;
                RSIOversold = 35;
                RSIOverbought = 65;

                // Risk Management - tight for scalping
                StopLossTicks = 16;
                TakeProfitTicks = 24;
                MaxTradesPerDay = 5;

                // Session settings (Eastern Time)
                TradeLondon = true;
                TradeNewYork = true;
                LondonStartHour = 3;
                LondonEndHour = 11;
                NYStartHour = 9;
                NYStartMinute = 45;
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
                ema = EMA(Close, EMAPeriod);
                rsi = RSI(Close, RSIPeriod, 3);
                stdDev = StdDev(Close, StdDevPeriod);

                AddChartIndicator(ema);
                AddChartIndicator(rsi);
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
            }

            // Check if we're in a valid trading session
            if (!IsInTradingSession())
                return;

            // Skip if we've hit max trades
            if (tradesToday >= MaxTradesPerDay)
                return;

            // Skip if already in a position
            if (Position.MarketPosition != MarketPosition.Flat)
                return;

            // Calculate bands
            double upperBand = ema[0] + (stdDev[0] * EntryStdDev);
            double lowerBand = ema[0] - (stdDev[0] * EntryStdDev);

            // Check for mean reversion signals

            // LONG: Price below lower band + RSI oversold + price starting to reverse
            bool longCondition = Close[0] < lowerBand &&
                                Close[0] > Close[1] &&  // Price turning up
                                rsi[0] < RSIOversold &&
                                rsi[0] > rsi[1];  // RSI turning up

            // SHORT: Price above upper band + RSI overbought + price starting to reverse
            bool shortCondition = Close[0] > upperBand &&
                                 Close[0] < Close[1] &&  // Price turning down
                                 rsi[0] > RSIOverbought &&
                                 rsi[0] < rsi[1];  // RSI turning down

            if (longCondition)
            {
                EnterLong("MRLong");
                tradesToday++;
                Print($"LONG Entry @ {Close[0]} - Mean Reversion from oversold (RSI: {rsi[0]:F1})");
            }
            else if (shortCondition)
            {
                EnterShort("MRShort");
                tradesToday++;
                Print($"SHORT Entry @ {Close[0]} - Mean Reversion from overbought (RSI: {rsi[0]:F1})");
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
        [Range(5, 50)]
        [Display(Name = "EMA Period", Order = 1, GroupName = "Strategy Parameters")]
        public int EMAPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(5, 50)]
        [Display(Name = "StdDev Period", Order = 2, GroupName = "Strategy Parameters")]
        public int StdDevPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 3.5)]
        [Display(Name = "Entry StdDev Multiplier", Order = 3, GroupName = "Strategy Parameters")]
        public double EntryStdDev { get; set; }

        [NinjaScriptProperty]
        [Range(5, 30)]
        [Display(Name = "RSI Period", Order = 4, GroupName = "Strategy Parameters")]
        public int RSIPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(20, 45)]
        [Display(Name = "RSI Oversold Level", Order = 5, GroupName = "Strategy Parameters")]
        public int RSIOversold { get; set; }

        [NinjaScriptProperty]
        [Range(55, 80)]
        [Display(Name = "RSI Overbought Level", Order = 6, GroupName = "Strategy Parameters")]
        public int RSIOverbought { get; set; }

        [NinjaScriptProperty]
        [Range(5, 50)]
        [Display(Name = "Stop Loss (Ticks)", Order = 1, GroupName = "Risk Management")]
        public int StopLossTicks { get; set; }

        [NinjaScriptProperty]
        [Range(5, 100)]
        [Display(Name = "Take Profit (Ticks)", Order = 2, GroupName = "Risk Management")]
        public int TakeProfitTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 15)]
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
