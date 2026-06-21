namespace CP6.Core.Services.Sys;
public interface IPasswordHasher { string Hash(string plain); bool Verify(string plain, string hash); bool IsHashed(string value); }
