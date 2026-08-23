namespace BackupGateway.Web.Data.Model;

public enum TargetLifecycleState
{
    Unknown = 0,
    Offline = 1,
    Starting = 2,
    Online = 3,
    Stopping = 4,
    Faulted = 5,
}
