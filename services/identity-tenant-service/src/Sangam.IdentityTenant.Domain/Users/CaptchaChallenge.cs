using System.Security.Cryptography;

namespace Sangam.IdentityTenant.Domain.Users;

/// <summary>
/// One CAPTCHA the sign-in form showed: the answer, as a hash, good once and
/// for five minutes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Self-hosted, confirmed with the user</b>: no third-party script on the
/// sign-in page, and nobody else learns who is signing in. The image is drawn
/// by this service (<c>ICaptchaRenderer</c>) and the answer never leaves it.
/// </para>
/// <para>
/// <b>Single use is the point.</b> A solved CAPTCHA that could be replayed
/// would cost a password-guessing script one solve for unlimited attempts, so
/// redeeming marks it used whether the answer was right or not - a wrong guess
/// spends it too, and the form asks for a new one. Not tenant-scoped: it is
/// answered before anybody has said who they are.
/// </para>
/// <para>
/// The alphabet leaves out 0/O, 1/I/L, 2/Z, 5/S and 8/B - the pairs a person
/// reading a distorted image confuses - and the answer is compared without case.
/// </para>
/// <para>
/// <b>The one typed value that meets <c>HashDeterministic</c>, knowingly.</b>
/// That hash is reserved for high-entropy secrets because an unsalted fast hash
/// of something guessable can be reversed. Here that costs nothing: the
/// challenge's own id is hashed with the answer, so nothing can be computed in
/// advance, and reversing one would reveal the letters of a picture that is
/// good for five minutes and one attempt - to somebody who can already read
/// the database. A slow salted hash would only make every sign-in slower.
/// </para>
/// </remarks>
public sealed class CaptchaChallenge
{
    public const string Alphabet = "ACDEFGHJKMNPQRTUVWXY34679";
    public const int Length = 5;
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    public Guid Id { get; private set; }
    public string AnswerHash { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? UsedAt { get; private set; }

    private CaptchaChallenge() { }   // EF Core

    public static (CaptchaChallenge Challenge, string Answer) Issue(
        Func<string, string> hashDeterministic, DateTimeOffset now)
    {
        var answer = new string([.. Enumerable.Range(0, Length)
            .Select(_ => Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)])]);

        var challenge = new CaptchaChallenge
        {
            Id = Guid.NewGuid(),
            CreatedAt = now,
            ExpiresAt = now.Add(Lifetime),
        };

        challenge.AnswerHash = hashDeterministic(Normalize(challenge.Id, answer));

        return (challenge, answer);
    }

    /// <summary>
    /// What is hashed: the id with the answer, so two challenges with the same
    /// five letters do not share a hash. Case and surrounding spaces are not
    /// the member's mistake to be refused for.
    /// </summary>
    public static string Normalize(Guid id, string answer) =>
        id.ToString("N") + ":" + answer.Trim().ToUpperInvariant();
}
