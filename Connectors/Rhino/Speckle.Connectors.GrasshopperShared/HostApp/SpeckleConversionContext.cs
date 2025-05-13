using Microsoft.Extensions.DependencyInjection;
using Rhino;
using Rhino.Geometry;
using Speckle.Connectors.GrasshopperShared.Registration;
using Speckle.Converters.Common;
using Speckle.Converters.Rhino;
using Speckle.Sdk;
using Speckle.Sdk.Models;

namespace Speckle.Connectors.GrasshopperShared.HostApp;

/// <summary>
/// Handles grasshopper wide converters. We don't need new converters, unless the document changes - this class should handle this (untested).
/// </summary>
public static class SpeckleConversionContext
{
  private static IServiceScope? Scope { get; set; }
  private static IRootToHostConverter ToHostConverter { get; set; }
  private static IRootToSpeckleConverter ToSpeckleConverter { get; set; }

  static SpeckleConversionContext()
  {
    RhinoDoc.ActiveDocumentChanged += RhinoDocOnActiveDocumentChanged;
    InitializeConverters();
  }

  private static void RhinoDocOnActiveDocumentChanged(object sender, DocumentEventArgs e) => InitializeConverters(); // note: untested, and wrong on mac

  private static void InitializeConverters()
  {
    Scope?.Dispose();
    Scope = PriorityLoader.Container.CreateScope();

    var rhinoConversionSettingsFactory = Scope.ServiceProvider.GetRequiredService<IRhinoConversionSettingsFactory>();
    Scope
      .ServiceProvider.GetRequiredService<IConverterSettingsStore<RhinoConversionSettings>>()
      .Initialize(rhinoConversionSettingsFactory.Create(RhinoDoc.ActiveDoc));

    ToHostConverter = Scope.ServiceProvider.GetService<IRootToHostConverter>();
    ToSpeckleConverter = Scope.ServiceProvider.GetService<IRootToSpeckleConverter>();
  }

  public static Base ConvertToSpeckle(GeometryBase geo) => ToSpeckleConverter.Convert(geo);

  public static List<(GeometryBase, Base)> ConvertToHost(Base input)
  {
    var result = ToHostConverter.Convert(input);

    return result switch
    {
      GeometryBase geometry => [(geometry, input)],
      List<GeometryBase> geometryList => geometryList.Select(o => (o, input)).ToList(),
      IEnumerable<(GeometryBase, Base)> fallbackConversionResult => fallbackConversionResult.ToList(),
      _ => throw new SpeckleException("Failed to convert input to rhino")
    };
  }
}
