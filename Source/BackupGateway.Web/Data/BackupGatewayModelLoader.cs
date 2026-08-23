using Wkg.EntityFrameworkCore.Configuration;
using Wkg.EntityFrameworkCore.Discovery.SourceGeneration;

namespace BackupGateway.Web.Data;

[ModelLoader(AssemblyDiscoveryFailureBehavior = AssemblyDiscoveryFailureBehavior.Error, TargetAssemblies = ["BackupGateway.Web"])]
internal sealed partial class BackupGatewayModelLoader;
