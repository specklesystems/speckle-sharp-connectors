using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Converters.CSiShared.ToSpeckle.Raw;
using Speckle.Converters.ETABSShared.ToSpeckle.Raw;
using Speckle.Sdk;

namespace Speckle.Converters.ETABSShared;

public static class ServiceRegistration
{
  public static IServiceCollection AddEtabsConverters(this IServiceCollection serviceCollection)
  {
    var converterAssembly = Assembly.GetExecutingAssembly();

    serviceCollection.AddTransient<CsiFrameToSpeckleConverter, FrameToSpeckleConverter>();
    serviceCollection.AddTransient<CsiJointToSpeckleConverter, JointToSpeckleConverter>();
    serviceCollection.AddTransient<CsiShellToSpeckleConverter, ShellToSpeckleConverter>();

    serviceCollection.AddMatchingInterfacesAsTransient(converterAssembly);

    return serviceCollection;
  }
}
