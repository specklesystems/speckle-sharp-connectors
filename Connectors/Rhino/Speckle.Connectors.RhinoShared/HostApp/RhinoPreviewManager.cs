using Rhino.Display;
using Rhino.Geometry;
using Speckle.Converters.Common;
using Speckle.Converters.Rhino;
using Point = Rhino.Geometry.Point;

namespace Speckle.Connectors.Rhino.HostApp;

/// <summary>
/// Utility class managing layer creation. Expects to be a scoped dependency per receive operation.
/// </summary>
public class RhinoPreviewManager
{
  private PreviewConduit _preview;
  private readonly IConverterSettingsStore<RhinoConversionSettings> _converterSettings;

  public RhinoPreviewManager(IConverterSettingsStore<RhinoConversionSettings> converterSettings)
  {
    _converterSettings = converterSettings;
  }

  public void UpdatePreview(List<GeometryBase> objs)
  {
    _preview.Enabled = false;
    _preview = new PreviewConduit(objs) { Enabled = true };
    _converterSettings.Current.Document.Views.Redraw();
  }

  private sealed class PreviewConduit : DisplayConduit
  {
    public BoundingBox Bbox;
    private readonly Color _color = Color.FromArgb(200, 59, 130, 246);

    //private readonly DisplayMaterial _material;

    public PreviewConduit(List<GeometryBase> preview)
    {
      Bbox = new BoundingBox();

      foreach (var previewObj in preview)
      {
        Bbox.Union(previewObj.GetBoundingBox(false));

        Preview.Add(previewObj);
      }
    }

    public List<object> Preview { get; set; } = new();

    // reference: https://developer.rhino3d.com/api/RhinoCommon/html/M_Rhino_Display_DisplayConduit_CalculateBoundingBox.htm
    protected override void CalculateBoundingBox(CalculateBoundingBoxEventArgs e)
    {
      base.CalculateBoundingBox(e);
      e.IncludeBoundingBox(Bbox);
    }

    protected override void CalculateBoundingBoxZoomExtents(CalculateBoundingBoxEventArgs e)
    {
      CalculateBoundingBox(e);
    }

    protected override void PreDrawObjects(DrawEventArgs e)
    {
      // draw preview objects
      var display = e.Display;

      foreach (var previewobj in Preview)
      {
        var drawColor = _color;
        DisplayMaterial drawMaterial = new() { Transparency = 0.8, Diffuse = _color };
        drawMaterial.Diffuse = drawColor;

        switch (previewobj)
        {
          case Brep o:
            display.DrawBrepShaded(o, drawMaterial);
            break;
          case Mesh o:
            display.DrawMeshShaded(o, drawMaterial);
            break;
          case Curve o:
            display.DrawCurve(o, drawColor);
            break;
          case Point o:
            display.DrawPoint(o.Location, drawColor);
            break;
          case Point3d o:
            display.DrawPoint(o, drawColor);
            break;
          case PointCloud o:
            display.DrawPointCloud(o, 5, drawColor);
            break;
        }
      }
    }
  }
}
