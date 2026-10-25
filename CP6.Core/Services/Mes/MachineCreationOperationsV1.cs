using CP6.Entity.DTOs.Mes;

namespace CP6.Core.Services.Mes;

public sealed record MachineCreationDepartmentV1(Guid Id,string Code,string Name);
public sealed record MachineCreationScopeV1(Guid TenantId,Guid ActorId);
public sealed record MachineCreationContextV1(Guid TenantId,Guid ActorId,string AuthorityVersion,bool AllowsUnbound,
    MachineCreationDepartmentV1[] Departments);
public sealed record MachineCreationCommandV1
{
    public Guid OperationId {get;init;}
    public string ExpectedAuthorityVersion {get;init;}="";
    public Guid? DepartmentId {get;init;}
    public MachineDto Machine {get;init;}=new();
}
public sealed record MachineCreationResultV1(Guid OperationId,Guid MachineId,string MachineCd,Guid? DepartmentId);
public sealed record MachineCreationOperationV1(Guid OperationId,string Status,MachineCreationResultV1? Result=null);
public interface IMachineCreationOperationsV1
{
    Task<MachineCreationContextV1> CreationContextAsync(CancellationToken ct=default);
    Task<MachineCreationScopeV1> CreationScopeAsync(CancellationToken ct=default);
    Task<MachineCreationResultV1> CreateCommandAsync(MachineCreationCommandV1 request,CancellationToken ct=default);
    Task<MachineCreationOperationV1> ReadCreationAsync(Guid operation,CancellationToken ct=default);
    Task<MachineCreationOperationV1> CloseCreationAsync(Guid operation,CancellationToken ct=default);
}
