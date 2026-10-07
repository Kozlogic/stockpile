// Ehlers Band-Pass Filter for NinjaTrader 8
//
// Install:
//   1. Copy this file to  Documents\NinjaTrader 8\bin\Custom\Indicators\
//   2. NinjaTrader: New > NinjaScript Editor, press F5 to compile
//   3. Chart > Indicators > "Ehlers Band-Pass Filter"
//
// Algorithm (John Ehlers, "Cycle Analytics for Traders"):
//   1. A one-pole high-pass removes the trend from the input.
//   2. A 2-pole band-pass isolates the cycle at Period, with width set by Bandwidth.
//   3. Automatic gain control (AGC) rescales the output to roughly -1..+1.
//
// Tips: Ehlers recommends the median price (H+L)/2 as the input series. Set it in
// the indicator dialog under "Input series". Signal zero-crossings mark the cycle
// midpoint; peaks and troughs near +/-1 mark cycle extremes. The filter works best
// in cyclic markets and gives poor signals when price is strongly trending.

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
    public class EhlersBandPass : Indicator
    {
        private Series<double> hp;
        private Series<double> bp;
        private Series<double> peak;

        private double alpha1;
        private double alpha2;
        private double beta1;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description                 = "Ehlers Band-Pass Filter with AGC-normalised output";
                Name                        = "Ehlers Band-Pass Filter";
                Calculate                   = Calculate.OnBarClose;
                IsOverlay                   = false;
                DisplayInDataBox            = true;
                DrawOnPricePanel            = false;
                IsSuspendedWhileInactive    = true;

                Period                      = 20;
                Bandwidth                   = 0.3;

                AddPlot(new Stroke(Brushes.DodgerBlue, 2), PlotStyle.Line, "Signal");
                AddLine(Brushes.DimGray, 0, "Zero");
            }
            else if (State == State.DataLoaded)
            {
                hp   = new Series<double>(this);
                bp   = new Series<double>(this);
                peak = new Series<double>(this);

                double hpArg = 0.25 * Bandwidth * 2.0 * Math.PI / Period;
                alpha2 = (Math.Cos(hpArg) + Math.Sin(hpArg) - 1.0) / Math.Cos(hpArg);

                double gamma1 = 1.0 / Math.Cos(2.0 * Math.PI * Bandwidth / Period);
                alpha1 = gamma1 - Math.Sqrt(gamma1 * gamma1 - 1.0);
                beta1  = Math.Cos(2.0 * Math.PI / Period);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 2)
            {
                hp[0]   = 0;
                bp[0]   = 0;
                peak[0] = 0;
                return;
            }

            hp[0] = (1.0 + alpha2 / 2.0) * (Input[0] - Input[1]) + (1.0 - alpha2) * hp[1];

            bp[0] = 0.5 * (1.0 - alpha1) * (hp[0] - hp[2])
                  + beta1 * (1.0 + alpha1) * bp[1]
                  - alpha1 * bp[2];

            peak[0] = Math.Max(0.991 * peak[1], Math.Abs(bp[0]));

            Values[0][0] = peak[0] > 0 ? bp[0] / peak[0] : 0;
        }

        #region Properties
        [Range(3, int.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "Period", Description = "Centre period of the band-pass, in bars", GroupName = "Parameters", Order = 0)]
        public int Period { get; set; }

        [Range(0.01, 0.5)]
        [NinjaScriptProperty]
        [Display(Name = "Bandwidth", Description = "Fractional bandwidth (0.1 = narrow, 0.5 = wide)", GroupName = "Parameters", Order = 1)]
        public double Bandwidth { get; set; }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> Signal
        {
            get { Update(); return Values[0]; }
        }
        #endregion
    }
}
