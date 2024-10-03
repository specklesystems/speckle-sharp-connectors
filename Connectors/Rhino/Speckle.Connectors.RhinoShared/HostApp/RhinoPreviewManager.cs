using Rhino;
using Rhino.Display;
using Rhino.Geometry;
using Point = Rhino.Geometry.Point;

namespace Speckle.Connectors.Rhino.HostApp;

/// <summary>
/// Utility class managing layer creation. Expects to be a scoped dependency per receive operation.
/// </summary>
public class RhinoPreviewManager
{
  private PreviewConduit _preview;

  public RhinoPreviewManager() { }

  public void UpdatePreview(List<GeometryBase> objs)
  {
    if (_preview is not null)
    {
      _preview.Enabled = false;
    }
    _preview = new PreviewConduit(objs) { Enabled = true };
    RhinoDoc.ActiveDoc.Views.Redraw();
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
