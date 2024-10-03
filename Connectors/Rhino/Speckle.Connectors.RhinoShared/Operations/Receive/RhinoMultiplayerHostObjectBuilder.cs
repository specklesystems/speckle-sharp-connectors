using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Operations.Receive;
using Speckle.Connectors.Rhino.HostApp;
using Speckle.Converters.Common;
using Speckle.Converters.Rhino;
using Speckle.Sdk;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Models.GraphTraversal;
using Speckle.Sdk.Models.Instances;
using Layer = Rhino.DocObjects.Layer;
using Point = Rhino.Geometry.Point;

namespace Speckle.Connectors.Rhino.Operations.Receive;

/// <summary>
/// <para>Expects to be a scoped dependency per receive operation.</para>
/// </summary>
public class RhinoMultiplayerHostObjectBuilder : IMultiplayerHostObjectBuilder
{
  private readonly IRootToHostConverter _converter;
  private readonly IConverterSettingsStore<RhinoConversionSettings> _converterSettings;
  private readonly RhinoLayerBaker _layerBaker;
  private readonly RhinoMaterialBaker _materialBaker;
  private readonly RhinoColorBaker _colorBaker;
  private readonly RootObjectUnpacker _rootObjectUnpacker;
  private readonly ISdkActivityFactory _activityFactory;

  public RhinoMultiplayerHostObjectBuilder(
    IRootToHostConverter converter,
    IConverterSettingsStore<RhinoConversionSettings> converterSettings,
    RhinoLayerBaker layerBaker,
    RootObjectUnpacker rootObjectUnpacker,
    RhinoMaterialBaker materialBaker,
    RhinoColorBaker colorBaker,
    ISdkActivityFactory activityFactory
  )
  {
    _converter = converter;
    _converterSettings = converterSettings;
    _rootObjectUnpacker = rootObjectUnpacker;
    _materialBaker = materialBaker;
    _colorBaker = colorBaker;
    _layerBaker = layerBaker;
    _activityFactory = activityFactory;
  }

  public Task<HostObjectBuilderResult> Build(
    Base rootObject,
    string projectName,
    string modelName,
    Action<string, double?>? onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    using var activity = _activityFactory.Start("Build");
    // POC: This is where the top level base-layer name is set. Could be abstracted or injected in the context?
    var baseLayerName = $"Project {projectName}: Model {modelName} - MULTIPLAYER SESSION";
    var index = RhinoDoc.ActiveDoc.Layers.Add(new Layer { Name = baseLayerName });

    // purge current view and preview conduit
    PreReceiveDeepClean(baseLayerName);

    // UPDATE VIEW
    UpdateActiveViewCamera(rootObject, index);

    _converterSettings.Current.Document.Views.Redraw();

    // 1 - Unpack objects and proxies from root commit object
    var unpackedRoot = _rootObjectUnpacker.Unpack(rootObject);
    List<TraversalContext> atomicObjects = unpackedRoot
      .ObjectsToConvert.Where(o => o is not IInstanceComponent)
      .ToList();

    var atomicObjectsWithPath = _layerBaker.GetAtomicObjectsWithPath(atomicObjects);

    // 2 - Parse colors, as they are used by the preview conduit
    onOperationProgressed?.Invoke("Converting colors", null);
    if (unpackedRoot.ColorProxies != null)
    {
      _colorBaker.ParseColors(unpackedRoot.ColorProxies);
    }

    // 3 - Convert atomic objects
    List<string> bakedObjectIds = new();
    List<ReceiveConversionResult> conversionResults = new();

    List<GeometryBase> convertedObjects = new();

    int count = 0;
    using (var _ = _activityFactory.Start("Converting objects"))
    {
      foreach (var (path, obj) in atomicObjectsWithPath)
      {
        using (var convertActivity = _activityFactory.Start("Converting object"))
        {
          onOperationProgressed?.Invoke("Converting objects", (double)++count / atomicObjects.Count);
          try
          {
            // 1: convert
            var result = _converter.Convert(obj);

            if (result is GeometryBase geometryBase)
            {
              convertedObjects.Add(geometryBase);
            }

            // 5: populate app id map
            convertActivity?.SetStatus(SdkActivityStatusCode.Ok);
          }
          catch (Exception ex) when (!ex.IsFatal())
          {
            conversionResults.Add(new(Status.ERROR, obj, null, null, ex));
            convertActivity?.SetStatus(SdkActivityStatusCode.Error);
            convertActivity?.RecordException(ex);
          }
        }
      }
    }

    // PREVIEW CONDUIT
    var preview = new PreviewConduit(convertedObjects);

    _converterSettings.Current.Document.Views.Redraw();

    return Task.FromResult(new HostObjectBuilderResult(bakedObjectIds, conversionResults));
  }

  private void UpdateActiveViewCamera(Base rootObject, int layer)
  {
    if (
      rootObject["view"] is Base view
      && view["locationX"] is double x
      && view["locationY"] is double y
      && view["locationZ"] is double z
    )
    {
      // convert view to text dot (first pass)
      var viewTextDot = new TextDot("Player 2", new Point3d(x, y, z));

      ObjectAttributes atts = new() { LayerIndex = layer };
      _converterSettings.Current.Document.Objects.Add(viewTextDot, atts);
    }
    else
    {
      // TODO: throw
    }
  }

  private void PreReceiveDeepClean(string baseLayerName)
  {
    // Remove all previously received layers and render materials from the document
    int rootLayerIndex = _converterSettings.Current.Document.Layers.Find(
      Guid.Empty,
      baseLayerName,
      RhinoMath.UnsetIntIndex
    );

    var doc = _converterSettings.Current.Document;

    var purgeSuccess = doc.Layers.Purge(rootLayerIndex, true);
    if (!purgeSuccess)
    {
      Console.WriteLine($"Failed to purge layer: {baseLayerName}");
    }
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
