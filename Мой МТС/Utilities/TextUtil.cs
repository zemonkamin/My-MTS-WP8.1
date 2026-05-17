using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;

namespace Мой_МТС.Utilities
{
    public static class TextUtil
    {
        private static readonly string[] MonthNames = new[]
        {
            "январе", "феврале", "марте", "апреле", "мае", "июне",
            "июле", "августе", "сентябре", "октябре", "ноябре", "декабре"
        };

        public static string Safe(string value)
        {
            if (String.IsNullOrWhiteSpace(value))
                return "—";
            return NormalizeRuble(Regex.Replace(value, "\\s+", " ").Trim());
        }

        public static string StripHtml(string value)
        {
            if (String.IsNullOrWhiteSpace(value))
                return "";
            string text = Regex.Replace(value, "<[^>]+>", "");
            text = text.Replace("&nbsp;", " ").Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">");
            return NormalizeRuble(Regex.Replace(text, "\\s+", " ").Trim());
        }

        public static string Money(double? value, string currency)
        {
            if (!value.HasValue)
                return "—";
            string c = NormalizeCurrency(currency);
            double v = value.Value;
            if (Math.Abs(v - Math.Round(v)) < 0.001)
                return ((int)Math.Round(v)).ToString("N0", CultureInfo.CurrentCulture) + " " + c;
            return v.ToString("N2", CultureInfo.CurrentCulture) + " " + c;
        }

        private static string NormalizeCurrency(string currency)
        {
            if (String.IsNullOrWhiteSpace(currency) || currency == "RUB" || currency == "₽")
                return "руб.";
            return NormalizeRuble(currency);
        }

        public static string NormalizeRuble(string value)
        {
            if (String.IsNullOrEmpty(value))
                return value;
            return value.Replace("₽", "руб.");
        }

        public static string Phone(string value)
        {
            if (String.IsNullOrWhiteSpace(value))
                return "—";
            string digits = Regex.Replace(value, "\\D", "");
            if (digits.Length == 10)
                digits = "7" + digits;
            if (digits.Length == 11 && digits.StartsWith("7", StringComparison.Ordinal))
                return "+7 " + digits.Substring(1, 3) + " " + digits.Substring(4, 3) + "-" + digits.Substring(7, 2) + "-" + digits.Substring(9, 2);
            return value;
        }

        public static string DateRu(string value, bool withTime)
        {
            DateTimeOffset dto;
            if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out dto))
                return String.IsNullOrWhiteSpace(value) ? "—" : value;
            DateTime local = dto.ToLocalTime().DateTime;
            return withTime ? local.ToString("dd.MM.yyyy HH:mm") : local.ToString("dd.MM.yyyy");
        }

        public static string CurrentMonthName()
        {
            int index = DateTime.Now.Month - 1;
            if (index < 0 || index >= MonthNames.Length)
                return "месяце";
            return MonthNames[index];
        }

        public static double ClampPercent(double value)
        {
            if (Double.IsNaN(value) || Double.IsInfinity(value))
                return 0;
            if (value < 0)
                return 0;
            if (value > 100)
                return 100;
            return value;
        }

        public static Brush Brush(string hex)
        {
            return new SolidColorBrush((Color)Application.Current.Resources[hex]);
        }
    }
}
