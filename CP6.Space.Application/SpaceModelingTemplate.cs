namespace CP6.Space.Application;

public sealed record SpaceModelingTemplateFile(
    byte[] Content,
    string FileName,
    string ContentType,
    string SchemaVersion);

public interface ISpaceModelingTemplateService
{
    SpaceModelingTemplateFile CreateStandardExcelTemplate();
}
