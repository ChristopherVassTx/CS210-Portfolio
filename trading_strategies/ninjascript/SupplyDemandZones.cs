#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    /// <summary>
    /// Supply/Demand Zone Strategy with Liquidity Sweep
    /// - 5-min timeframe for zone identification
    /// - 1-min timeframe for entry
    /// - Pivot points for liquidity targets
    /// - First/second touch only
    /// - Immediate invalidation if price closes through zone
    /// </summary>
    public class SupplyDemandZones : Strategy
    {
        #region Variables
        // Zone tracking
        private double demandZoneHigh = 0;
        private double demandZoneLow = 0;
        private double supplyZoneHigh = 0;
        private double supplyZoneLow = 0;
        private int demandZoneTouches = 0;
        private int supplyZoneTouches = 0;
        private bool demandZoneValid = false;
        private bool supplyZoneValid = false;

        // Pivot tracking
        private double pivotHigh = 0;
        private double pivotLow = 0;

        // Session tracking
        private DateTime lastSessionDate = DateTime.MinValue;
        private int tradesToday = 0;
        private double dailyPnL = 0;
        private bool dailyLossLimitHit = false;

        // ATR for impulse detection
        private ATR atr;
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Supply/Demand Zone Strategy - Trades liquidity sweeps into fresh zones near pivot points.";
                Name = "SupplyDemandZones";
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

                // Zone Detection Settings
                ImpulseATRMultiple = 1.2;       // Move must be 1.2x ATR to qualify as impulse (lowered for more zones)
                ATRPeriod = 14;
                PivotLookback = 5;              // Bars to look back for pivot highs/lows
                ZoneBuffer = 4;                 // Ticks buffer around zone (increased)
                ConsolidationBodyRatio = 0.5;   // Body must be < 50% of range to be consolidation

                // Entry Settings
                MaxTouchesAllowed = 2;          // Only trade 1st or 2nd touch
                RequirePivotConfluence = false; // Disabled - was filtering too many setups

                // Risk Management
                Contracts = 1;
                RiskRewardRatio = 2.0;          // TP at 2R
                MaxTradesPerDay = 5;            // Increased for more opportunities
                DailyLossLimit = 500;

                // Session Settings
                HardKillHour = 16;
                HardKillMinute = 0;
                SessionStartHour = 9;
                SessionStartMinute = 30;
                SessionEndHour = 15;
                SessionEndMinute = 45;
            }
            else if (State == State.Configure)
            {
                // Add 5-minute data series for zone identification
                AddDataSeries(BarsPeriodType.Minute, 5);
            }
            else if (State == State.DataLoaded)
            {
                atr = ATR(BarsArray[1], ATRPeriod); // ATR on 5-min
            }
        }

        protected override void OnBarUpdate()
        {
            // Process 5-minute bars for zone identification
            if (BarsInProgress == 1)
            {
                Process5MinBar();
                return;
            }

            // Process 1-minute bars for entry
            if (BarsInProgress == 0)
            {
                Process1MinBar();
            }
        }

        private void Process5MinBar()
        {
            if (CurrentBars[1] < ATRPeriod + 5)
                return;

            // Check for new session - redraw zones
            if (Times[1][0].Date != lastSessionDate.Date)
            {
                ResetForNewSession();
                lastSessionDate = Times[1][0].Date;
            }

            // Detect impulse moves and create zones
            DetectImpulseMoves();

            // Update pivot points
            UpdatePivotPoints();
        }

        private void Process1MinBar()
        {
            if (CurrentBars[0] < 20 || CurrentBars[1] < ATRPeriod + 5)
                return;

            // 4PM Hard Kill
            if (Times[0][0].Hour >= HardKillHour)
            {
                if (Position.MarketPosition != MarketPosition.Flat)
                {
                    if (Position.MarketPosition == MarketPosition.Long)
                        ExitLong("4PM Kill");
                    else
                        ExitShort("4PM Kill");
                }
                return;
            }

            // Check session time
            if (!IsInTradingSession())
                return;

            // Daily loss limit check
            if (dailyLossLimitHit || tradesToday >= MaxTradesPerDay)
                return;

            // Already in position - check for invalidation
            if (Position.MarketPosition != MarketPosition.Flat)
            {
                CheckForInvalidation();
                return;
            }

            // Look for entry signals
            CheckForDemandEntry();
            CheckForSupplyEntry();
        }

        private void ResetForNewSession()
        {
            // Reset zones at start of each session
            demandZoneHigh = 0;
            demandZoneLow = 0;
            supplyZoneHigh = 0;
            supplyZoneLow = 0;
            demandZoneTouches = 0;
            supplyZoneTouches = 0;
            demandZoneValid = false;
            supplyZoneValid = false;
            tradesToday = 0;
            dailyPnL = 0;
            dailyLossLimitHit = false;

            Print($"{Times[1][0]} | NEW SESSION - Zones reset");
        }

        private void DetectImpulseMoves()
        {
            if (atr[0] <= 0)
                return;

            double impulseThreshold = atr[0] * ImpulseATRMultiple;

            // Check for bullish impulse (creates demand zone from consolidation candle before)
            double currentMove = Closes[1][0] - Opens[1][0];
            if (currentMove >= impulseThreshold && !demandZoneValid)
            {
                // Walk back to find the CONSOLIDATION candle before the impulse
                int consolidationBar = FindConsolidationCandle(true); // true = looking for demand (before up move)

                if (consolidationBar > 0)
                {
                    demandZoneHigh = Highs[1][consolidationBar];
                    demandZoneLow = Lows[1][consolidationBar];
                    demandZoneTouches = 0;
                    demandZoneValid = true;

                    Draw.Rectangle(this, "DemandZone" + CurrentBars[1], false, consolidationBar, demandZoneLow, -500, demandZoneHigh, Brushes.Green, Brushes.Green, 25);
                    Print($"{Times[1][0]} | DEMAND ZONE created: {demandZoneLow:F2} - {demandZoneHigh:F2} (bar -{consolidationBar})");
                }
            }

            // Check for bearish impulse (creates supply zone from consolidation candle before)
            if (currentMove <= -impulseThreshold && !supplyZoneValid)
            {
                // Walk back to find the CONSOLIDATION candle before the impulse
                int consolidationBar = FindConsolidationCandle(false); // false = looking for supply (before down move)

                if (consolidationBar > 0)
                {
                    supplyZoneHigh = Highs[1][consolidationBar];
                    supplyZoneLow = Lows[1][consolidationBar];
                    supplyZoneTouches = 0;
                    supplyZoneValid = true;

                    Draw.Rectangle(this, "SupplyZone" + CurrentBars[1], false, consolidationBar, supplyZoneLow, -500, supplyZoneHigh, Brushes.Red, Brushes.Red, 25);
                    Print($"{Times[1][0]} | SUPPLY ZONE created: {supplyZoneLow:F2} - {supplyZoneHigh:F2} (bar -{consolidationBar})");
                }
            }
        }

        private int FindConsolidationCandle(bool forDemand)
        {
            // Walk back from bar [1] to find a small-bodied consolidation candle
            // Consolidation = body is small relative to the candle's range

            for (int i = 1; i <= 10; i++) // Look back up to 10 bars
            {
                if (i >= CurrentBars[1])
                    break;

                double body = Math.Abs(Closes[1][i] - Opens[1][i]);
                double range = Highs[1][i] - Lows[1][i];

                if (range <= 0)
                    continue;

                double bodyRatio = body / range;

                // Consolidation candle: body is less than 50% of the range (small body, could have wicks)
                // Also check it's a small candle overall (not a big doji)
                bool isSmallBody = bodyRatio < ConsolidationBodyRatio;
                bool isSmallRange = range < atr[0] * 0.75; // Range less than 75% of ATR

                if (isSmallBody && isSmallRange)
                {
                    // For demand zones, we want the consolidation at a relative low
                    // For supply zones, we want the consolidation at a relative high
                    if (forDemand)
                    {
                        // Check this candle is near a local low (lower than next few candles)
                        bool isNearLow = true;
                        for (int j = 1; j < i && j <= 3; j++)
                        {
                            if (Lows[1][i] > Lows[1][j] + 5 * TickSize)
                            {
                                isNearLow = false;
                                break;
                            }
                        }
                        if (isNearLow)
                            return i;
                    }
                    else
                    {
                        // Check this candle is near a local high
                        bool isNearHigh = true;
                        for (int j = 1; j < i && j <= 3; j++)
                        {
                            if (Highs[1][i] < Highs[1][j] - 5 * TickSize)
                            {
                                isNearHigh = false;
                                break;
                            }
                        }
                        if (isNearHigh)
                            return i;
                    }
                }
            }

            // Fallback: if no consolidation found, use bar 1
            return 1;
        }

        private void UpdatePivotPoints()
        {
            // Find recent pivot high
            double highestHigh = double.MinValue;
            for (int i = 1; i <= PivotLookback; i++)
            {
                if (Highs[1][i] > highestHigh)
                    highestHigh = Highs[1][i];
            }
            pivotHigh = highestHigh;

            // Find recent pivot low
            double lowestLow = double.MaxValue;
            for (int i = 1; i <= PivotLookback; i++)
            {
                if (Lows[1][i] < lowestLow)
                    lowestLow = Lows[1][i];
            }
            pivotLow = lowestLow;
        }

        private void CheckForDemandEntry()
        {
            if (!demandZoneValid || demandZoneTouches >= MaxTouchesAllowed)
                return;

            double zoneHighWithBuffer = demandZoneHigh + ZoneBuffer * TickSize;
            double zoneLowWithBuffer = demandZoneLow - ZoneBuffer * TickSize;

            // Check if price wicked INTO the zone (liquidity sweep)
            bool wickedIntoZone = Lows[0][0] <= zoneHighWithBuffer && Lows[0][0] >= zoneLowWithBuffer;

            // Check if price CLOSED above the zone (rejection)
            bool closedAboveZone = Closes[0][0] > zoneHighWithBuffer;

            // Check for pivot confluence - pivot should be just above the zone
            bool pivotConfluence = !RequirePivotConfluence ||
                                   (pivotLow > zoneLowWithBuffer && pivotLow < zoneHighWithBuffer + 20 * TickSize);

            // Check for bullish rejection candle (relaxed - just needs to close green with lower wick)
            bool rejectionCandle = Closes[0][0] > Opens[0][0] &&  // Green candle
                                   Lows[0][0] < Opens[0][0];       // Has a lower wick

            if (wickedIntoZone && closedAboveZone && rejectionCandle)
            {
                demandZoneTouches++;

                // Calculate stop and target
                double stopPrice = zoneLowWithBuffer - 2 * TickSize;
                double entryPrice = Closes[0][0];
                double risk = entryPrice - stopPrice;
                double targetPrice = entryPrice + (risk * RiskRewardRatio);

                SetStopLoss(CalculationMode.Price, stopPrice);
                SetProfitTarget(CalculationMode.Price, targetPrice);

                EnterLong(Contracts, "DemandLong");
                tradesToday++;

                Print($"{Times[0][0]} | LONG @ {entryPrice:F2} | Stop: {stopPrice:F2} | Target: {targetPrice:F2} | Touch #{demandZoneTouches}");
            }

            // Invalidate zone if price closes THROUGH it
            if (Closes[0][0] < zoneLowWithBuffer)
            {
                demandZoneValid = false;
                Print($"{Times[0][0]} | DEMAND ZONE INVALIDATED - Price closed through");
            }
        }

        private void CheckForSupplyEntry()
        {
            if (!supplyZoneValid || supplyZoneTouches >= MaxTouchesAllowed)
                return;

            double zoneHighWithBuffer = supplyZoneHigh + ZoneBuffer * TickSize;
            double zoneLowWithBuffer = supplyZoneLow - ZoneBuffer * TickSize;

            // Check if price wicked INTO the zone (liquidity sweep)
            bool wickedIntoZone = Highs[0][0] >= zoneLowWithBuffer && Highs[0][0] <= zoneHighWithBuffer;

            // Check if price CLOSED below the zone (rejection)
            bool closedBelowZone = Closes[0][0] < zoneLowWithBuffer;

            // Check for pivot confluence - pivot should be just below the zone
            bool pivotConfluence = !RequirePivotConfluence ||
                                   (pivotHigh < zoneHighWithBuffer && pivotHigh > zoneLowWithBuffer - 20 * TickSize);

            // Check for bearish rejection candle (relaxed - just needs to close red with upper wick)
            bool rejectionCandle = Closes[0][0] < Opens[0][0] &&  // Red candle
                                   Highs[0][0] > Opens[0][0];      // Has an upper wick

            if (wickedIntoZone && closedBelowZone && rejectionCandle)
            {
                supplyZoneTouches++;

                // Calculate stop and target
                double stopPrice = zoneHighWithBuffer + 2 * TickSize;
                double entryPrice = Closes[0][0];
                double risk = stopPrice - entryPrice;
                double targetPrice = entryPrice - (risk * RiskRewardRatio);

                SetStopLoss(CalculationMode.Price, stopPrice);
                SetProfitTarget(CalculationMode.Price, targetPrice);

                EnterShort(Contracts, "SupplyShort");
                tradesToday++;

                Print($"{Times[0][0]} | SHORT @ {entryPrice:F2} | Stop: {stopPrice:F2} | Target: {targetPrice:F2} | Touch #{supplyZoneTouches}");
            }

            // Invalidate zone if price closes THROUGH it
            if (Closes[0][0] > zoneHighWithBuffer)
            {
                supplyZoneValid = false;
                Print($"{Times[0][0]} | SUPPLY ZONE INVALIDATED - Price closed through");
            }
        }

        private void CheckForInvalidation()
        {
            // If long and price closes below demand zone - immediate exit
            if (Position.MarketPosition == MarketPosition.Long)
            {
                double zoneLowWithBuffer = demandZoneLow - ZoneBuffer * TickSize;
                if (Closes[0][0] < zoneLowWithBuffer)
                {
                    ExitLong("Invalidated");
                    Print($"{Times[0][0]} | LONG INVALIDATED - Price closed through zone");
                }
            }

            // If short and price closes above supply zone - immediate exit
            if (Position.MarketPosition == MarketPosition.Short)
            {
                double zoneHighWithBuffer = supplyZoneHigh + ZoneBuffer * TickSize;
                if (Closes[0][0] > zoneHighWithBuffer)
                {
                    ExitShort("Invalidated");
                    Print($"{Times[0][0]} | SHORT INVALIDATED - Price closed through zone");
                }
            }
        }

        private bool IsInTradingSession()
        {
            int hour = Times[0][0].Hour;
            int minute = Times[0][0].Minute;
            double timeValue = hour + minute / 60.0;
            double startTime = SessionStartHour + SessionStartMinute / 60.0;
            double endTime = SessionEndHour + SessionEndMinute / 60.0;

            return timeValue >= startTime && timeValue < endTime;
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (Position.MarketPosition == MarketPosition.Flat && SystemPerformance.AllTrades.Count > 0)
            {
                Trade lastTrade = SystemPerformance.AllTrades[SystemPerformance.AllTrades.Count - 1];
                if (lastTrade.Exit.Time.Date == Times[0][0].Date)
                {
                    dailyPnL = 0;
                    foreach (Trade trade in SystemPerformance.AllTrades)
                    {
                        if (trade.Exit.Time.Date == Times[0][0].Date)
                            dailyPnL += trade.ProfitCurrency;
                    }

                    if (dailyPnL <= -DailyLossLimit)
                        dailyLossLimitHit = true;

                    Print($"{Times[0][0]} | Trade closed | Daily P&L: ${dailyPnL:F2}");
                }
            }
        }

        #region Properties
        [NinjaScriptProperty]
        [Range(1.0, 5.0)]
        [Display(Name = "Impulse ATR Multiple", Description = "Move size to qualify as impulse (x ATR)", Order = 1, GroupName = "1. Zone Detection")]
        public double ImpulseATRMultiple { get; set; }

        [NinjaScriptProperty]
        [Range(5, 30)]
        [Display(Name = "ATR Period", Order = 2, GroupName = "1. Zone Detection")]
        public int ATRPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(3, 20)]
        [Display(Name = "Pivot Lookback Bars", Order = 3, GroupName = "1. Zone Detection")]
        public int PivotLookback { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Zone Buffer (Ticks)", Order = 4, GroupName = "1. Zone Detection")]
        public int ZoneBuffer { get; set; }

        [NinjaScriptProperty]
        [Range(0.2, 0.8)]
        [Display(Name = "Consolidation Body Ratio", Description = "Max body/range ratio for consolidation candle", Order = 5, GroupName = "1. Zone Detection")]
        public double ConsolidationBodyRatio { get; set; }

        [NinjaScriptProperty]
        [Range(1, 3)]
        [Display(Name = "Max Touches Allowed", Description = "Only trade 1st or 2nd touch", Order = 1, GroupName = "2. Entry Settings")]
        public int MaxTouchesAllowed { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Require Pivot Confluence", Order = 2, GroupName = "2. Entry Settings")]
        public bool RequirePivotConfluence { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Contracts", Order = 1, GroupName = "3. Risk Management")]
        public int Contracts { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 5.0)]
        [Display(Name = "Risk/Reward Ratio", Description = "Target at this multiple of risk", Order = 2, GroupName = "3. Risk Management")]
        public double RiskRewardRatio { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Max Trades Per Day", Order = 3, GroupName = "3. Risk Management")]
        public int MaxTradesPerDay { get; set; }

        [NinjaScriptProperty]
        [Range(100, 5000)]
        [Display(Name = "Daily Loss Limit ($)", Order = 4, GroupName = "3. Risk Management")]
        public double DailyLossLimit { get; set; }

        [NinjaScriptProperty]
        [Range(0, 23)]
        [Display(Name = "Hard Kill Hour", Order = 5, GroupName = "3. Risk Management")]
        public int HardKillHour { get; set; }

        [NinjaScriptProperty]
        [Range(0, 59)]
        [Display(Name = "Hard Kill Minute", Order = 6, GroupName = "3. Risk Management")]
        public int HardKillMinute { get; set; }

        [NinjaScriptProperty]
        [Range(0, 23)]
        [Display(Name = "Session Start Hour", Order = 1, GroupName = "4. Session")]
        public int SessionStartHour { get; set; }

        [NinjaScriptProperty]
        [Range(0, 59)]
        [Display(Name = "Session Start Minute", Order = 2, GroupName = "4. Session")]
        public int SessionStartMinute { get; set; }

        [NinjaScriptProperty]
        [Range(0, 23)]
        [Display(Name = "Session End Hour", Order = 3, GroupName = "4. Session")]
        public int SessionEndHour { get; set; }

        [NinjaScriptProperty]
        [Range(0, 59)]
        [Display(Name = "Session End Minute", Order = 4, GroupName = "4. Session")]
        public int SessionEndMinute { get; set; }
        #endregion
    }
}
