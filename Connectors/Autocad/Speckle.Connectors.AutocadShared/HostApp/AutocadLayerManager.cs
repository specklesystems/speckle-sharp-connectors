using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.LayerManager;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Models.GraphTraversal;
using AutocadColor = Autodesk.AutoCAD.Colors.Color;

namespace Speckle.Connectors.Autocad.HostApp;

/// <summary>
/// Expects to be a scoped dependency for a given operation and helps with layer creation and cleanup.
/// </summary>
public class AutocadLayerManager
{
  private readonly AutocadContext _autocadContext;
  private readonly AutocadMaterialManager _materialManager;
  private readonly AutocadColorManager _colorManager;
  private readonly string _layerFilterName = "Speckle";
  public Dictionary<string, Layer> CollectionCache { get; } = new();

  // POC: Will be addressed to move it into AutocadContext!
  private Document Doc => Application.DocumentManager.MdiActiveDocument;
  private readonly HashSet<string> _uniqueLayerNames = new();

  public AutocadLayerManager(
    AutocadContext autocadContext,
    AutocadMaterialManager materialManager,
    AutocadColorManager colorManager
  )
  {
    _autocadContext = autocadContext;
    _materialManager = materialManager;
    _colorManager = colorManager;
  }

  public Layer GetOrCreateSpeckleLayer(Entity entity, Transaction tr, out LayerTableRecord? layer)
  {
    string layerName = entity.Layer;
    layer = null;
    if (CollectionCache.TryGetValue(layerName, out Layer speckleLayer))
    {
      return speckleLayer;
    }
    else
    {
      if (tr.GetObject(entity.LayerId, OpenMode.ForRead) is LayerTableRecord autocadLayer)
      {
        speckleLayer = new Layer(layerName) { applicationId = autocadLayer.Handle.ToString() };
        CollectionCache[layerName] = speckleLayer;
        layer = autocadLayer;
      }
      else
      {
        // POC: this shouldn't happen, but we should probably throw
      }
    }
    return speckleLayer;
  }

  /// <summary>
  /// Will create a layer with the provided name, or, if it finds an existing one, will "purge" all objects from it.
  /// This ensures we're creating the new objects we've just received rather than overlaying them.
  /// </summary>
  /// <returns>The name of the existing or created layer</returns>
  public string CreateLayerForReceive(Collection[] layerPath, string baseLayerPrefix)
  {
    string[] namePath = layerPath.Select(c => c.name).ToArray();
    string layerName = _autocadContext.RemoveInvalidChars(baseLayerPrefix + string.Join("-", namePath));
    if (!_uniqueLayerNames.Add(layerName))
    {
      return layerName;
    }

    // get the color and material if any, of the leaf collection with a color
    AutocadColor? layerColor = null;
    ObjectId layerMaterial = ObjectId.Null;
    if (_colorManager.ObjectColorsIdMap.Count > 0 || _materialManager.ObjectMaterialsIdMap.Count > 0)
    {
      bool foundColor = false;
      bool foundMaterial = false;

      // Goes up the tree to find any potential parent layer that has a material/color
      for (int j = layerPath.Length - 1; j >= 0; j--)
      {
        string layerId = layerPath[j].applicationId ?? layerPath[j].id;

        if (!foundColor)
        {
          foundColor = _colorManager.ObjectColorsIdMap.TryGetValue(layerId, out layerColor);
        }

        if (!foundMaterial)
        {
          foundMaterial = _materialManager.ObjectMaterialsIdMap.TryGetValue(layerId, out layerMaterial);
        }

        if (foundColor && foundMaterial)
        {
          break;
        }
      }
    }

    Doc.LockDocument();
    using Transaction transaction = Doc.TransactionManager.StartTransaction();

    LayerTable? layerTable =
      transaction.TransactionManager.GetObject(Doc.Database.LayerTableId, OpenMode.ForRead) as LayerTable;
    LayerTableRecord layerTableRecord = new() { Name = layerName };

    if (layerColor is not null)
    {
      layerTableRecord.Color = layerColor;
    }

    if (layerMaterial != ObjectId.Null)
    {
      layerTableRecord.MaterialId = layerMaterial;
    }

    bool hasLayer = layerTable != null && layerTable.Has(layerName);
    if (hasLayer)
    {
      TypedValue[] tvs = [new((int)DxfCode.LayerName, layerName)];
      SelectionFilter selectionFilter = new(tvs);
      SelectionSet selectionResult = Doc.Editor.SelectAll(selectionFilter).Value;
      if (selectionResult == null)
      {
        return layerName;
      }

      foreach (SelectedObject selectedObject in selectionResult)
      {
        transaction.GetObject(selectedObject.ObjectId, OpenMode.ForWrite).Erase();
      }

      return layerName;
    }

    layerTable?.UpgradeOpen();
    layerTable?.Add(layerTableRecord);
    transaction.AddNewlyCreatedDBObject(layerTableRecord, true);
    transaction.Commit();

    return layerName;
  }

  public void DeleteAllLayersByPrefix(string prefix)
  {
    Doc.LockDocument();
    using Transaction transaction = Doc.TransactionManager.StartTransaction();

    var layerTable = (LayerTable)transaction.TransactionManager.GetObject(Doc.Database.LayerTableId, OpenMode.ForRead);
    foreach (var layerId in layerTable)
    {
      var layer = (LayerTableRecord)transaction.GetObject(layerId, OpenMode.ForRead);
      var layerName = layer.Name;
      if (layer.Name.Contains(prefix))
      {
        // Delete objects from this layer
        TypedValue[] tvs = [new((int)DxfCode.LayerName, layerName)];
        SelectionFilter selectionFilter = new(tvs);
        SelectionSet selectionResult = Doc.Editor.SelectAll(selectionFilter).Value;
        if (selectionResult == null)
        {
          continue;
        }
        foreach (SelectedObject selectedObject in selectionResult)
        {
          transaction.GetObject(selectedObject.ObjectId, OpenMode.ForWrite).Erase();
        }
        // Delete layer
        layer.UpgradeOpen();
        layer.Erase();
      }
    }
    transaction.Commit();
  }

  /// <summary>
  /// Creates a layer filter for the just received model, grouped under a top level filter "Speckle". Note: manual close and open of the layer properties panel required (it's an acad thing).
  /// This comes in handy to quickly access the layers created for this specific model.
  /// </summary>
  /// <param name="projectName"></param>
  /// <param name="modelName"></param>
  public void CreateLayerFilter(string projectName, string modelName)
  {
    using var docLock = Doc.LockDocument();
    string filterName = _autocadContext.RemoveInvalidChars($@"{projectName}-{modelName}");
    LayerFilterTree layerFilterTree = Doc.Database.LayerFilters;
    LayerFilterCollection? layerFilterCollection = layerFilterTree.Root.NestedFilters;
    LayerFilter? groupFilter = null;

    // Find existing layer filter if exists
    foreach (LayerFilter existingFilter in layerFilterCollection)
    {
      if (existingFilter.Name == _layerFilterName)
      {
        groupFilter = existingFilter;
        break;
      }
    }

    // Create new one unless exists
    if (groupFilter == null)
    {
      groupFilter = new LayerFilter() { Name = "Speckle", FilterExpression = $"NAME==\"SPK-*\"" };
      layerFilterCollection.Add(groupFilter);
    }

    string layerFilterExpression = $"NAME==\"SPK-{filterName}*\"";
    foreach (LayerFilter lf in groupFilter.NestedFilters)
    {
      if (lf.Name == filterName)
      {
        lf.FilterExpression = layerFilterExpression;
        return;
      }
    }
    var layerFilter = new LayerFilter() { Name = filterName, FilterExpression = layerFilterExpression };
    groupFilter.NestedFilters.Add(layerFilter);
    Doc.Database.LayerFilters = layerFilterTree;
  }

  /// <summary>
  /// Gets a valid collection representing a layer for a given context.
  /// </summary>
  /// <param name="context"></param>
  /// <returns>A new Speckle Layer object</returns>
  public Collection[] GetLayerPath(TraversalContext context)
  {
    Collection[] collectionBasedPath = context.GetAscendantOfType<Collection>().Reverse().ToArray();

    if (collectionBasedPath.Length == 0)
    {
      string[] path = context.GetPropertyPath().Reverse().ToArray();
      collectionBasedPath = [new Collection(string.Join("-", path))];
    }

    return collectionBasedPath;
  }
}
