namespace ReportLogBatcher.Core.Services;

public sealed class NaturalStringComparer : IComparer<string>
{
    public static NaturalStringComparer Instance { get; } = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
            return 0;
        if (x is null)
            return -1;
        if (y is null)
            return 1;

        var natural = CompareNatural(x, y);
        if (natural != 0)
            return natural;

        return string.CompareOrdinal(x, y);
    }

    private static int CompareNatural(string x, string y)
    {
        var i = 0;
        var j = 0;

        while (i < x.Length && j < y.Length)
        {
            var a = char.ToLowerInvariant(x[i]);
            var b = char.ToLowerInvariant(y[j]);

            if (char.IsDigit(a) && char.IsDigit(b))
            {
                var numberStartX = i;
                var numberStartY = j;

                while (i < x.Length && char.IsDigit(x[i]))
                    i++;
                while (j < y.Length && char.IsDigit(y[j]))
                    j++;

                var numberLengthX = i - numberStartX;
                var numberLengthY = j - numberStartY;
                var significantStartX = numberStartX + CountLeadingZeros(x, numberStartX, i);
                var significantStartY = numberStartY + CountLeadingZeros(y, numberStartY, j);
                var significantLengthX = i - significantStartX;
                var significantLengthY = j - significantStartY;

                if (significantLengthX != significantLengthY)
                    return significantLengthX < significantLengthY ? -1 : 1;

                for (var k = 0; k < significantLengthX; k++)
                {
                    var digitX = x[significantStartX + k];
                    var digitY = y[significantStartY + k];
                    if (digitX != digitY)
                        return digitX < digitY ? -1 : 1;
                }

                if (numberLengthX != numberLengthY)
                    return numberLengthX < numberLengthY ? -1 : 1;

                continue;
            }

            if (a != b)
                return a < b ? -1 : 1;

            i++;
            j++;
        }

        return (x.Length - i).CompareTo(y.Length - j);
    }

    private static int CountLeadingZeros(string s, int start, int end)
    {
        var count = 0;
        for (var k = start; k < end && s[k] == '0'; k++)
            count++;
        return count;
    }
}