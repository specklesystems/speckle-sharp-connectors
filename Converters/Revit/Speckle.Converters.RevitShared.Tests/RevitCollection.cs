using Microsoft.Extensions.DependencyInjection;
using Speckle.Converters.Common;
using Speckle.Converters.RevitShared.Settings;
using Xunit;

namespace Speckle.Connectors.Rhino;

[CollectionDefinition(RevitSetup.RevitCollection)]
#pragma warning disable CA1711
public class RevitCollection : ICollectionFixture<RevitSetup>
#pragma warning restore CA1711
{
  // This class has no code, and is never created. Its purpose is simply
  // to be the place to apply [CollectionDefinition] and all the
  // ICollectionFixture<> interfaces.
}

public class RevitSetup
{
  public const string DEFAULT_UNITS = "units";
  // ReSharper disable once InconsistentNaming
#pragma warning disable IDE1006
  public const string RevitCollection = "Revit collection";
#pragma warning restore IDE1006
  public RevitSetup(IServiceProvider serviceProvider)
  {
    serviceProvider.GetRequiredService<IConverterSettingsStore<RevitConversionSettings>>().Initialize(new RevitConversionSettings(null!, 
      DetailLevelType.Coarse, null, DEFAULT_UNITS, false));
  }
}
