using Microsoft.Extensions.DependencyInjection;
using Rhino.Geometry;
using Speckle.Connectors.GrasshopperShared.Registration;
using Speckle.Converters.Common;
using Speckle.Sdk;
using Speckle.Sdk.Common;
using Speckle.Sdk.Models;

namespace Speckle.Connectors.GrasshopperShared.HostApp;

/// <summary>
/// Handles grasshopper wide converters. We don't need new converters, unless the document changes - this class should handle this (untested).
/// </summary>
public class SpeckleConversionContext(IRootToSpeckleConverter speckleConverter, IRootToHostConverter hostConverter)
{
  private static IServiceScope? s_scope;
  private static SpeckleConversionContext? s_currentContext;

  public static SpeckleConversionContext Current
  {
    get
    {
      if (s_currentContext == null)
      {
        SetupCurrent();
      }

      return s_currentContext.NotNull();
    }
  }

  public static void SetupCurrent()
  {
    if (s_currentContext != null)
    {
      return;
    }
    s_scope = PriorityLoader.CreateScopeForActiveDocument();
    s_currentContext = s_scope.Get<SpeckleConversionContext>();
  }

  public static void EndCurrent()
  {
    if (s_currentContext == null)
    {
      return;
    }
    s_currentContext = null;
    s_scope?.Dispose();
    s_scope = null;
  }

  public Base ConvertToSpeckle(object geo) => speckleConverter.Convert(geo);

  public List<(object, Base)> ConvertToHost(Base input)
  {
    var result = hostConverter.Convert(input);

    return result switch
    {
      GeometryBase geometry => [(geometry, input)],
      List<GeometryBase> geometryList => geometryList.Select(o => ((object)o, input)).ToList(),
      IEnumerable<(GeometryBase, Base)> fallbackConversionResult
        => fallbackConversionResult.Select(o => ((object)o.Item1, o.Item2)).ToList(),
      object obj => [(obj, input)],
      _ => throw new SpeckleException("Failed to convert input to grasshopper")
    };
  }
}
