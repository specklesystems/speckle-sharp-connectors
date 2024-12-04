using Speckle.Objects;

namespace Speckle.Converter.Navisworks.Models;

/// <summary>
/// Interface for Speckle objects converted from Navisworks.
/// </summary>
public interface INavisworksObject : IDataObject
{
  Dictionary<string, object?> Properties { get; }
}

[SSM.SpeckleType("Objects.Navisworks.ModelItem")]
internal sealed class NavisworksModelItem : SSM.Base, INavisworksObject
{
  public required string name { get; init; }
  public required Dictionary<string, object?> Properties { get; init; }

  [SSM.DetachProperty]
  public required List<NavisworksModelItem> Elements { get; set; }

  // Implement the `IDataObject.displayValue` property with an empty list
  IReadOnlyList<SSM.Base> IDataObject.displayValue => [];
}

[SSM.SpeckleType("Objects.Navisworks.ModelGeometry")]
internal sealed class NavisworksModelGeometry : SSM.Base, INavisworksObject
{
  public required string name { get; init; }
  public required Dictionary<string, object?> Properties { get; init; }

  [SSM.DetachProperty]
  public required IReadOnlyList<SSM.Base> displayValue { get; init; }
}
