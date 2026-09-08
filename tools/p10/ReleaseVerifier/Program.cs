using System.Text.Json;
using CP6.P10.ReleaseVerifier;
using CP6.Platform.Release;

try
{
    if (args is ["canonicalize", var inputPath, var outputPath])
    {
        var bytes = Cp6DeterministicJson.Canonicalize(ContractInspection.ReadBounded(inputPath));
        using var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        output.Write(bytes);
        Console.WriteLine(Cp6DeterministicJson.Sha256Hex(bytes));
        return 0;
    }
    if (args is ["inspect", var schemaId, var expectedHash, var path])
    {
        var result = ContractInspection.Inspect(ContractInspection.ReadBounded(path), schemaId, expectedHash);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(result,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        Console.WriteLine(System.Text.Encoding.UTF8.GetString(Cp6DeterministicJson.Canonicalize(bytes)));
        return 0;
    }
    if (ReadOnlyVerificationCommand.Matches(args))
    {
        var bytes = await ReadOnlyVerificationCommand.ExecuteAsync(args);
        Console.WriteLine(System.Text.Encoding.UTF8.GetString(bytes));
        return 0;
    }
    if (ValidationCommand.Matches(args))
    {
        var bytes = await ValidationCommand.ExecuteAsync(args);
        Console.WriteLine(System.Text.Encoding.UTF8.GetString(bytes));
        return 0;
    }
    if (PublicationCommand.Matches(args))
    {
        var bytes = await PublicationCommand.ExecuteAsync(args);
        Console.WriteLine(System.Text.Encoding.UTF8.GetString(bytes));
        return 0;
    }
    Console.Error.WriteLine("usage: canonicalize INPUT NEW_OUTPUT | inspect SCHEMA_ID EXPECTED_SHA256 INPUT | " +
        "verify-platform TAG | confirm-platform-intent TAG ARTIFACT_ID | confirm-platform-published TAG | " +
        "prepare-validation NEW_STAGE | finalize-validation STAGE IMAGE_INPUTS NEW_ARTIFACT | " +
        "prepare-publication TAG VALIDATION_RUN VALIDATION_ATTEMPT VALIDATION_ARTIFACT NEW_STAGE | " +
        "store-publication-bundle TAG SIGNED_STAGE NEW_INTENT | commit-publication TAG INTENT_ARTIFACT");
    return 2;
}
catch (Cp6ReleaseContractException error)
{
    Console.Error.WriteLine(error.Code);
    return 1;
}
catch (Exception)
{
    Console.Error.WriteLine("inspection-io");
    return 1;
}
