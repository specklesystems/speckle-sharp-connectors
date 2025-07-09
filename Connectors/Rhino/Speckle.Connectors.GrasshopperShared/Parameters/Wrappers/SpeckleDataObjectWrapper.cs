using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Display;
using Speckle.Sdk;
using Speckle.Sdk.Models;
using DataObject = Speckle.Objects.Data.DataObject;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

/// <summary>
/// Wrapper around a data object and its converted speckle equivalent.
/// </summary>
public class SpeckleDataObjectWrapper : SpeckleWrapper, ISpeckleCollectionObject
{
  /// <summary>
  /// Name on the wrapper and wrapped Base (DataObject) are kept in sync here.
  /// DataObject.name is the single source of truth - all geometry names inherit from it.
  /// </summary>
  public override string Name
  {
    get => DataObject.name;
    set
    {
      DataObject.name = value;
      SyncGeometryNames(); // Only sync names when name changes
    }
  }

  /// <summary>
  /// The wrapped DataObject.
  /// </summary>
  public DataObject DataObject { get; set; }

  /// <summary>
  /// Validated gateway to the typed property (validates and delegates to DataObject).
  /// </summary>
  public override required Base Base
  {
    get => DataObject;
    set
    {
      if (value is not DataObject dataObject)
      {
        throw new ArgumentException("Cannot create data object wrapper from a non-DataObject Base");
      }
      DataObject = dataObject;
    }
  }

  /// <summary>
  /// Contains a list of <see cref="SpeckleGeometryWrapper"/>.
  /// </summary>
  /// <remarks>
  /// A list of the wrappers as opposed to the geometry bases allows us to hold on to the color and material information.
  /// However, this does make syncing of name, props etc. more challenging.
  /// </remarks>
  public List<SpeckleGeometryWrapper> Geometries { get; set; } = [];

  private List<string> _path = [];

  /// <summary>
  /// The list of collection names that forms the full path to this object.
  /// </summary>
  public List<string> Path
  {
    get => _path;
    set
    {
      _path = value;
      SyncGeometryPath();
    }
  }

  private SpeckleCollectionWrapper? _parent;

  /// <summary>
  /// Reference to the parent collection wrapper.
  /// </summary>
  public SpeckleCollectionWrapper? Parent
  {
    get => _parent;
    set
    {
      _parent = value;
      SyncGeometryParent();
    }
  }

  /// <summary>
  /// Try to keep DataObject.properties as source of truth.
  /// </summary>
  public SpecklePropertyGroupGoo Properties
  {
    get => new(DataObject.properties);
    set
    {
      DataObject.properties = value.Unwrap();
      SyncGeometryProperties(value); // Pass existing goo, only sync properties
    }
  }

  public override IGH_Goo CreateGoo() => new SpeckleDataObjectWrapperGoo(this);

  public override string ToString() =>
    $"Speckle Data Object : {(string.IsNullOrWhiteSpace(Name) ? Base.speckle_type : Name)}";

  /// <summary>
  /// Draws preview for all geometries contained in this data object.
  /// </summary>
  public virtual void DrawPreview(IGH_PreviewArgs args, bool isSelected = false)
  {
    // iterate through all geometries and delegate to their existing preview logic
    foreach (var geometry in Geometries)
    {
      geometry.DrawPreview(args, isSelected);
    }
  }

  /// <summary>
  /// Draws raw preview for all geometries using a specific material.
  /// </summary>
  public void DrawPreviewRaw(DisplayPipeline display, DisplayMaterial material)
  {
    foreach (var geometry in Geometries)
    {
      geometry.DrawPreviewRaw(display, material);
    }
  }

  /// <summary>
  /// Bakes the DataObject as a Rhino group containing all the display geometries.
  /// </summary>
  /// <param name="doc">Rhino doc to bake into</param>
  /// <param name="objIds">Collection to store created objects GUIDs</param>
  /// <param name="bakeLayerIndex">Layer index to bake geometries to (-1 for automatic)</param>
  /// <param name="layersAlreadyCreated">Indicates whether layers have already been created or not</param>
  /// <param name="baseLayerName">Base layer name for group naming</param>
  public virtual void Bake(
    RhinoDoc doc,
    List<Guid> objIds,
    int bakeLayerIndex = -1,
    bool layersAlreadyCreated = false,
    string? baseLayerName = null
  )
  {
    // handles layer creation (if needed)
    if (!layersAlreadyCreated && bakeLayerIndex < 0 && Path.Count > 0 && Parent != null)
    {
      bakeLayerIndex = Parent.Bake(doc, objIds, false);
      if (bakeLayerIndex < 0)
      {
        return; // failed to create layers
      }
    }

    if (Geometries.Count == 0)
    {
      return; // nothing to bake
    }

    // bake all display geometries as individual objects
    List<Guid> geometryIds = [];

    foreach (SpeckleGeometryWrapper geometryWrapper in Geometries)
    {
      if (geometryWrapper.GeometryBase != null)
      {
        // geometry wrappers should already be synced via prop setters, assume we're in a consistent state
        List<Guid> currentGeometryIds = [];
        geometryWrapper.Bake(doc, currentGeometryIds, bakeLayerIndex, true);
        geometryIds.AddRange(currentGeometryIds);
      }
    }

    // create a group for all geometries
    if (geometryIds.Count > 0)
    {
      string groupName = CreateGroupName(baseLayerName);

      try
      {
        int groupIndex = doc.Groups.Add(groupName, geometryIds);

        if (groupIndex >= 0)
        {
          var group = doc.Groups.FindIndex(groupIndex);
          objIds.Add(group.Id); // add group ID first
        }
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        // group creation failed - continue like RhinoGroupBaker pattern
        // causes: invalid object IDs? duplicate names? doc state issues?
      }

      // always add individual geometry IDs (whether group creation succeeded or failed)
      objIds.AddRange(geometryIds);
    }
  }

  /// <summary>
  /// Creates a deep copy of this wrapper.
  /// </summary>
  public SpeckleDataObjectWrapper DeepCopy() =>
    new()
    {
      Base = DataObject.ShallowCopy(),
      Geometries = [.. Geometries.Select(g => g.DeepCopy())],
      Properties = Properties,
      ApplicationId = ApplicationId,
      Name = Name,
      Path = [.. Path],
      Parent = Parent
    };

  /// <summary>
  /// Syncs geometry names to match the DataObject name.
  /// </summary>
  private void SyncGeometryNames()
  {
    foreach (var geometry in Geometries)
    {
      geometry.Name = DataObject.name;
    }
  }

  /// <summary>
  /// Syncs geometry properties to match the DataObject properties.
  /// </summary>
  private void SyncGeometryProperties(SpecklePropertyGroupGoo propertyGoo)
  {
    foreach (var geometry in Geometries)
    {
      geometry.Properties = propertyGoo; // Reuse the passed goo
    }
  }

  /// <summary>
  /// Syncs geometry paths.
  /// </summary>
  private void SyncGeometryPath()
  {
    foreach (var geometry in Geometries)
    {
      geometry.Path = [.. Path];
    }
  }

  /// <summary>
  /// Syncs geometry parents.
  /// </summary>
  private void SyncGeometryParent()
  {
    foreach (var geometry in Geometries)
    {
      geometry.Parent = Parent;
    }
  }

  /// <summary>
  /// Creates a descriptive group name
  /// </summary>
  private string CreateGroupName(string? baseLayerName)
  {
    // Reference: RhinoGroupBaker.BakeGroups pattern:
    // var groupName = (groupProxy.name ?? "No Name Group") + $" ({baseLayerName})";

    string groupName = !string.IsNullOrEmpty(Name) ? Name : "No Name DataObject";

    if (!string.IsNullOrEmpty(baseLayerName))
    {
      return $"{groupName} ({baseLayerName})";
    }

    return groupName;
  }
}
