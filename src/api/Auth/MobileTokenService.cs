using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Expenses.Api.Data;
using Expenses.Api.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Expenses.Api.Auth;

// Issues and validates tokens for native mobile sessions, without any signing secret:
// - Access token: a short-lived, self-contained string protected by ASP.NET Data Protection (its key
//   is held in Key Vault in prod), so validating it needs no database round-trip.
// - Refresh token: a long-lived opaque random value; only its SHA-256 hash is persisted, and it rotates
//   (old one revoked) on every refresh so a captured refresh token is quickly useless.
public class MobileTokenService(IDataProtectionProvider dataProtection, ExpensesDbContext db, TimeProvider clock)
{
    public const string BearerScheme = "Bearer";
    public static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromHours(1);
    public static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(30);

    private const char Separator = '\u001f'; // unit separator: cannot appear in names/emails
    private readonly IDataProtector _protector = dataProtection.CreateProtector("Expenses.Mobile.AccessToken.v1");

    public (string token, DateTimeOffset expiresAt) CreateAccessToken(Member member)
    {
        var expiresAt = clock.GetUtcNow().Add(AccessTokenLifetime);
        var payload = string.Join(Separator,
            member.Id,
            member.HouseholdId,
            member.Role,
            member.DisplayName ?? string.Empty,
            member.Email ?? string.Empty,
            expiresAt.ToUnixTimeSeconds());
        return (_protector.Protect(payload), expiresAt);
    }

    public ClaimsPrincipal? ValidateAccessToken(string token)
    {
        string payload;
        try
        {
            payload = _protector.Unprotect(token);
        }
        catch (CryptographicException)
        {
            return null; // tampered, wrong key, or not one of our tokens
        }

        var parts = payload.Split(Separator);
        if (parts.Length != 6 || !long.TryParse(parts[5], out var expiryUnix))
        {
            return null;
        }
        if (DateTimeOffset.FromUnixTimeSeconds(expiryUnix) <= clock.GetUtcNow())
        {
            return null; // expired
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, parts[0]),
            new(AppClaims.HouseholdId, parts[1]),
            new(AppClaims.Role, parts[2]),
            new(ClaimTypes.Name, parts[3]),
        };
        if (!string.IsNullOrEmpty(parts[4]))
        {
            claims.Add(new Claim(ClaimTypes.Email, parts[4]));
        }
        return new ClaimsPrincipal(new ClaimsIdentity(claims, BearerScheme));
    }

    public async Task<string> IssueRefreshTokenAsync(Guid memberId, CancellationToken ct = default)
    {
        var raw = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        db.MobileRefreshTokens.Add(new MobileRefreshToken
        {
            Id = Guid.NewGuid(),
            MemberId = memberId,
            TokenHash = Hash(raw),
            ExpiresAt = clock.GetUtcNow().Add(RefreshTokenLifetime),
        });
        await db.SaveChangesAsync(ct);
        return raw;
    }

    // Validates a refresh token and rotates it (revokes the presented one). Returns the owning member, or
    // null when the token is unknown, already used/revoked, or expired.
    public async Task<Member?> ConsumeRefreshTokenAsync(string refreshToken, CancellationToken ct = default)
    {
        var hash = Hash(refreshToken);
        var stored = await db.MobileRefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (stored is null || stored.RevokedAt is not null || stored.ExpiresAt <= clock.GetUtcNow())
        {
            return null;
        }

        stored.RevokedAt = clock.GetUtcNow();
        var member = await db.Members.FirstOrDefaultAsync(m => m.Id == stored.MemberId, ct);
        await db.SaveChangesAsync(ct);
        return member;
    }

    public async Task RevokeRefreshTokenAsync(string refreshToken, CancellationToken ct = default)
    {
        var hash = Hash(refreshToken);
        var stored = await db.MobileRefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash && t.RevokedAt == null, ct);
        if (stored is not null)
        {
            stored.RevokedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
        }
    }

    private static string Hash(string token) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
