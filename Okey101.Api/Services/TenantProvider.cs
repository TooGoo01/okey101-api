namespace Okey101.Api.Services;

public class TenantProvider : ITenantProvider
{
    private static readonly AsyncLocal<Guid?> _currentTenantId = new();

    public Guid? TenantId => _currentTenantId.Value;

    public void SetTenantId(Guid? tenantId)
    {
        _currentTenantId.Value = tenantId;
    }
}
