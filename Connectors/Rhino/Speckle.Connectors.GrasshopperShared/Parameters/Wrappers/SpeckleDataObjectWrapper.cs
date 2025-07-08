using Grasshopper.Kernel.Types;
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

  // TODO: CNX-2094
  // public virtual void DrawPreview()
  // public void DrawPreviewRaw()

  // TODO: CNX-2095
  // public virtual void Bake()

  /// <summary>
  /// Creates a deep copy of this wrapper.
  /// </summary>
  public SpeckleDataObjectWrapper DeepCopy() =>
    new()
    {
      Base = DataObject.ShallowCopy(),
      Geometries = [.. Geometries],
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
}
