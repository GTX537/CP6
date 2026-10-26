using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
namespace CP6.Core.Services.Erp;

public partial class PlateMoldService
{
    private readonly PlateMoldWriteOwnerV1? _writeOwner;
    private PlateMoldWriteOwnerV1 WriteOwner=>_writeOwner?.Owns(_db)==true?_writeOwner:throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE",503);
    internal static PlateMoldDto NativeDto(PlateMold value)=>EntityToDto(value);
    internal static PlateMold NativeCreate(PlateMoldDto dto,string number,int revision)
    {
        ValidateBeforeSave(dto);
        var copy=CP6.Core.Services.Integration.OperationsOwnerV1.Freeze(dto);
        if(string.IsNullOrEmpty(copy.TypeClass) && !string.IsNullOrEmpty(copy.ProcessCd))copy.TypeClass=AutoTypeClassFromProcess(copy.ProcessCd);
        var value=DtoToEntity(copy);value.WdPtnNo=number;value.WdRev=revision;ValidateNative(value);return value;
    }
    internal static void NativeApply(PlateMoldDto dto,PlateMold value)
    {ValidateBeforeSave(dto);ApplyDtoToEntity(dto,value);ValidateNative(value);}
    private static void ValidateNative(PlateMold value)
    {
        // Preserve the original field domains; reject values that PostgreSQL numeric(15,4) would round.
        var results=new List<ValidationResult>();
        if(!Validator.TryValidateObject(value,new ValidationContext(value),results,true))
            throw new OrderPriceCorrectionException("TOOL_FIELD_INVALID",400);
        foreach(var p in typeof(PlateMold).GetProperties().Where(p=>p.PropertyType==typeof(decimal) || p.PropertyType==typeof(decimal?)))
            if(p.GetValue(value) is decimal n && (Math.Abs(n)>99999999999.9999m || decimal.Round(n,4)!=n))
                throw new OrderPriceCorrectionException("TOOL_DECIMAL_INVALID",400);
    }
    private static PlateMoldOrderLinkResultDto Link(PlateMoldWriteResultV1 result)=>new()
        {WebOrderNo=result.Order.Number,WebOrderDetailNo=result.Order.LineNumber,ArrangeNo=result.Order.ArrangeNumber};
    private static PlateMoldPeApiResultDto Pe()=>new(){Ok=false,Message="NOT_EXECUTED: PE delivery has not been executed."};
    private async Task<(string,int,PlateMoldOrderLinkResultDto,PlateMoldPeApiResultDto)> CreateNativeOwned(PlateMoldDto dto)
    {var result=await WriteOwner.ExecuteAsync("CREATE",null,1,dto);return(result.Number,result.Revision,Link(result),Pe());}
    private async Task<(int,PlateMoldOrderLinkResultDto,PlateMoldPeApiResultDto)> ReviseNativeOwned(string number,PlateMoldDto dto)
    {var result=await WriteOwner.ExecuteAsync("REVISE",number,dto.WdRev,dto);return(result.Revision,Link(result),Pe());}
    public async Task DeleteVersionedAsync(string number,int revision,PlateMoldDto dto,string? userName)
    {
        if(_db.Database.IsNpgsql())await WriteOwner.ExecuteAsync("DELETE",number,revision,dto);
        else await DeleteAsync(number,revision,dto.RowVersion,userName);
    }
}
