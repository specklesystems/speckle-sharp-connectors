using Microsoft.Extensions.DependencyInjection;
using Rhino.Geometry;
using Speckle.Connectors.GrasshopperShared.Registration;
using Speckle.Converters.Common;
using Speckle.Sdk;
using Speckle.Sdk.Common;
using Speckle.Sdk.Common.Exceptions;
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

  public static void SetupCurrent(IServiceScope? scope = null)
  {
    if (s_currentContext != null)
    {
      return;
    }
    s_scope = scope ?? PriorityLoader.CreateScopeForActiveDocument();
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

  public Base? ConvertToSpeckle(object geo)
  {
    try
    {
      return speckleConverter.Convert(geo);
    }
    catch (ConversionException ex)
    {
      // changed as of CNX-2855
      // log for debugging but don't throw - let caller handle null return
      // we don't want to throw and fail whole operation, but want a way to signal to component that sumting wong
      System.Diagnostics.Debug.WriteLine($"Conversion failed for {geo.GetType().Name}: {ex.Message}");
      return null;
    }
  }

  public List<(object, Base)> ConvertToHost(Base input)
  {
    var result = hostConverter.Convert(input);

    return result switch
    {
      GeometryBase geometry => [(geometry, input)],
      List<GeometryBase> geometryList => geometryList.Select(o => ((object)o, input)).ToList(),
      List<(GeometryBase, Base)> pairList when pairList.Count == 0 => [],
      IEnumerable<(GeometryBase, Base)> fallbackConversionResult
        => fallbackConversionResult.Select(o => ((object)o.Item1, o.Item2)).ToList(),
      object obj => [(obj, input)],
      _ => throw new SpeckleException("Failed to convert input to grasshopper")
    };
  }
}
