using System.Collections.Generic;

namespace SchemeBuilder.Core
{
    /// <summary>
    /// «Квартира 2» должна идти раньше «Квартиры 10», а обычное сравнение строк ставит их
    /// наоборот. Числа внутри имени сравниваются как числа.
    ///
    /// Тем же порядком идут колонки матрицы и строки сводки — сравнение вынесено сюда, чтобы
    /// схема и таблица не разошлись в порядке зон.
    /// </summary>
    internal class NaturalComparer : IComparer<string>
    {
        public static readonly NaturalComparer Instance = new NaturalComparer();

        public int Compare(string x, string y)
        {
            if (x == null || y == null) return string.CompareOrdinal(x, y);

            int i = 0, j = 0;

            while (i < x.Length && j < y.Length)
            {
                if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
                {
                    int startX = i, startY = j;
                    while (i < x.Length && char.IsDigit(x[i])) i++;
                    while (j < y.Length && char.IsDigit(y[j])) j++;

                    string numberX = x.Substring(startX, i - startX).TrimStart('0');
                    string numberY = y.Substring(startY, j - startY).TrimStart('0');

                    if (numberX.Length != numberY.Length) return numberX.Length - numberY.Length;

                    int digits = string.CompareOrdinal(numberX, numberY);
                    if (digits != 0) return digits;

                    continue;
                }

                int letters = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
                if (letters != 0) return letters;

                i++;
                j++;
            }

            return (x.Length - i) - (y.Length - j);
        }
    }
}
