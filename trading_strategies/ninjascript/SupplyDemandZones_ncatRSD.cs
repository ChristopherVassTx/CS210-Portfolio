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
    /// Supply/Demand Zone Strategy using ncatRSD indicator zones
    /// - Reads zone drawing objects from ncatRSD indicator
    /// - Executes entries on liquidity sweeps into zones
    /// - Handles risk management and position sizing
    /// </summary>
    public class SupplyDemandZones_ncatRSD : Strategy
    {
        #region Variables
        // Zone tracking from ncatRSD
        private List<ZoneInfo> demandZones = new List<ZoneInfo>();
        private List<ZoneInfo> supplyZones = new List<ZoneInfo>();

        // Track which zones we've traded
        private HashSet<string> tradedZoneTags = new HashSet<string>();

        // Session tracking
        private DateTime lastZoneScanTime = DateTime.MinValue;
        private int tradesToday = 0;
        private double dailyPnL = 0;
        private bool dailyLossLimitHit = false;

        // Current trade zone tracking
        private string currentTradeZoneTag = "";
        private double currentZoneLow = 0;
        private double currentZoneHigh = 0;
        #endregion

        public class ZoneInfo
        {
            public string Tag { get; set; }
            public double High { get; set; }
            public double Low { get; set; }
            public DateTime StartTime { get; set; }
            public int TouchCount { get; set; }
            public bool IsValid { get; set; }

            public ZoneInfo()
            {
                TouchCount = 0;
                IsValid = true;
            }
        }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Trades liquidity sweeps into ncatRSD supply/demand zones.";
                Name = "SupplyDemandZones_ncatRSD";
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

                // Entry Settings
                ZoneBuffer = 4;                 // Ticks buffer around zone
                MaxTouchesAllowed = 2;          // Only trade 1st or 2nd touch
                ZoneScanIntervalSeconds = 30;   // How often to scan for new zones

                // Risk Management
                Contracts = 1;
                RiskRewardRatio = 2.0;
                MaxTradesPerDay = 5;
                DailyLossLimit = 500;

                // Session Settings
                HardKillHour = 16;
                HardKillMinute = 0;
                SessionStartHour = 9;
                SessionStartMinute = 30;
                SessionEndHour = 15;
                SessionEndMinute = 45;
            }
            else if (State == State.DataLoaded)
            {
                // Initialize zone lists
                demandZones = new List<ZoneInfo>();
                supplyZones = new List<ZoneInfo>();
                tradedZoneTags = new HashSet<string>();
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < BarsRequiredToTrade)
                return;

            // Reset for new day
            if (Bars.IsFirstBarOfSession)
            {
                ResetForNewSession();
            }

            // Periodically scan for ncatRSD zones
            if ((Time[0] - lastZoneScanTime).TotalSeconds >= ZoneScanIntervalSeconds)
            {
                ScanForZones();
                lastZoneScanTime = Time[0];
            }

            // 4PM Hard Kill
            if (Time[0].Hour >= HardKillHour && Time[0].Minute >= HardKillMinute)
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
            tradesToday = 0;
            dailyPnL = 0;
            dailyLossLimitHit = false;
            tradedZoneTags.Clear();

            // Clear old zones - new day means new zones from ncatRSD
            demandZones.Clear();
            supplyZones.Clear();

            Print($"{Time[0]} | NEW SESSION - Zones cleared, waiting for ncatRSD zones");
        }

        private void ScanForZones()
        {
            // Try to access chart-level drawings (from all indicators including ncatRSD)
            if (ChartControl == null)
            {
                Print($"{Time[0]} | ChartControl null - running in backtest without chart");
                return;
            }

            try
            {
                // Access all drawing objects on the chart via ChartObjects
                var chartObjects = ChartControl.ChartObjects;
                if (chartObjects == null)
                    return;

                foreach (var chartObject in chartObjects)
                {
                    if (chartObject == null)
                        continue;

                    // Check if it's a Rectangle
                    if (chartObject is DrawingTools.Rectangle rect)
                    {
                        string tag = rect.Tag;
                        if (string.IsNullOrEmpty(tag))
                            continue;

                        // Demand zones from ncatRSD: dnzone_*
                        if (tag.StartsWith("dnzone_"))
                        {
                            if (!demandZones.Any(z => z.Tag == tag))
                            {
                                var zone = new ZoneInfo
                                {
                                    Tag = tag,
                                    High = Math.Max(rect.StartAnchor.Price, rect.EndAnchor.Price),
                                    Low = Math.Min(rect.StartAnchor.Price, rect.EndAnchor.Price),
                                    StartTime = rect.StartAnchor.Time,
                                    IsValid = true
                                };
                                demandZones.Add(zone);
                                Print($"{Time[0]} | DEMAND ZONE found: {zone.Low:F2} - {zone.High:F2} ({tag})");
                            }
                        }
                        // Supply zones from ncatRSD: upzone_*
                        else if (tag.StartsWith("upzone_"))
                        {
                            if (!supplyZones.Any(z => z.Tag == tag))
                            {
                                var zone = new ZoneInfo
                                {
                                    Tag = tag,
                                    High = Math.Max(rect.StartAnchor.Price, rect.EndAnchor.Price),
                                    Low = Math.Min(rect.StartAnchor.Price, rect.EndAnchor.Price),
                                    StartTime = rect.StartAnchor.Time,
                                    IsValid = true
                                };
                                supplyZones.Add(zone);
                                Print($"{Time[0]} | SUPPLY ZONE found: {zone.Low:F2} - {zone.High:F2} ({tag})");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Print($"{Time[0]} | Error scanning zones: {ex.Message}");
            }
        }

        private void CheckForDemandEntry()
        {
            foreach (var zone in demandZones.Where(z => z.IsValid && z.TouchCount < MaxTouchesAllowed))
            {
                // Skip if we already traded this zone
                if (tradedZoneTags.Contains(zone.Tag))
                    continue;

                double zoneHighWithBuffer = zone.High + ZoneBuffer * TickSize;
                double zoneLowWithBuffer = zone.Low - ZoneBuffer * TickSize;

                // Check if price wicked INTO the zone (liquidity sweep)
                bool wickedIntoZone = Low[0] <= zoneHighWithBuffer && Low[0] >= zoneLowWithBuffer;

                // Check if price CLOSED above the zone (rejection)
                bool closedAboveZone = Close[0] > zoneHighWithBuffer;

                // Check for bullish rejection candle
                bool rejectionCandle = Close[0] > Open[0] && Low[0] < Open[0];

                if (wickedIntoZone && closedAboveZone && rejectionCandle)
                {
                    zone.TouchCount++;

                    // Calculate stop and target
                    double stopPrice = zoneLowWithBuffer - 2 * TickSize;
                    double entryPrice = Close[0];
                    double risk = entryPrice - stopPrice;
                    double targetPrice = entryPrice + (risk * RiskRewardRatio);

                    SetStopLoss(CalculationMode.Price, stopPrice);
                    SetProfitTarget(CalculationMode.Price, targetPrice);

                    EnterLong(Contracts, "DemandLong");
                    tradesToday++;
                    tradedZoneTags.Add(zone.Tag);

                    // Track current trade zone for invalidation
                    currentTradeZoneTag = zone.Tag;
                    currentZoneLow = zone.Low;
                    currentZoneHigh = zone.High;

                    Print($"{Time[0]} | LONG @ {entryPrice:F2} | Stop: {stopPrice:F2} | Target: {targetPrice:F2} | Zone: {zone.Tag} Touch #{zone.TouchCount}");
                    return;
                }

                // Invalidate zone if price closes THROUGH it
                if (Close[0] < zoneLowWithBuffer)
                {
                    zone.IsValid = false;
                    Print($"{Time[0]} | DEMAND ZONE INVALIDATED ({zone.Tag}) - Price closed through");
                }
            }
        }

        private void CheckForSupplyEntry()
        {
            foreach (var zone in supplyZones.Where(z => z.IsValid && z.TouchCount < MaxTouchesAllowed))
            {
                // Skip if we already traded this zone
                if (tradedZoneTags.Contains(zone.Tag))
                    continue;

                double zoneHighWithBuffer = zone.High + ZoneBuffer * TickSize;
                double zoneLowWithBuffer = zone.Low - ZoneBuffer * TickSize;

                // Check if price wicked INTO the zone (liquidity sweep)
                bool wickedIntoZone = High[0] >= zoneLowWithBuffer && High[0] <= zoneHighWithBuffer;

                // Check if price CLOSED below the zone (rejection)
                bool closedBelowZone = Close[0] < zoneLowWithBuffer;

                // Check for bearish rejection candle
                bool rejectionCandle = Close[0] < Open[0] && High[0] > Open[0];

                if (wickedIntoZone && closedBelowZone && rejectionCandle)
                {
                    zone.TouchCount++;

                    // Calculate stop and target
                    double stopPrice = zoneHighWithBuffer + 2 * TickSize;
                    double entryPrice = Close[0];
                    double risk = stopPrice - entryPrice;
                    double targetPrice = entryPrice - (risk * RiskRewardRatio);

                    SetStopLoss(CalculationMode.Price, stopPrice);
                    SetProfitTarget(CalculationMode.Price, targetPrice);

                    EnterShort(Contracts, "SupplyShort");
                    tradesToday++;
                    tradedZoneTags.Add(zone.Tag);

                    // Track current trade zone for invalidation
                    currentTradeZoneTag = zone.Tag;
                    currentZoneLow = zone.Low;
                    currentZoneHigh = zone.High;

                    Print($"{Time[0]} | SHORT @ {entryPrice:F2} | Stop: {stopPrice:F2} | Target: {targetPrice:F2} | Zone: {zone.Tag} Touch #{zone.TouchCount}");
                    return;
                }

                // Invalidate zone if price closes THROUGH it
                if (Close[0] > zoneHighWithBuffer)
                {
                    zone.IsValid = false;
                    Print($"{Time[0]} | SUPPLY ZONE INVALIDATED ({zone.Tag}) - Price closed through");
                }
            }
        }

        private void CheckForInvalidation()
        {
            // If long and price closes below demand zone - immediate exit
            if (Position.MarketPosition == MarketPosition.Long)
            {
                double zoneLowWithBuffer = currentZoneLow - ZoneBuffer * TickSize;
                if (Close[0] < zoneLowWithBuffer)
                {
                    ExitLong("Invalidated");
                    Print($"{Time[0]} | LONG INVALIDATED - Price closed through zone");
                }
            }

            // If short and price closes above supply zone - immediate exit
            if (Position.MarketPosition == MarketPosition.Short)
            {
                double zoneHighWithBuffer = currentZoneHigh + ZoneBuffer * TickSize;
                if (Close[0] > zoneHighWithBuffer)
                {
                    ExitShort("Invalidated");
                    Print($"{Time[0]} | SHORT INVALIDATED - Price closed through zone");
                }
            }
        }

        private bool IsInTradingSession()
        {
            int hour = Time[0].Hour;
            int minute = Time[0].Minute;
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
                if (lastTrade.Exit.Time.Date == Time[0].Date)
                {
                    dailyPnL = 0;
                    foreach (Trade trade in SystemPerformance.AllTrades)
                    {
                        if (trade.Exit.Time.Date == Time[0].Date)
                            dailyPnL += trade.ProfitCurrency;
                    }

                    if (dailyPnL <= -DailyLossLimit)
                        dailyLossLimitHit = true;

                    Print($"{Time[0]} | Trade closed | Daily P&L: ${dailyPnL:F2}");
                }
            }
        }

        #region Properties
        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Zone Buffer (Ticks)", Order = 1, GroupName = "1. Entry Settings")]
        public int ZoneBuffer { get; set; }

        [NinjaScriptProperty]
        [Range(1, 3)]
        [Display(Name = "Max Touches Allowed", Description = "Only trade 1st or 2nd touch", Order = 2, GroupName = "1. Entry Settings")]
        public int MaxTouchesAllowed { get; set; }

        [NinjaScriptProperty]
        [Range(5, 300)]
        [Display(Name = "Zone Scan Interval (sec)", Description = "How often to scan for new ncatRSD zones", Order = 3, GroupName = "1. Entry Settings")]
        public int ZoneScanIntervalSeconds { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Contracts", Order = 1, GroupName = "2. Risk Management")]
        public int Contracts { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 5.0)]
        [Display(Name = "Risk/Reward Ratio", Description = "Target at this multiple of risk", Order = 2, GroupName = "2. Risk Management")]
        public double RiskRewardRatio { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Max Trades Per Day", Order = 3, GroupName = "2. Risk Management")]
        public int MaxTradesPerDay { get; set; }

        [NinjaScriptProperty]
        [Range(100, 5000)]
        [Display(Name = "Daily Loss Limit ($)", Order = 4, GroupName = "2. Risk Management")]
        public double DailyLossLimit { get; set; }

        [NinjaScriptProperty]
        [Range(0, 23)]
        [Display(Name = "Hard Kill Hour", Order = 5, GroupName = "2. Risk Management")]
        public int HardKillHour { get; set; }

        [NinjaScriptProperty]
        [Range(0, 59)]
        [Display(Name = "Hard Kill Minute", Order = 6, GroupName = "2. Risk Management")]
        public int HardKillMinute { get; set; }

        [NinjaScriptProperty]
        [Range(0, 23)]
        [Display(Name = "Session Start Hour", Order = 1, GroupName = "3. Session")]
        public int SessionStartHour { get; set; }

        [NinjaScriptProperty]
        [Range(0, 59)]
        [Display(Name = "Session Start Minute", Order = 2, GroupName = "3. Session")]
        public int SessionStartMinute { get; set; }

        [NinjaScriptProperty]
        [Range(0, 23)]
        [Display(Name = "Session End Hour", Order = 3, GroupName = "3. Session")]
        public int SessionEndHour { get; set; }

        [NinjaScriptProperty]
        [Range(0, 59)]
        [Display(Name = "Session End Minute", Order = 4, GroupName = "3. Session")]
        public int SessionEndMinute { get; set; }
        #endregion
    }
}
