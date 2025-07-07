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
  /// Properties on the wrapper and wrapped Base (DataObject) are kept in sync here.
  /// </summary>
  public SpecklePropertyGroupGoo Properties { get; set; } = new();

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
}
