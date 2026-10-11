// Ehlers Master Upper Overlay - NinjaTrader 8 port of the thinkscript study
//
// Install:
//   1. Copy to  Documents\NinjaTrader 8\bin\Custom\Indicators\
//   2. NinjaScript Editor: press F5 to compile
//   3. Chart > Indicators > "Ehlers Master Upper Overlay"
//
// Plots (all on the price panel):
//   SuperSmoother   cyan when rising, red when falling
//   ITrend          lime when ITrend >= trigger, yellow otherwise
//   ITrend Trigger  ITrend delayed by 2 bars (dotted yellow)
//   VWAP            session VWAP (resets at the start of each session)
//   Upper/Lower     SuperSmoother +/- NumDev * local deviation
//   Trail Stop      SuperSmoother -/+ StopMult * local deviation (light red floor
//                   while SuperSmoother is rising, light green ceiling when falling)
//   Exhaustion dots high > upper band (magenta) / low < lower band (cyan)
//   ITrend arrows   triangle up / down on ITrend crossing its trigger
//
// The price source is the median price (H+L)/2, same as hl2 in thinkscript.

#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class EhlersMasterUpperOverlay : Indicator
    {
        private const int PlotSS        = 0;
        private const int PlotITrend    = 1;
        private const int PlotITrendTrg = 2;
        private const int PlotVwap      = 3;
        private const int PlotUpper     = 4;
        private const int PlotLower     = 5;
        private const int PlotStop      = 6;
        private const int PlotUpperDot  = 7;
        private const int PlotLowerDot  = 8;
        private const int PlotArrowUp   = 9;
        private const int PlotArrowDown = 10;

        // Internal state lives in its own series so hiding a plot never breaks the recursion
        private Series<double> ssS;
        private Series<double> itS;
        private Series<double> diffS;
        private Series<double> cumPV;
        private Series<double> cumV;

        private double ssC1;
        private double ssC2;
        private double ssC3;
        private double itAlpha;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description                 = "SuperSmoother, ITrend, VWAP, deviation channels, trailing stop and exhaustion markers";
                Name                        = "Ehlers Master Upper Overlay";
                Calculate                   = Calculate.OnBarClose;
                IsOverlay                   = true;
                DisplayInDataBox            = true;
                DrawOnPricePanel            = true;
                IsSuspendedWhileInactive    = true;
                PaintPriceMarkers           = false;

                SsLength                    = 20;
                ItrendLength                = 20;
                ChannelLength               = 50;
                NumDev                      = 2.0;
                StopMult                    = 2.0;

                ShowSuperSmoother           = true;
                ShowITrend                  = true;
                ShowVWAP                    = true;
                ShowChannels                = true;
                ShowExhaustionDots          = true;
                ShowTrailingStop            = true;

                AddPlot(new Stroke(Brushes.Cyan, 2),                          PlotStyle.Line,         "SuperSmoother");
                AddPlot(new Stroke(Brushes.Lime, 2),                          PlotStyle.Line,         "ITrend");
                AddPlot(new Stroke(Brushes.Yellow, DashStyleHelper.Dot, 1),   PlotStyle.Line,         "ITrend Trigger");
                AddPlot(new Stroke(Brushes.DodgerBlue, 2),                    PlotStyle.Line,         "VWAP");
                AddPlot(new Stroke(Brushes.Gray, DashStyleHelper.Dash, 1),    PlotStyle.Line,         "Upper Channel");
                AddPlot(new Stroke(Brushes.Gray, DashStyleHelper.Dash, 1),    PlotStyle.Line,         "Lower Channel");
                AddPlot(new Stroke(Brushes.LightCoral, DashStyleHelper.Dot, 2), PlotStyle.Line,       "Trail Stop");
                AddPlot(new Stroke(Brushes.Magenta, 3),                       PlotStyle.Dot,          "Upper Exhaustion");
                AddPlot(new Stroke(Brushes.Cyan, 3),                          PlotStyle.Dot,          "Lower Exhaustion");
                AddPlot(new Stroke(Brushes.Lime, 2),                          PlotStyle.TriangleUp,   "ITrend Up");
                AddPlot(new Stroke(Brushes.Red, 2),                           PlotStyle.TriangleDown, "ITrend Down");
            }
            else if (State == State.DataLoaded)
            {
                ssS   = new Series<double>(this);
                itS   = new Series<double>(this);
                diffS = new Series<double>(this, MaximumBarsLookBack.Infinite);
                cumPV = new Series<double>(this);
                cumV  = new Series<double>(this);

                // SuperSmoother coefficients
                double a = Math.Exp(-1.414 * Math.PI / SsLength);
                ssC2 = 2.0 * a * Math.Cos(1.414 * Math.PI / SsLength);
                ssC3 = -a * a;
                ssC1 = 1.0 - ssC2 - ssC3;

                // ITrend smoothing constant
                itAlpha = 2.0 / (ItrendLength + 1);
            }
        }

        protected override void OnBarUpdate()
        {
            double src = Median[0];

            // 1. SuperSmoother (first two bars seeded with price)
            if (CurrentBar < 2)
                ssS[0] = src;
            else
                ssS[0] = ssC1 * (src + Median[1]) / 2.0 + ssC2 * ssS[1] + ssC3 * ssS[2];

            bool ssRising = CurrentBar == 0 || ssS[0] >= ssS[1];

            // 2. Instantaneous Trendline and trigger (ITrend delayed 2 bars)
            double c = itAlpha;
            if (CurrentBar < 2)
                itS[0] = src;
            else
                itS[0] = (c - c * c / 4.0) * src
                       + 0.5 * c * c * Median[1]
                       - (c - 0.75 * c * c) * Median[2]
                       + 2.0 * (1.0 - c) * itS[1]
                       - (1.0 - c) * (1.0 - c) * itS[2];

            bool hasTrigger     = CurrentBar >= 2;
            double itTrigger    = hasTrigger ? itS[2] : 0;
            bool itBullish      = hasTrigger && itS[0] >= itTrigger;
            bool itCrossUp      = CurrentBar >= 3 && itS[0] > itS[2] && itS[1] <= itS[3];
            bool itCrossDown    = CurrentBar >= 3 && itS[0] < itS[2] && itS[1] >= itS[3];

            // 3. Session VWAP (resets on first bar of each session; price = typical price)
            double vwapPrice = (High[0] + Low[0] + Close[0]) / 3.0;
            double prevPV    = (CurrentBar == 0 || Bars.IsFirstBarOfSession) ? 0 : cumPV[1];
            double prevV     = (CurrentBar == 0 || Bars.IsFirstBarOfSession) ? 0 : cumV[1];
            cumPV[0] = prevPV + vwapPrice * Volume[0];
            cumV[0]  = prevV + Volume[0];
            double vwap = cumV[0] > 0 ? cumPV[0] / cumV[0] : vwapPrice;

            // Local deviation: population standard deviation of (price - SuperSmoother)
            diffS[0] = src - ssS[0];
            bool devValid = CurrentBar >= ChannelLength - 1;
            double localDev = 0;
            if (devValid)
            {
                double mean = 0;
                for (int i = 0; i < ChannelLength; i++)
                    mean += diffS[i];
                mean /= ChannelLength;

                double sumSq = 0;
                for (int i = 0; i < ChannelLength; i++)
                {
                    double d = diffS[i] - mean;
                    sumSq += d * d;
                }
                localDev = Math.Sqrt(sumSq / ChannelLength);
            }

            double upperBand = ssS[0] + NumDev * localDev;
            double lowerBand = ssS[0] - NumDev * localDev;

            // 4. Adaptive trailing stop
            double longStop  = ssS[0] - StopMult * localDev;
            double shortStop = ssS[0] + StopMult * localDev;
            double trailStop = ssRising ? longStop : shortStop;

            // 5. Band exhaustion
            bool upperExhaustion = devValid && High[0] > upperBand;
            bool lowerExhaustion = devValid && Low[0]  < lowerBand;

            // 6. Plots
            SetPlot(PlotSS,        ShowSuperSmoother,                      ssS[0],  ssRising ? Brushes.Cyan : Brushes.Red);
            SetPlot(PlotITrend,    ShowITrend,                             itS[0],  itBullish ? Brushes.Lime : Brushes.Yellow);
            SetPlot(PlotITrendTrg, ShowITrend && hasTrigger,               itTrigger, null);
            SetPlot(PlotVwap,      ShowVWAP,                               vwap,    null);
            SetPlot(PlotUpper,     ShowChannels && devValid,               upperBand, null);
            SetPlot(PlotLower,     ShowChannels && devValid,               lowerBand, null);
            SetPlot(PlotStop,      ShowTrailingStop && devValid,           trailStop, ssRising ? Brushes.LightCoral : Brushes.LightGreen);
            SetPlot(PlotUpperDot,  ShowExhaustionDots && upperExhaustion,  High[0] + 0.1 * localDev, null);
            SetPlot(PlotLowerDot,  ShowExhaustionDots && lowerExhaustion,  Low[0]  - 0.1 * localDev, null);
            SetPlot(PlotArrowUp,   ShowITrend && itCrossUp,                Low[0],  null);
            SetPlot(PlotArrowDown, ShowITrend && itCrossDown,              High[0], null);
        }

        // Writes a plot value for this bar, or clears it when the plot is hidden or has no signal
        private void SetPlot(int index, bool visible, double value, Brush brush)
        {
            if (!visible)
            {
                Values[index].Reset();
                return;
            }

            Values[index][0] = value;
            if (brush != null)
                PlotBrushes[index][0] = brush;
        }

        #region Properties
        [Range(2, int.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "SuperSmoother length", GroupName = "Parameters", Order = 0)]
        public int SsLength { get; set; }

        [Range(1, int.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "ITrend length", GroupName = "Parameters", Order = 1)]
        public int ItrendLength { get; set; }

        [Range(2, int.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "Channel length", GroupName = "Parameters", Order = 2)]
        public int ChannelLength { get; set; }

        [Range(0.0, double.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "Channel deviations", GroupName = "Parameters", Order = 3)]
        public double NumDev { get; set; }

        [Range(0.0, double.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "Stop multiplier", GroupName = "Parameters", Order = 4)]
        public double StopMult { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show SuperSmoother", GroupName = "Visibility", Order = 0)]
        public bool ShowSuperSmoother { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show ITrend", GroupName = "Visibility", Order = 1)]
        public bool ShowITrend { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show VWAP", GroupName = "Visibility", Order = 2)]
        public bool ShowVWAP { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show channels", GroupName = "Visibility", Order = 3)]
        public bool ShowChannels { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show exhaustion dots", GroupName = "Visibility", Order = 4)]
        public bool ShowExhaustionDots { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show trailing stop", GroupName = "Visibility", Order = 5)]
        public bool ShowTrailingStop { get; set; }
        #endregion
    }
}
