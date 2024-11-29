using Speckle.Connectors.CSiShared;
using Speckle.Sdk.Host;

// NOTE: Plugin entry point must match the assembly name, otherwise ETABS hits you with a "Not found" error when loading plugin
// Disabling error below to prioritize DUI3 project structure. Name of cPlugin class cannot be changed
#pragma warning disable IDE0130
namespace Speckle.Connectors.ETABS22;

public class SpeckleForm : SpeckleFormBase
{
  protected override HostAppVersion GetVersion() => HostAppVersion.v2021;
}
