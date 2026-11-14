namespace Cmr.Api.Data;

public sealed class CurrentUser
{
    public Guid? UserId { get; set; }
    public Guid? TenantId { get; set; }
    public string Role { get; set; } = "";
    public Guid? DeviceId { get; set; }
    public bool IsOwner => Role == "owner";
    public bool IsAuthenticated => UserId is not null;
}
