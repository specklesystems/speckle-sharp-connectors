using System.Diagnostics.CodeAnalysis;
using Speckle.Objects;

namespace Speckle.Converter.Navisworks.Models;

/// <summary>
/// Interface for Speckle objects converted from Navisworks.
/// </summary>
public interface INavisworksObject : IDataObject
{
  void AddProperty(string key, object? value);
  object? GetProperty(string key);
}

[SSM.SpeckleType("Objects.Navisworks.ModelItem")]
internal sealed class NavisworksModelItem : SSM.Base, INavisworksObject
{
  public required string name { get; init; }
  private readonly Dictionary<string, object?> _properties = [];

  [SSM.DetachProperty]
  public required List<NavisworksModelItem> elements { get; set; }

  // Implement the `IDataObject.displayValue` property with an empty list
  IReadOnlyList<SSM.Base> IDataObject.displayValue => [];

  public void AddProperty(string key, object? value) => _properties[key] = value;

  public object? GetProperty(string key) => _properties.TryGetValue(key, out var value) ? value : null;
}

[SSM.SpeckleType("Objects.Navisworks.ModelGeometry")]
internal sealed class NavisworksModelGeometry : SSM.Base, INavisworksObject
{
  public required string name { get; init; }
  private readonly Dictionary<string, object?> _properties = [];

  [SSM.DetachProperty]
  public required IReadOnlyList<SSM.Base> displayValue { get; init; }

  public void AddProperty(string key, object? value) => _properties[key] = value;

  public object? GetProperty(string key) => _properties.TryGetValue(key, out var value) ? value : null;
}
