namespace BackupGateway.Web.Services.Targets;

/// <summary>
/// Immutable lookup of operational target definitions loaded from application configuration.
/// </summary>
public interface ITargetCatalog
{
    /// <summary>
    /// Gets all configured target definitions.
    /// </summary>
    IReadOnlyCollection<TargetDefinition> All { get; }

    /// <summary>
    /// Gets a configured target by its stable identifier.
    /// </summary>
    bool TryGet(string targetId, out TargetDefinition? target);
}
