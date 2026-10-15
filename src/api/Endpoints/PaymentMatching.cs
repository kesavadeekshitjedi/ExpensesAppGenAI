using System.Text.RegularExpressions;

namespace Expenses.Api.Endpoints;

// Suggests which household payment method a receipt/invoice was paid with, by reading the card details
// printed on it: the last four digits of the card number (matched against the payment method's label)
// and the card brand (e.g. "Costco Visa", "Discover"). This is only a suggestion — the user confirms
// the payment method before saving. A Costco receipt that prints "COSTCO VISA ... XXXX7039" therefore
// defaults to a method labelled e.g. "Costco Citi 7039".
public static partial class PaymentMatching
{
    public record PaymentRef(Guid Id, string Label);

    // Brand words we look for on the document AND in the method label. "Costco"/"Citi" let the Costco
    // co-brand card win even when no last-4 is printed; the generic brands help the common case.
    private static readonly string[] BrandKeywords =
        ["Costco", "Citi", "Visa", "Mastercard", "Amex", "American Express", "Discover"];

    // Scores each payment method against the text: +100 if its label contains the printed last-4,
    // +10 per brand word shared with the document. The highest positive score wins; null if nothing matches.
    public static Guid? Suggest(IReadOnlyList<PaymentRef> methods, string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || methods.Count == 0)
        {
            return null;
        }

        var last4 = Last4Regex().Match(text) is { Success: true } m ? m.Groups[1].Value : null;
        var brands = BrandKeywords.Where(b => text.Contains(b, StringComparison.OrdinalIgnoreCase)).ToArray();

        PaymentRef? best = null;
        var bestScore = 0;
        foreach (var method in methods)
        {
            var score = 0;
            if (last4 is not null && method.Label.Contains(last4, StringComparison.OrdinalIgnoreCase))
            {
                score += 100;
            }
            score += brands.Count(b => method.Label.Contains(b, StringComparison.OrdinalIgnoreCase)) * 10;
            if (score > bestScore)
            {
                bestScore = score;
                best = method;
            }
        }
        return best?.Id;
    }

    // The last four digits of a masked card number: a run of mask characters (*, x, X, or bullets)
    // immediately before four digits (e.g. "************7039", "***8654"), or an "ending in 1234" phrase.
    // Pure digit runs (phone numbers, member ids) are not matched because a mask prefix is required.
    [GeneratedRegex(@"(?:(?:[\*xX•●]\s*){3,}|ending(?:\s+in)?\s+)(\d{4})\b", RegexOptions.IgnoreCase)]
    private static partial Regex Last4Regex();
}
