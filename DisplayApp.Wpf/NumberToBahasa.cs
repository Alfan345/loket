using System;

namespace DisplayApp.Wpf
{
    public static class NumberToBahasa
    {
        public static bool TryParseSequenceFromTicketNumber(string ticketNumber, out int seq)
        {
            seq = 0;
            var parts = ticketNumber.Split('-', '_');
            if (parts.Length > 1 && int.TryParse(parts[1], out seq))
                return true;
            return false;
        }

        public static string ToWords(int number)
        {
            // Simple implementation for demo: only supports 0-10
            string[] words = { "nol", "satu", "dua", "tiga", "empat", "lima", "enam", "tujuh", "delapan", "sembilan", "sepuluh" };
            if (number >= 0 && number < words.Length)
                return words[number];
            return number.ToString();
        }

        public static string LoketToWords(int counter)
        {
            // Simple implementation for demo
            return ToWords(counter);
        }
    }
}
