namespace Expenses.Api.Domain;

// Turns a full item name into a receipt-style "short form", e.g.
//   "Kirkland Signature Organic Eggs, 24 ct" -> "KIRKL SIGNA ORGAN EGGS"
// This is how the app "figures out the short form" when the user doesn't give one during manual
// entry. It is deliberately simple and deterministic (no AI — SPEC decision #16): upper-case, drop
// punctuation, abbreviate long words, and keep whole words up to a receipt-like length.
public static class ShortForm
{
    private const int MaxWordLength = 5;
    private const int MaxTotalLength = 24;

    public static string FromFullName(string fullName)
    {
        var cleaned = new string((fullName ?? "").Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray());
        var words = cleaned.ToUpperInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Length > MaxWordLength ? w[..MaxWordLength] : w)
            .ToList();

        if (words.Count == 0)
        {
            return "";
        }

        var kept = new List<string>();
        var length = 0;
        foreach (var word in words)
        {
            var added = kept.Count == 0 ? word.Length : word.Length + 1; // +1 for the separating space
            if (length + added > MaxTotalLength)
            {
                break;
            }
            kept.Add(word);
            length += added;
        }

        // If even the first word is over the limit, keep a truncated form of it rather than nothing.
        if (kept.Count == 0)
        {
            return words[0][..Math.Min(words[0].Length, MaxTotalLength)];
        }

        return string.Join(' ', kept);
    }
}
