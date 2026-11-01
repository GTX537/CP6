using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
namespace CP6.Core.Services.Mes.Scheduling;
public sealed class SchedInstantConverterV1:JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader,Type type,JsonSerializerOptions options)
    {
        if(reader.TokenType!=JsonTokenType.String)throw new JsonException("SCHED_OFFSET_TIME_REQUIRED");var value=reader.GetString()!;
        if(!Regex.IsMatch(value,@"\A[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]{1,6})?(?:Z|[+-][0-9]{2}:[0-9]{2})\z")
            ||!DateTimeOffset.TryParse(value,CultureInfo.InvariantCulture,DateTimeStyles.None,out var result))throw new JsonException("SCHED_OFFSET_TIME_REQUIRED");return result;
    }
    public override void Write(Utf8JsonWriter writer,DateTimeOffset value,JsonSerializerOptions options)
    {if(value.Ticks%10!=0)throw new JsonException("SCHED_MICROSECOND_PRECISION_REQUIRED");writer.WriteStringValue(value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'",CultureInfo.InvariantCulture));}
}
