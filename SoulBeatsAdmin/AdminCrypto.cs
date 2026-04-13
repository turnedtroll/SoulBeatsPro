using System.Security.Cryptography;
using System.Text;

namespace SoulBeatsAdmin;

/// <summary>
/// AES-256 encryption for admin config files.
/// Must match the decryption logic in the macro client (SoulBeatsPro.AdminCrypto).
/// </summary>
internal static class AdminCrypto
{
    private static byte[] DeriveKey()
    {
        var parts = new[] { "SoulB", "eats", "Pro", "#Adm", "in@", "2024", "!Sec", "ure" };
        var combined = string.Concat(parts);
        var salt = Encoding.UTF8.GetBytes("SBP_AdminConfig_Salt_v1");
        return Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(combined), salt, 100_000, HashAlgorithmName.SHA256, 32);
    }

    public static byte[] Encrypt(string plainText)
    {
        var key = DeriveKey();
        using var aes = Aes.Create();
        aes.Key = key;
        aes.GenerateIV();

        using var ms = new MemoryStream();
        ms.Write(aes.IV);

        using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
        using (var sw = new StreamWriter(cs, Encoding.UTF8))
        {
            sw.Write(plainText);
        }

        return ms.ToArray();
    }

    public static string Decrypt(byte[] cipherData)
    {
        var key = DeriveKey();
        using var aes = Aes.Create();
        aes.Key = key;

        var iv = new byte[16];
        Array.Copy(cipherData, 0, iv, 0, 16);
        aes.IV = iv;

        using var ms = new MemoryStream(cipherData, 16, cipherData.Length - 16);
        using var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Read);
        using var sr = new StreamReader(cs, Encoding.UTF8);
        return sr.ReadToEnd();
    }
}
