using System.Drawing;
using ArcGIS.Core.CIM;
using ArcGIS.Desktop.Mapping;
using Speckle.Connectors.Common.Operations;
using Speckle.Converters.ArcGIS3.Utils;
using Speckle.Objects;
using Speckle.Objects.Other;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Models.Extensions;
using Speckle.Sdk.Models.GraphTraversal;
using Speckle.Sdk.Models.Proxies;

namespace Speckle.Connectors.ArcGIS.HostApp;

/// <summary>
/// TODO: definitely need to refactor this, probably will collect colors during layer iteration in the root object builder.
/// </summary>
public class ArcGISColorManager
{
  public Dictionary<string, Color> ObjectColorsIdMap { get; set; } = new();
  public Dictionary<string, Color> ObjectMaterialsIdMap { get; set; } = new();

  /// <summary>
  /// Parse Color Proxies and stores in ObjectColorsIdMap the relationship between object ids and colors
  /// </summary>
  /// <param name="colorProxies"></param>
  /// <param name="onOperationProgressed"></param>
  public async Task ParseColors(List<ColorProxy> colorProxies, IProgress<CardProgress> onOperationProgressed)
  {
    // injected as Singleton, so we need to clean existing proxies first
    ObjectColorsIdMap = new();
    var count = 0;
    foreach (ColorProxy colorProxy in colorProxies)
    {
      onOperationProgressed.Report(new("Converting colors", (double)++count / colorProxies.Count));
      await Task.Yield();
      foreach (string objectId in colorProxy.objects)
      {
        Color convertedColor = Color.FromArgb(colorProxy.value);
        ObjectColorsIdMap.TryAdd(objectId, convertedColor);
      }
    }
  }

  /// <summary>
  /// Parse Color renderMaterials  and stores in ObjectMaterialsIdMap the relationship between object ids and colors
  /// </summary>
  /// <param name="materialProxies"></param>
  /// <param name="onOperationProgressed"></param>
  public async Task ParseMaterials(
    List<RenderMaterialProxy> materialProxies,
    IProgress<CardProgress> onOperationProgressed
  )
  {
    // injected as Singleton, so we need to clean existing proxies first
    ObjectMaterialsIdMap = new();
    var count = 0;
    foreach (RenderMaterialProxy colorProxy in materialProxies)
    {
      onOperationProgressed.Report(new("Converting materials", (double)++count / materialProxies.Count));
      await Task.Yield();
      foreach (string objectId in colorProxy.objects)
      {
        Color convertedColor = Color.FromArgb(colorProxy.value.diffuse);
        ObjectMaterialsIdMap.TryAdd(objectId, convertedColor);
      }
    }
  }

  public int CIMColorToInt(CIMColor color)
  {
    return (255 << 24)
      | ((int)Math.Round(color.Values[0]) << 16)
      | ((int)Math.Round(color.Values[1]) << 8)
      | (int)Math.Round(color.Values[2]);
  }

  /// <summary>
  /// Create a new CIMUniqueValueClass for UniqueRenderer per each object ID
  /// </summary>
  /// <param name="tc"></param>
  /// <param name="speckleGeometryType"></param>
  private CIMUniqueValueClass CreateColorCategory(
    TraversalContext tc,
    esriGeometryType speckleGeometryType,
    string uniqueLabel
  )
  {
    // declare default white color
    Color color = Color.FromArgb(255, 255, 255, 255);
    bool colorFound = false;

    // get color moving upwards from the object
    foreach (var parent in tc.GetAscendants())
    {
      if (parent.applicationId is string appId)
      {
        if (ObjectMaterialsIdMap.TryGetValue(appId, out Color objColorMaterial))
        {
          color = objColorMaterial;
          colorFound = true;
          break;
        }
        if (ObjectColorsIdMap.TryGetValue(appId, out Color objColor))
        {
          color = objColor;
          colorFound = true;
          break;
        }
      }
    }

    // handling Revit case, where child objects have separate colors/materials
    if (!colorFound && tc.Current is IDataObject)
    {
      var displayable = tc.Current.TryGetDisplayValue();
      if (displayable != null)
      {
        foreach (var childObj in displayable)
        {
          if (childObj.applicationId is string appId)
          {
            if (ObjectMaterialsIdMap.TryGetValue(appId, out Color objColorMaterial))
            {
              color = objColorMaterial;
              break;
            }
            if (ObjectColorsIdMap.TryGetValue(appId, out Color objColor))
            {
              color = objColor;
              break;
            }
          }
        }
      }
    }

    CIMSymbolReference symbol = CreateSymbol(speckleGeometryType, color);

    // First create a "CIMUniqueValueClass"
    List<CIMUniqueValue> listUniqueValues = new() { new CIMUniqueValue { FieldValues = new string[] { uniqueLabel } } };

    CIMUniqueValueClass newUniqueValueClass =
      new()
      {
        Editable = true,
        Label = uniqueLabel,
        Patch = PatchShape.Default,
        Symbol = symbol,
        Visible = true,
        Values = listUniqueValues.ToArray()
      };
    return newUniqueValueClass;
  }

  /// <summary>
  /// Create a Symbol from GeometryType and Color
  /// </summary>
  /// <param name="speckleGeometryType"></param>
  /// <param name="color"></param>
  private CIMSymbolReference CreateSymbol(esriGeometryType speckleGeometryType, Color color)
  {
    var symbol = SymbolFactory
      .Instance.ConstructPointSymbol(ColorFactory.Instance.CreateColor(color))
      .MakeSymbolReference();

    switch (speckleGeometryType)
    {
      case esriGeometryType.esriGeometryLine:
      case esriGeometryType.esriGeometryPolyline:
        symbol = SymbolFactory
          .Instance.ConstructLineSymbol(ColorFactory.Instance.CreateColor(color))
          .MakeSymbolReference();
        break;
      case esriGeometryType.esriGeometryPolygon:
      case esriGeometryType.esriGeometryMultiPatch:
        symbol = SymbolFactory
          .Instance.ConstructPolygonSymbol(ColorFactory.Instance.CreateColor(color))
          .MakeSymbolReference();
        break;
    }

    return symbol;
  }

  /// <summary>
  /// Add CIMUniqueValueClass to Layer Renderer (if exists); apply Renderer to Layer (again)
  /// </summary>
  /// <param name="tc"></param>
  /// <param name="trackerItem"></param>
  public CIMUniqueValueRenderer? CreateOrEditLayerRenderer(
    TraversalContext tc,
    ObjectConversionTracker trackerItem,
    CIMRenderer? existingRenderer
  )
  {
    if (trackerItem.HostAppMapMember is not FeatureLayer fLayer)
    {
      // do nothing with non-feature layers
      return null;
    }

    // declare default grey color, create default symbol for the given layer geometry type
    var color = Color.FromArgb(CIMColorToInt(ColorFactory.Instance.GreyRGB));
    CIMSymbolReference defaultSymbol = CreateSymbol(fLayer.ShapeType, color);

    // get existing renderer classes
    List<CIMUniqueValueClass> listUniqueValueClasses = new() { };
    if (existingRenderer is CIMUniqueValueRenderer uniqueRenderer)
    {
      if (uniqueRenderer.Groups[0].Classes != null)
      {
        listUniqueValueClasses.AddRange(uniqueRenderer.Groups[0].Classes.ToList());
      }
    }

    // Add new CIMUniqueValueClass (or multiple, if it's a Collection with elements, e.g. VectorLayer)
    List<TraversalContext> traversalContexts = new();
    if (tc.Current is Collection collection)
    {
      foreach (var element in collection.elements)
      {
        TraversalContext newTc = new(element, "elements", tc);
        traversalContexts.Add(newTc);
      }
    }
    else
    {
      traversalContexts.Add(tc);
    }

    foreach (var tContext in traversalContexts)
    {
      // get unique label
      string? uniqueLabel = tContext.Current?.id;

      // remove any GIS-specific classes for now
      /*
      if (tContext.Current is IGisFeature gisFeat)
      {
        var existingLabel = gisFeat.attributes["Speckle_ID"];
        if (existingLabel is string stringLabel)
        {
          uniqueLabel = stringLabel;
        }
      }*/

      if (uniqueLabel is not null && !listUniqueValueClasses.Select(x => x.Label).Contains(uniqueLabel))
      {
        CIMUniqueValueClass newUniqueValueClass = CreateColorCategory(tContext, fLayer.ShapeType, uniqueLabel);
        listUniqueValueClasses.Add(newUniqueValueClass);
      }
    }

    // Create a list of CIMUniqueValueGroup
    CIMUniqueValueGroup uvg = new() { Classes = listUniqueValueClasses.ToArray(), Heading = "Speckle_ID" };
    List<CIMUniqueValueGroup> listUniqueValueGroups = new() { uvg };
    // Create the CIMUniqueValueRenderer
    CIMUniqueValueRenderer uvr =
      new()
      {
        UseDefaultSymbol = true,
        DefaultLabel = "all other values",
        DefaultSymbol = defaultSymbol,
        Groups = listUniqueValueGroups.ToArray(),
        Fields = new string[] { "Speckle_ID" }
      };
    return uvr;
  }
}
