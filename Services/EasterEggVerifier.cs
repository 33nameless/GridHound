using System;
using System.Security.Cryptography;
namespace ObjectDetectTrackApp.Services;
internal static class EasterEggVerifier
{
    internal static bool Verify(string phrase)
    {
        if (!EasterEggSecret.IsConfigured || phrase.Length == 0 || phrase.Length > 256) return false;
        byte[]? actual = null;
        try
        {
            var salt = Convert.FromBase64String(EasterEggSecret.SaltBase64);
            var expected = Convert.FromBase64String(EasterEggSecret.HashBase64);
            if (salt.Length != 16 || expected.Length != 32 || EasterEggSecret.Iterations != 210000) return false;
            actual = Rfc2898DeriveBytes.Pbkdf2(phrase, salt, EasterEggSecret.Iterations, HashAlgorithmName.SHA256, 32);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException) { return false; }
        finally { if (actual != null) CryptographicOperations.ZeroMemory(actual); }
    }
}
