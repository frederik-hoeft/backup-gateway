namespace BackupGateway.Web.Data.Model;

public enum BackupLeaseState
{
    Unknown = 0,
    Held = 1,
    Released = 2,
    ForceReleased = 3,
}
