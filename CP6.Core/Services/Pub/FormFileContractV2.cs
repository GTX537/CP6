using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CP6.Core.Services.Sys;

namespace CP6.Core.Services.Pub;

// FORM references are a versioned content identity, never an arbitrary URL or an engineering parent.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FormFileScopeV2(string Kind,string? Key,Guid? Id,Guid FormVersionId,
    string? ExpectedVersion=null,string? ExpectedDataVersion=null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FormFileUploadV2(Guid OperationId,Guid FileId,FormFileScopeV2 Scope,string Field,
    string FileName,long ByteLength,string Digest);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FormFileReadV2(FormFileScopeV2 Scope,string Field,string Reference);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FormBoundFileReadV2(string Field,string Reference);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FormFileStateCommandV2(Guid OperationId,FormFileScopeV2 Scope,string Field,string Reference,
    string ExpectedStateVersion,string Action,string Reason);
public sealed record FormFileViewV2(string Reference,string FileName,FileContentRefV1 ContentRef,string StateVersion,
    FileInspectionV1 Inspection,bool Held,string Availability);
public sealed record FormFileOperationV2(Guid OperationId,string State,FormFileViewV2? Result=null);

internal static class FormFileReferenceV2
{
    internal const string Owner="Main.File.FormContent.v2",Contract="Main.File.FormContent.commands.v2";
    internal static string Encode(Guid id,string digest)=>$"cp6-form-file-v2/{id:D}/1/{digest}";
    internal static (Guid Id,string Digest) Parse(string text)
    {
        var match=Regex.Match(text,"\\Acp6-form-file-v2/([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})/1/([0-9a-f]{64})\\z");
        if(!match.Success||Guid.Parse(match.Groups[1].Value)==Guid.Empty)throw new ScopeAuthorizationExceptionV1("FORM_FILE_REFERENCE_INVALID",422);
        return(Guid.Parse(match.Groups[1].Value),match.Groups[2].Value);
    }
    internal static string[] Values(JsonElement value)
    {
        if(value.ValueKind==JsonValueKind.Null||value.ValueKind==JsonValueKind.String&&value.GetString()=="")return [];
        var items=value.ValueKind==JsonValueKind.Array?value.EnumerateArray().ToArray():[value];
        var result=new List<string>();
        foreach(var item in items)
        {
            if(item.ValueKind!=JsonValueKind.String)throw new ScopeAuthorizationExceptionV1("FORM_FILE_REFERENCE_INVALID",422);
            var text=item.GetString()!;Parse(text);result.Add(text);
        }
        if(result.Distinct(StringComparer.Ordinal).Count()!=result.Count)throw new ScopeAuthorizationExceptionV1("FORM_FILE_REFERENCE_DUPLICATE",422);
        return result.ToArray();
    }
    internal static bool Valid(JsonElement value){Values(value);return true;}
    internal static void Upload(FormFileUploadV2 value)
    {
        if(value.OperationId==Guid.Empty||value.FileId==Guid.Empty||value.Scope==null||string.IsNullOrEmpty(value.Field)||value.Field.Length>200
            ||value.ByteLength is <1 or >FileContentBytesV1.MaxBytes||value.Digest==null||!Regex.IsMatch(value.Digest,"\\A[0-9a-f]{64}\\z")
            ||string.IsNullOrWhiteSpace(value.FileName)||value.FileName.Length>255||value.FileName.Any(char.IsControl)||value.FileName.IndexOfAny(['/', '\\'])>=0)
            throw new ScopeAuthorizationExceptionV1("FILE_UPLOAD_INVALID",400);
    }
}
