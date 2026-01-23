using Microsoft.Extensions.Logging;
using Rhino.DocObjects;
using Speckle.Connectors.Common.Operations;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Rhino;
using Speckle.Objects.Other;
using Speckle.Sdk;
using Speckle.Sdk.Common;
using Speckle.Sdk.Models;
using RG = Rhino.Geometry;

namespace Speckle.Connectors.Rhino.HostApp;

/// <summary>
/// Utility class that creates Named Views during receive.
/// </summary>
public class RhinoViewBaker
{
  private readonly IConverterSettingsStore<RhinoConversionSettings> _converterSettings;
  private readonly ILogger<RhinoViewBaker> _logger;
  private readonly ITypedConverter<Speckle.Objects.Geometry.Point, RG.Point3d> _pointConverter;
  private readonly ITypedConverter<Speckle.Objects.Geometry.Vector, RG.Vector3d> _vectorConverter;

  public RhinoViewBaker(
    IConverterSettingsStore<RhinoConversionSettings> converterSettings,
    ILogger<RhinoViewBaker> logger,
    ITypedConverter<Speckle.Objects.Geometry.Point, RG.Point3d> pointConverter,
    ITypedConverter<Speckle.Objects.Geometry.Vector, RG.Vector3d> vectorConverter
  )
  {
    _converterSettings = converterSettings;
    _logger = logger;
    _pointConverter = pointConverter;
    _vectorConverter = vectorConverter;
  }

  /// <summary>
  /// Bakes view objects from the root object as Named Views in Rhino.
  /// </summary>
  public void BakeViews(Base rootObject)
  {
    var cameras = TryGetCameras(rootObject);
    if (cameras == null || cameras.Count == 0)
    {
      return;
    }

    foreach (var camera in cameras)
    {
      if (string.IsNullOrWhiteSpace(camera.name))
      {
        continue;
      }

      var viewName = camera.name.Trim();

      if (NamedViewExists(viewName))
      {
        _logger.LogInformation("Named View '{ViewName}' already exists, skipping creation", viewName);
        continue;
      }

      try
      {
        CreateNamedView(camera, viewName);
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        _logger.LogError(ex, "Failed to create Named View '{ViewName}'", viewName);
      }
    }
  }

  private List<Camera>? TryGetCameras(Base rootObject)
  {
    if (!rootObject.DynamicPropertyKeys.Contains(RootKeys.VIEW))
    {
      return null;
    }

    var viewsProperty = rootObject[RootKeys.VIEW];
    if (viewsProperty is null)
    {
      return null;
    }

    var cameras = new List<Camera>();

    if (viewsProperty is IEnumerable<object> viewsList)
    {
      foreach (var item in viewsList)
      {
        if (item is Camera camera)
        {
          cameras.Add(camera);
        }
      }
    }

    return cameras;
  }

  private bool NamedViewExists(string name)
  {
    var doc = _converterSettings.Current.Document;
    foreach (var view in doc.NamedViews)
    {
      if (string.Equals(view.Name, name, StringComparison.OrdinalIgnoreCase))
      {
        return true;
      }
    }
    return false;
  }

  private void CreateNamedView(Camera camera, string viewName)
  {
    var doc = _converterSettings.Current.Document;

    // Get the active viewport
    var activeView = doc.Views.ActiveView;
    if (activeView == null)
    {
      _logger.LogError("No active view available to create Named View '{ViewName}'", viewName);
      return;
    }

    var cameraLocation = _pointConverter.Convert(camera.position);
    var cameraDirection = _vectorConverter.Convert(camera.forward);
    var cameraUp = _vectorConverter.Convert(camera.up);

    // scaling needed (point converters don't scale)
    var sourceUnits = camera.position.units;
    if (!string.IsNullOrEmpty(sourceUnits))
    {
      var scaleFactor = Units.GetConversionFactor(sourceUnits, _converterSettings.Current.SpeckleUnits);
      cameraLocation = new RG.Point3d(
        cameraLocation.X * scaleFactor,
        cameraLocation.Y * scaleFactor,
        cameraLocation.Z * scaleFactor
      );
    }

    // Direction vectors (they don't need scaling, just direction)
    cameraDirection.Unitize();
    cameraUp.Unitize();

    var targetDistance = Units.GetConversionFactor(Units.Meters, _converterSettings.Current.SpeckleUnits);
    var targetPoint = cameraLocation + cameraDirection * targetDistance;

    var originalViewportInfo = new ViewportInfo(activeView.ActiveViewport);

    // Copy the current viewport info to preserve aspect ratio settings
    var viewportInfo = new ViewportInfo(activeView.ActiveViewport);
    viewportInfo.SetCameraLocation(cameraLocation);
    viewportInfo.SetCameraDirection(cameraDirection);
    viewportInfo.SetCameraUp(cameraUp);
    viewportInfo.TargetPoint = targetPoint;

    // Ensure it's perspective (lensLength=50mm, symmetric=true, nearDistance=1)
    if (!viewportInfo.IsPerspectiveProjection)
    {
      viewportInfo.ChangeToPerspectiveProjection(50, true, 1);
    }

    activeView.ActiveViewport.SetViewProjection(viewportInfo, true);

    // Add the named view from the current viewport
    doc.NamedViews.Add(viewName, activeView.ActiveViewportID);

    // Restore original viewport state
    activeView.ActiveViewport.SetViewProjection(originalViewportInfo, true);
    activeView.Redraw();
  }
}
