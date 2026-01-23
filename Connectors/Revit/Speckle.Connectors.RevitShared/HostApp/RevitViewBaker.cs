using Autodesk.Revit.DB;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Operations;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Objects.Other;
using Speckle.Sdk;
using Speckle.Sdk.Models;

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
  private static readonly char[] s_invalidViewNameChars = ['{', '}', '[', ']', '|', ';', '<', '>', '?', '`', '~'];

  /// <summary>
  /// Bakes Camera objects from the root object View3D in Revit.
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

      var restoredName = RestoreViewName(camera.name);
      if (string.IsNullOrWhiteSpace(restoredName))
      {
        continue;
      }

      if (ViewExistsByName(restoredName))
      {
        _logger.LogInformation("View with name '{ViewName}' already exists, skipping creation", restoredName);
        continue;
      }

      try
      {
        CreatePerspectiveView(camera, restoredName);
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        _logger.LogError(ex, "Failed to create view '{ViewName}'", restoredName);
      }
    }
  }

  /// <summary>
  /// Restores the view name by removing prefixes and invalid characters.
  /// </summary>
  private static string RestoreViewName(string name)
  {
    var restored = name;

    // Strip common view type prefixes (e.g., "3D View: Kitchen" -> "Kitchen")
    string[] prefixesToRemove = ["3D View: ", "3D View:", "3D View - ", "3D View-"];
    foreach (var prefix in prefixesToRemove)
    {
      if (restored.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
      {
        restored = restored[prefix.Length..];
        break;
      }
    }

    restored = restored.Replace(':', '-');

    foreach (var c in s_invalidViewNameChars)
    {
      restored = restored.Replace(c.ToString(), string.Empty);
    }

    return restored.Trim();
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

  private bool ViewExistsByName(string name)
  {
    using var collector = new FilteredElementCollector(_converterSettings.Current.Document);
    return collector
      .OfClass(typeof(View3D))
      .Cast<View3D>()
      .Any(v => !v.IsTemplate && string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));
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
