namespace CP6.Core.Services.Sys;

public class BCryptPasswordHasher : IPasswordHasher
{
    public string Hash(string plain) => BCrypt.Net.BCrypt.HashPassword(plain, workFactor: 11);
    public bool Verify(string plain, string hash) { try { return BCrypt.Net.BCrypt.Verify(plain, hash); } catch { return false; } }
    public bool IsHashed(string value)
        => !string.IsNullOrEmpty(value) && value.Length == 60
           && (value.StartsWith("$2a$") || value.StartsWith("$2b$") || value.StartsWith("$2y$"));
}
