namespace Okey101.Api.Services;

public interface ITenantProvider
{
    Guid? TenantId { get; }
    void SetTenantId(Guid? tenantId);
}
