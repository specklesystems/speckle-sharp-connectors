using Speckle.InterfaceGenerator;
using Speckle.Sdk;
using Speckle.Sdk.Host;

namespace Speckle.Connectors.Common.Threading;

[GenerateAutoInterface]
public class ThreadOptions(ISpeckleApplication speckleApplication) : IThreadOptions
{
  public bool RunReceiveBuildOnMainThread => speckleApplication.HostApplication != HostApplications.Rhino.Name;
  public bool RunCommandsOnMainThread => speckleApplication.HostApplication == HostApplications.ArcGIS.Name;
}
