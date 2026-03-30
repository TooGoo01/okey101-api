using Okey101.Api.Models.Enums;

namespace Okey101.Api.Models.Entities;

public class Table
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public int TableNumber { get; set; }
    public TableStatus Status { get; set; } = TableStatus.Active;
    public string QrCodeIdentifier { get; set; } = string.Empty;
    public Guid GameCenterId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public GameCenter GameCenter { get; set; } = null!;
}
