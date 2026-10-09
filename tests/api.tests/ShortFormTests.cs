using Expenses.Api.Domain;

namespace Expenses.Api.Tests;

public class ShortFormTests
{
    [Theory]
    [InlineData("Kirkland Signature Organic Eggs, 24 ct", "KIRKL SIGNA ORGAN EGGS")]
    [InlineData("Milk", "MILK")]
    [InlineData("banana", "BANAN")]            // a single long word is abbreviated
    [InlineData("A/B C", "A B C")]             // punctuation becomes a separator
    [InlineData("   ", "")]                     // nothing usable
    public void FromFullName_ProducesReceiptStyleShortForm(string input, string expected)
    {
        Assert.Equal(expected, ShortForm.FromFullName(input));
    }

    [Fact]
    public void FromFullName_NeverExceedsReceiptLength()
    {
        var result = ShortForm.FromFullName("Supercalifragilisticexpialidocious deluxe premium edition bundle");
        Assert.True(result.Length <= 24, $"'{result}' is longer than 24 characters");
    }
}
