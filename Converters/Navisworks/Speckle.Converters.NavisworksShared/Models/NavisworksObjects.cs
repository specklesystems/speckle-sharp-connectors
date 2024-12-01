using System.Diagnostics.CodeAnalysis;
using Speckle.Objects;

namespace Speckle.Converter.Navisworks.Models;

/// <summary>
/// Interface for Speckle objects converted from Navisworks.
/// </summary>
public interface INavisworksObject : IDataObject
{
  /// <summary>
  /// Adds a property to the object.
  /// </summary>
  /// <param name="key">The property key.</param>
  /// <param name="value">The property value.</param>
  void AddProperty(string key, object? value);

  /// <summary>
  /// Retrieves a property value by key.
  /// </summary>
  /// <param name="key">The property key.</param>
  /// <returns>The property value, or null if the key does not exist.</returns>
  object? GetProperty(string key);
}

/// <summary>
/// Represents a non-geometry Speckle object.
/// </summary>
internal sealed class NavisworksObject(string name) : SSM.Base, INavisworksObject
{
  public string name { get; init; } = name;

  private readonly Dictionary<string, object?> _properties = [];

  public void AddProperty(string key, object? value) => _properties[key] = value;

  public object? GetProperty(string key) => _properties.TryGetValue(key, out var value) ? value : null;

  [SSM.DetachProperty]
  [SuppressMessage("Style", "IDE1006:Naming Styles")]
  [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Local")]
  [SuppressMessage("ReSharper", "InconsistentNaming")]
  public required List<NavisworksObject> elements { get; set; } = [];

  // Implement the `IDataObject.displayValue` property with an empty list
  IReadOnlyList<SSM.Base> IDataObject.displayValue => [];
}

/// <summary>
/// Represents a geometry-based Speckle object.
/// </summary>
internal sealed class NavisworksGeometryObject(IReadOnlyList<SSM.Base> displayValue, string name)
  : SSM.Base,
    INavisworksObject
{
  IReadOnlyList<SSM.Base> IDataObject.displayValue => displayValue;
  public string name { get; init; } = name;

  private readonly Dictionary<string, object?> _properties = [];

  public void AddProperty(string key, object? value) => _properties[key] = value;

  public object? GetProperty(string key) => _properties.TryGetValue(key, out var value) ? value : null;
}
