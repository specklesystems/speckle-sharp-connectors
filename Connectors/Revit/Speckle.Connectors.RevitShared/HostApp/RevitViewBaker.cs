using Autodesk.Revit.DB;
using Microsoft.Extensions.Logging;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Objects.Other;
using Speckle.Sdk;

namespace Speckle.Connectors.Revit.HostApp;

/// <summary>
/// Utility class that creates View3D elements from Camera objects during receive.
/// </summary>
public class RevitViewBaker
{
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;
  private readonly ILogger<RevitViewBaker> _logger;
  private readonly ITypedConverter<Speckle.Objects.Geometry.Point, XYZ> _pointConverter;
  private readonly ITypedConverter<Speckle.Objects.Geometry.Vector, XYZ> _vectorConverter;

  public RevitViewBaker(
    IConverterSettingsStore<RevitConversionSettings> converterSettings,
    ILogger<RevitViewBaker> logger,
    ITypedConverter<Speckle.Objects.Geometry.Point, XYZ> pointConverter,
    ITypedConverter<Speckle.Objects.Geometry.Vector, XYZ> vectorConverter
  )
  {
    _converterSettings = converterSettings;
    _logger = logger;
    _pointConverter = pointConverter;
    _vectorConverter = vectorConverter;
  }

  // Characters that are not allowed in Revit view names
  private readonly char[] _invalidViewNameChars = ['{', '}', '[', ']', '|', ';', '<', '>', '?', '`', '~', '\\', ':'];

  /// <summary>
  /// Bakes Camera objects as View3D elements in Revit.
  /// </summary>
  public void BakeViews(IReadOnlyCollection<Camera> cameras)
  {
    if (cameras.Count == 0)
    {
      return;
    }

    foreach (var camera in cameras)
    {
      if (string.IsNullOrWhiteSpace(camera.name))
      {
        continue;
      }

      var restoredName = RestoreViewName(camera.name);
      if (string.IsNullOrWhiteSpace(restoredName))
      {
        continue;
      }

      var existingView = FindViewByName(restoredName);

      try
      {
        if (existingView != null)
        {
          UpdatePerspectiveView(existingView, camera);
        }
        else
        {
          CreatePerspectiveView(camera, restoredName);
        }
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        _logger.LogError(ex, "Failed to create view '{ViewName}'", restoredName);
      }
    }
  }

  /// <summary>
  /// Sanitizes the view name by removing invalid characters.
  /// </summary>
  private string RestoreViewName(string name)
  {
    var restored = name;

    foreach (var c in _invalidViewNameChars)
    {
      restored = restored.Replace(c.ToString(), string.Empty);
    }

    return restored.Trim();
  }

  private View3D? FindViewByName(string name)
  {
    using var collector = new FilteredElementCollector(_converterSettings.Current.Document);
    return collector
      .OfClass(typeof(View3D))
      .Cast<View3D>()
      .FirstOrDefault(v => !v.IsTemplate && string.Equals(v.Name, name, StringComparison.Ordinal));
  }

  private void UpdatePerspectiveView(View3D view3D, Camera camera)
  {
    var eyePosition = _pointConverter.Convert(camera.position);
    var forwardDirection = _vectorConverter.Convert(camera.forward).Normalize();
    var upDirection = _vectorConverter.Convert(camera.up).Normalize();

    var orientation = new ViewOrientation3D(eyePosition, upDirection, forwardDirection);
    view3D.SetOrientation(orientation);
  }

  private void CreatePerspectiveView(Camera camera, string viewName)
  {
    var document = _converterSettings.Current.Document;

    // Get ViewFamilyType for 3D views
    using var collector = new FilteredElementCollector(document);
    var viewFamilyType = collector
      .OfClass(typeof(ViewFamilyType))
      .Cast<ViewFamilyType>()
      .FirstOrDefault(vft => vft.ViewFamily == ViewFamily.ThreeDimensional);

    if (viewFamilyType == null)
    {
      _logger.LogError("Could not find a 3D ViewFamilyType to create view '{ViewName}'", viewName);
      return;
    }

    // Create perspective view (View3D is a document element, not disposable) - low happiness level
#pragma warning disable CA2000
    var view3D = View3D.CreatePerspective(document, viewFamilyType.Id);
#pragma warning restore CA2000

    // Convert camera position, forward, and up vectors
    var eyePosition = _pointConverter.Convert(camera.position);
    var forwardDirection = _vectorConverter.Convert(camera.forward).Normalize();
    var upDirection = _vectorConverter.Convert(camera.up).Normalize();

    var orientation = new ViewOrientation3D(eyePosition, upDirection, forwardDirection);
    view3D.SetOrientation(orientation);

    view3D.Name = viewName;

    // Set display style to Shaded (looks better than default wireframe)
    view3D.DisplayStyle = DisplayStyle.Shading;

    // Disable far clipping so depth is infinite
    var farClipParam = view3D.get_Parameter(BuiltInParameter.VIEWER_BOUND_ACTIVE_FAR);
    if (farClipParam != null && !farClipParam.IsReadOnly)
    {
      farClipParam.Set(0);
    }
  }
}
