using Microsoft.Extensions.DependencyInjection;
using Rhino;
using Speckle.Converters.Common;
using Speckle.Converters.Rhino;

namespace Speckle.Connectors.Grasshopper8.HostApp;

/// <summary>
/// Handles grasshopper wide converters. We don't need new converters, unless the document changes - this class should handle this (untested).
/// </summary>
public static class ToSpeckleConversionContext
{
  private static IServiceScope? Scope { get; set; }
  public static IRootToHostConverter ToHostConverter { get; private set; }
  public static IRootToSpeckleConverter ToSpeckleConverter { get; private set; }

  static ToSpeckleConversionContext()
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
}
