using System;
using System.Security.Cryptography;
using System.Text;

namespace Ecomerce_dotnet.Services
{
    /// <summary>
    /// Cryptographically secure random password generator utility.
    /// Generates high-entropy temporary passwords meeting complexity requirements:
    /// uppercase letters, lowercase letters, digits, and special characters.
    /// </summary>
    public static class PasswordGenerator
    {
        private const string UpperChars = "ABCDEFGHJKLMNOPQRSTUVWXYZ"; // excluded I/O to avoid visual confusion
        private const string LowerChars = "abcdefghijkmnopqrstuvwxyz"; // excluded l to avoid visual confusion
        private const string DigitChars = "23456789";                  // excluded 0, 1
        private const string SpecialChars = "!@#$%^&*()-_=+[]{}";

        public static string GenerateTemporaryPassword(int length = 12)
        {
            if (length < 8)
            {
                throw new ArgumentException("Password length must be at least 8 characters.", nameof(length));
            }

            var passwordChars = new char[length];
            var byteBuffer = new byte[4];

            // 1. Guarantee at least one character from each character category
            passwordChars[0] = GetRandomChar(UpperChars);
            passwordChars[1] = GetRandomChar(LowerChars);
            passwordChars[2] = GetRandomChar(DigitChars);
            passwordChars[3] = GetRandomChar(SpecialChars);

            // 2. Fill the remaining positions with characters from the combined pool
            var combinedPool = UpperChars + LowerChars + DigitChars + SpecialChars;
            for (int i = 4; i < length; i++)
            {
                passwordChars[i] = GetRandomChar(combinedPool);
            }

            // 3. Cryptographically shuffle the characters using Fisher-Yates algorithm
            using var rng = RandomNumberGenerator.Create();
            for (int i = length - 1; i > 0; i--)
            {
                int j = RandomNumberGenerator.GetInt32(0, i + 1);
                (passwordChars[i], passwordChars[j]) = (passwordChars[j], passwordChars[i]);
            }

            return new string(passwordChars);
        }

        private static char GetRandomChar(string charSet)
        {
            int index = RandomNumberGenerator.GetInt32(0, charSet.Length);
            return charSet[index];
        }
    }
}
