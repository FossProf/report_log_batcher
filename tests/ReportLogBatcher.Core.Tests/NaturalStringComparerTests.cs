using ReportLogBatcher.Core.Services;

namespace ReportLogBatcher.Core.Tests;

public sealed class NaturalStringComparerTests
{
    [Fact]
    public void OrdersNumericSuffixAscending()
    {
        var input = new[] { "SPIN 10.docx", "SPIN 2.docx", "SPIN 1.docx" };

        var sorted = input.OrderBy(x => x, NaturalStringComparer.Instance).ToArray();

        Assert.Equal(new[] { "SPIN 1.docx", "SPIN 2.docx", "SPIN 10.docx" }, sorted);
    }

    [Fact]
    public void ComparesNumbersByValueNotDigitCount()
    {
        var input = new[] { "SPIN 9.docx", "SPIN 100.docx", "SPIN 10.docx" };

        var sorted = input.OrderBy(x => x, NaturalStringComparer.Instance).ToArray();

        Assert.Equal(new[] { "SPIN 9.docx", "SPIN 10.docx", "SPIN 100.docx" }, sorted);
    }

    [Fact]
    public void IsCaseInsensitive()
    {
        var input = new[] { "ABC 10.docx", "abc 2.docx" };

        var sorted = input.OrderBy(x => x, NaturalStringComparer.Instance).ToArray();

        Assert.Equal(new[] { "abc 2.docx", "ABC 10.docx" }, sorted);
    }

    [Fact]
    public void EqualNaturalValue_UsesOrdinalTieBreak()
    {
        var input = new[] { "spin 1.docx", "SPIN 1.docx" };

        var sorted = input.OrderBy(x => x, NaturalStringComparer.Instance).ToArray();

        Assert.Equal(new[] { "SPIN 1.docx", "spin 1.docx" }, sorted);
    }

    [Fact]
    public void NullComparisons_AreDeterministic()
    {
        Assert.True(NaturalStringComparer.Instance.Compare(null, null) == 0);
        Assert.True(NaturalStringComparer.Instance.Compare(null, "a") < 0);
        Assert.True(NaturalStringComparer.Instance.Compare("a", null) > 0);
    }
}