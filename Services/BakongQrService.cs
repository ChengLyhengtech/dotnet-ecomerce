using System.Security.Cryptography;
using System.Text;

namespace Ecomerce_dotnet.Services
{
    public class BakongQrService
    {
        // Computes the standard MD5 Hash that Bakong relies on for lookup tracking
        public string ComputeMd5Hash(string rawQrString)
        {
            byte[] inputBytes = Encoding.UTF8.GetBytes(rawQrString);
            byte[] hashBytes = MD5.HashData(inputBytes);

            return Convert.ToHexString(hashBytes).ToLower();
        }

        // Standard EMVCo format assembler for Bakong individual account specifications
        public string GenerateIndividualQrString(string username, string name, string city, decimal amount, string currency)
        {
            var currencyCode = currency.ToUpper() == "USD" ? "840" : "116";

            var sb = new StringBuilder();
            sb.Append("000201"); // Payload Format Indicator
            sb.Append("010212"); // Point of Initiation (12 = Dynamic QR)

            // Merchant Account Information (Tag 29)
            var subTag00 = $"00{username.Length:D2}{username}";
            var tag29Data = $"{subTag00}";
            sb.Append($"29{tag29Data.Length:D2}{tag29Data}");

            sb.Append("52040000"); // Merchant Category Code
            sb.Append($"5303{currencyCode}"); // Currency

            var amountStr = amount.ToString("F2");
            sb.Append($"54{amountStr.Length:D2}{amountStr}"); // Amount

            sb.Append("5802KH"); // Country Code
            sb.Append($"59{name.Length:D2}{name}"); // Account Name
            sb.Append($"60{city.Length:D2}{city}"); // City

            // Calculate standard EMVCo CRC16 Checksum
            string currentQr = sb.ToString() + "6304";
            ushort crc = ComputeCrc16(Encoding.UTF8.GetBytes(currentQr));

            return currentQr + crc.ToString("X4");
        }

        private ushort ComputeCrc16(byte[] bytes)
        {
            ushort crc = 0xFFFF;
            foreach (byte b in bytes)
            {
                crc ^= (ushort)(b << 8);
                for (int i = 0; i < 8; i++)
                {
                    if ((crc & 0x8000) != 0)
                        crc = (ushort)((crc << 1) ^ 0x1021);
                    else
                        crc <<= 1;
                }
            }
            return crc;
        }
    }
}
