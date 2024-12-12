using Moq;
using Speckle.Converters.RevitShared.Services;
using Speckle.HostApps;

namespace Speckle.Converters.Revit2023.Tests;

public static class MoqTestExtensions
{
  public static Mock<IScalingServiceToSpeckle> CreateScalingService(this MoqTest test)  =>
    test.Repository.Create<IScalingServiceToSpeckle>();
}
