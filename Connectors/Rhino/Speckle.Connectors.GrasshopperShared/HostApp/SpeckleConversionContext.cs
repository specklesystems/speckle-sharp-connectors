using Microsoft.Extensions.DependencyInjection;
using Rhino.Geometry;
using Speckle.Connectors.GrasshopperShared.Registration;
using Speckle.Converters.Common;
using Speckle.Sdk;
using Speckle.Sdk.Models;

namespace Speckle.Connectors.GrasshopperShared.HostApp;

/// <summary>
/// Handles grasshopper wide converters. We don't need new converters, unless the document changes - this class should handle this (untested).
/// </summary>
public static class SpeckleConversionContext
{
  public static Base ConvertToSpeckle(GeometryBase geo)
  {
    using var scope = PriorityLoader.CreateScopeForActiveDocument();
    return scope.ServiceProvider.GetRequiredService<IRootToSpeckleConverter>().Convert(geo);
  }

  public static List<(GeometryBase, Base)> ConvertToHost(Base input)
  {
    using var scope = PriorityLoader.CreateScopeForActiveDocument();
    var result = scope.ServiceProvider.GetRequiredService<IRootToHostConverter>().Convert(input);

    return result switch
    {
      GeometryBase geometry => [(geometry, input)],
      List<GeometryBase> geometryList => geometryList.Select(o => (o, input)).ToList(),
      IEnumerable<(GeometryBase, Base)> fallbackConversionResult => fallbackConversionResult.ToList(),
      _ => throw new SpeckleException("Failed to convert input to rhino")
    };
  }
}
