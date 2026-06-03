using System.Security.Cryptography;
using DataBuilder.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DataBuilder.Core.Services;

public class EncryptionService : IEncryptionService
{
    private readonly byte[] _key;
    private readonly ILogger<EncryptionService> _logger;

    public EncryptionService(IConfiguration configuration, ILogger<EncryptionService> logger)
    {
        _logger = logger;
        _key = LoadOrCreateKey(configuration);
    }

    public string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            throw new ArgumentException("明文不能为空", nameof(plainText));

        using var aes = Aes.Create();
        aes.Key = _key;
        aes.GenerateIV();
        var iv = aes.IV;

        using var encryptor = aes.CreateEncryptor();
        var plainBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        // 返回 base64(IV + ciphertext)
        var result = new byte[iv.Length + cipherBytes.Length];
        Buffer.BlockCopy(iv, 0, result, 0, iv.Length);
        Buffer.BlockCopy(cipherBytes, 0, result, iv.Length, cipherBytes.Length);

        return Convert.ToBase64String(result);
    }

    public string Decrypt(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText))
            throw new ArgumentException("密文不能为空", nameof(cipherText));

        try
        {
            var fullCipher = Convert.FromBase64String(cipherText);

            using var aes = Aes.Create();
            aes.Key = _key;

            var iv = new byte[16];
            Buffer.BlockCopy(fullCipher, 0, iv, 0, iv.Length);
            aes.IV = iv;

            var cipherBytes = new byte[fullCipher.Length - iv.Length];
            Buffer.BlockCopy(fullCipher, iv.Length, cipherBytes, 0, cipherBytes.Length);

            using var decryptor = aes.CreateDecryptor();
            var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);

            return System.Text.Encoding.UTF8.GetString(plainBytes);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("API Key 密文格式无效，请重新配置模型。", ex);
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException("API Key 解密失败，加密密钥可能已变更。请重新配置模型的 API Key。", ex);
        }
    }

    private byte[] LoadOrCreateKey(IConfiguration configuration)
    {
        // 优先级 1: appsettings.json 中的 Encryption:Key
        var keyBase64 = configuration["Encryption:Key"];
        if (!string.IsNullOrWhiteSpace(keyBase64))
        {
            _logger.LogInformation("使用 appsettings.json 中配置的 Encryption:Key");
            return ValidateAndConvertKey(keyBase64);
        }

        // 优先级 2: 本地 .encryption-key 文件
        var keyFilePath = Path.Combine(Directory.GetCurrentDirectory(), ".encryption-key");
        if (File.Exists(keyFilePath))
        {
            _logger.LogInformation("从文件加载加密密钥: {KeyFilePath}", keyFilePath);
            var fileKey = File.ReadAllText(keyFilePath).Trim();
            return ValidateAndConvertKey(fileKey);
        }

        // 优先级 3: 生成新密钥并持久化到文件
        var newKey = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(newKey);
        var newKeyBase64 = Convert.ToBase64String(newKey);

        File.WriteAllText(keyFilePath, newKeyBase64);
        _logger.LogInformation("已生成新加密密钥并保存到: {KeyFilePath}", keyFilePath);

        return newKey;
    }

    private byte[] ValidateAndConvertKey(string base64Key)
    {
        var key = Convert.FromBase64String(base64Key);
        if (key.Length != 32)
        {
            throw new InvalidOperationException(
                $"加密密钥长度不正确（{key.Length} 字节），预期 32 字节 (AES-256)。请检查密钥配置。");
        }
        return key;
    }
}
