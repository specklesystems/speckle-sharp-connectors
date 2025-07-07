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
    get
    {
      SyncGeometriesToDataObject();
      return DataObject.name;
    }
    set
    {
      DataObject.name = value;
      SyncGeometriesToDataObject();
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

  /// <summary>
  /// Try to keep DataObject.properties as source of truth.
  /// </summary>
  /// <remarks>
  /// Property ownership rules:
  /// - Single geometry → DataObject: Geometry properties become DataObject properties
  /// - DataObject mutation: DataObject properties overwrite all geometry properties
  /// - Multiple geometries → DataObject: First geometry's properties become DataObject properties, others inherit
  /// - Individual geometry mutation: Ignored
  /// </remarks>
  public SpecklePropertyGroupGoo Properties
  {
    get
    {
      SyncGeometriesToDataObject();
      return new SpecklePropertyGroupGoo(DataObject.properties);
    }
    set
    {
      DataObject.properties = value.Unwrap();
      SyncGeometriesToDataObject();
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
      Name = Name
    };

  /// <summary>
  /// Syncs all geometry wrappers' properties and names to match the DataObject.
  /// </summary>
  /// <remarks>
  /// All geometries in the list should reflect the current DataObject state.
  /// </remarks>
  private void SyncGeometriesToDataObject()
  {
    // Create property group once and reuse for all geometries
    SpecklePropertyGroupGoo dataObjectProperties = new(DataObject.properties);

    foreach (var geometry in Geometries)
    {
      // All geometries inherit the same name and properties from DataObject
      geometry.Name = DataObject.name;
      geometry.Properties = dataObjectProperties;
    }
  }
}
