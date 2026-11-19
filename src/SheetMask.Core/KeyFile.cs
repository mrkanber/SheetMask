using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SheetMask.Core;

/// <summary>
/// Password-protected key file (.smkey): "SMK1" | iterations (int32) | salt (16) | nonce (12) | tag (16) | ciphertext.
/// Key derivation PBKDF2-SHA256, encryption AES-256-GCM, payload gzip-compressed JSON.
/// </summary>
public static class KeyFile
{
    private static readonly byte[] Magic = "SMK1"u8.ToArray();
    private const int Iterations = 600_000;

    private static readonly JsonSerializerOptions Json = new()
    {
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = false
    };

    public static void Save(string path, MaskKey key, string password)
    {
        var plain = Compress(JsonSerializer.SerializeToUtf8Bytes(key, Json));
        var salt = RandomNumberGenerator.GetBytes(16);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var cipher = new byte[plain.Length];
        using (var aes = new AesGcm(Derive(password, salt, Iterations), 16))
            aes.Encrypt(nonce, plain, cipher, tag, Magic);

        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);
        w.Write(Magic);
        w.Write(Iterations);
        w.Write(salt);
        w.Write(nonce);
        w.Write(tag);
        w.Write(cipher);
    }

    /// <exception cref="InvalidDataException">Not a key file.</exception>
    /// <exception cref="UnauthorizedAccessException">Wrong password or damaged file.</exception>
    public static MaskKey Load(string path, string password)
    {
        var all = File.ReadAllBytes(path);
        if (all.Length < 4 + 4 + 16 + 12 + 16 || !all.AsSpan(0, 4).SequenceEqual(Magic))
            throw new InvalidDataException("This is not a SheetMask key file.");
        var iterations = BitConverter.ToInt32(all, 4);
        var salt = all.AsSpan(8, 16).ToArray();
        var nonce = all.AsSpan(24, 12).ToArray();
        var tag = all.AsSpan(36, 16).ToArray();
        var cipher = all.AsSpan(52).ToArray();
        var plain = new byte[cipher.Length];
        try
        {
            using var aes = new AesGcm(Derive(password, salt, iterations), 16);
            aes.Decrypt(nonce, cipher, tag, plain, Magic);
        }
        catch (CryptographicException)
        {
            throw new UnauthorizedAccessException("Wrong password, or the key file is damaged.");
        }
        return JsonSerializer.Deserialize<MaskKey>(Decompress(plain), Json)
               ?? throw new InvalidDataException("Empty key file.");
    }

    private static byte[] Derive(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, 32);

    private static byte[] Compress(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal)) gz.Write(data);
        return ms.ToArray();
    }

    private static byte[] Decompress(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var gz = new GZipStream(input, CompressionMode.Decompress);
        using var ms = new MemoryStream();
        gz.CopyTo(ms);
        return ms.ToArray();
    }
}
