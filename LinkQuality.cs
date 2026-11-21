using System;

namespace SDRSharp.InmarsatAero
{
    // Link figures from the coded bits the Viterbi decoder corrected. Every coded bit is one antipodal sample
    // (10500: an arm decision); at 600 / 1200 a coded bit is the product of two neighbouring samples, so its error
    // rate is about twice the samples', undone before the Es/N0 is taken. Eb/N0 is per information bit (rate 1/2: +3 dB).
    // Margin: Es/N0 above where the K = 7 code stops delivering, taken as 10% coded-bit errors (self-test: CRCs mostly
    // good at 8%, gone at 11%).
    public static class LinkQuality
    {
        public const double FailureBer = 0.10;
        public const int MinimumBits = 2000; // checked coded bits before a figure is shown
        public const int MinimumErrors = 3; // fewer counted errors: the figure is a lower bound

        // ber: coded-bit error rate after differential decoding (600 / 1200) or of the arm decisions (10500)
        public static double EsN0Db(double ber, bool differential)
        {
            var p = differential ? 0.5 * (1.0 - Math.Sqrt(Math.Max(0.0, 1.0 - 2.0 * Math.Min(ber, 0.4999)))) : ber;
            return EsN0ForBer(Math.Max(p, 1e-9));
        }

        // margin for a given Es/N0 (dB): how far above the Es/N0 at which decoding fails
        public static double MarginFromEsN0(double esN0Db, bool differential)
        {
            return esN0Db - EsN0Db(FailureBer, differential);
        }

        public static double MarginDb(double ber, bool differential)
        {
            return EsN0Db(ber, differential) - EsN0Db(FailureBer, differential);
        }

        // Es/N0 (dB) at which an antipodal signal has bit error rate p = Q(sqrt(2 Es/N0)); bisection on dB
        private static double EsN0ForBer(double p)
        {
            var lo = -15.0;
            var hi = 20.0;
            for (var i = 0; i < 40; i++)
            {
                var mid = 0.5 * (lo + hi);
                var ber = 0.5 * Erfc(Math.Sqrt(Math.Pow(10.0, mid / 10.0)));
                if (ber > p)
                {
                    lo = mid;
                }
                else
                {
                    hi = mid;
                }
            }
            return 0.5 * (lo + hi);
        }

        // Complementary error function, Chebyshev fit (fractional error < 1.2e-7)
        private static double Erfc(double x)
        {
            var z = Math.Abs(x);
            var t = 1.0 / (1.0 + 0.5 * z);
            var r = t * Math.Exp(-z * z - 1.26551223 + t * (1.00002368 + t * (0.37409196 + t * (0.09678418 + t * (-0.18628806
                + t * (0.27886807 + t * (-1.13520398 + t * (1.48851587 + t * (-0.82215223 + t * 0.17087277)))))))));
            return x >= 0 ? r : 2.0 - r;
        }
    }
}
