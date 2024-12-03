using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Converters.CSiShared.ToSpeckle.Raw;
using Speckle.Converters.ETABSShared.ToSpeckle.Raw;
using Speckle.Sdk;

namespace Speckle.Converters.ETABSShared;

public static class ServiceRegistration
{
  public static IServiceCollection AddETABSConverters(this IServiceCollection serviceCollection)
  {
    var converterAssembly = Assembly.GetExecutingAssembly();

    serviceCollection.AddTransient<CSiFrameToSpeckleConverter, FrameToSpeckleConverter>();
    //serviceCollection.AddTransient<CSiJointToSpeckleConverter, JointToSpeckleConverter>();
    //serviceCollection.AddTransient<CSiShellToSpeckleConverter, ShellToSpeckleConverter>();

    serviceCollection.AddMatchingInterfacesAsTransient(converterAssembly);

    return serviceCollection;
  }
}
