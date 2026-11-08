using CP6.Core.Services.Platform;
namespace CP6.WebApi.Configuration;
public static class PrivacyRestoreConfigurationV1
{
    public static PrivacyRestoreOptionsV1 Bind(IConfiguration configuration)
    {
        var section=configuration.GetSection("PrivacyRestore");var result=new PrivacyRestoreOptionsV1{ControlConnectionString=section["ControlConnectionString"]??""};
        // ConfigurationBinder does not bind Guid dictionary keys; parse the trusted registration boundary explicitly.
        foreach(var child in section.GetSection("Candidates").GetChildren())
        {
            if(!Guid.TryParseExact(child.Key,"D",out var id)||id==Guid.Empty||!result.Candidates.TryAdd(id,child.Get<PrivacyRestoreCandidateOptionsV1>()??throw new InvalidOperationException("PRIVACY_RESTORE_REGISTRATION_REQUIRED")))
                throw new InvalidOperationException("PRIVACY_RESTORE_REGISTRATION_INVALID");
            var item=result.Candidates[id];
            if(item.RegistrationVersion==Guid.Empty||string.IsNullOrWhiteSpace(item.ConnectionString)||string.IsNullOrWhiteSpace(item.DisplayName)
                ||item.BackupSha256.Length!=64||item.BackupSha256.Any(c=>!Uri.IsHexDigit(c)))throw new InvalidOperationException("PRIVACY_RESTORE_REGISTRATION_INVALID");
            item.BackupSha256=item.BackupSha256.ToLowerInvariant();
        }
        return result;
    }
}
