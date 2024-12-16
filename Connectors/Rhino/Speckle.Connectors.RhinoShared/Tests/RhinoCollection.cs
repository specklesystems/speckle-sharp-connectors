using Microsoft.Extensions.DependencyInjection;
using Speckle.Converters.Common;
using Speckle.Converters.Rhino;
using Xunit;

namespace Speckle.Connectors.Rhino;

[CollectionDefinition(RhinoSetup.RhinoCollection)]
#pragma warning disable CA1711
public class RhinoCollection : ICollectionFixture<RhinoSetup>
#pragma warning restore CA1711
{
  // This class has no code, and is never created. Its purpose is simply
  // to be the place to apply [CollectionDefinition] and all the
  // ICollectionFixture<> interfaces.
}

public class RhinoSetup
{
  // ReSharper disable once InconsistentNaming
#pragma warning disable IDE1006
  public const string RhinoCollection = "Rhino collection";
#pragma warning restore IDE1006
  public RhinoSetup(IServiceProvider serviceProvider)
  {
    serviceProvider.GetRequiredService<IConverterSettingsStore<RhinoConversionSettings>>().Initialize(new RhinoConversionSettings(null!, "units"));
  }
}
