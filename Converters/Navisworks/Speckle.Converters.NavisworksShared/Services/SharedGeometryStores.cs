using Speckle.InterfaceGenerator;
using Speckle.Sdk.Models;

namespace Speckle.Converter.Navisworks.Services;

/// <summary>
/// Singleton service that stores mesh objects with HashSet-like behavior for deduplication.
/// First discovered geometry on conversion will add, subsequent conversions can check
/// for existence and skip if needed.
/// </summary>
[GenerateAutoInterface]
public class SharedGeometryStores : ISharedGeometryStore
{
  private readonly HashSet<Base> _geometries = new();
  private readonly Dictionary<string, Base> _geometriesByApplicationId = new();
  private readonly object _lock = new();

  /// <summary>
  /// Gets a read-only collection of all stored geometries.
  /// </summary>
  public IReadOnlyCollection<Base> Geometries
  {
    get
    {
      lock (_lock)
      {
        return _geometries.ToList().AsReadOnly();
      }
    }
  }

  /// <summary>
  /// Adds a geometry to the store if it doesn't already exist.
  /// </summary>
  /// <param name="geometry">The geometry to add.</param>
  /// <returns>True if the geometry was added, false if it already existed.</returns>
  public bool Add(Base geometry)
  {
    if (geometry == null)
    {
      throw new ArgumentNullException(nameof(geometry));
    }

    if (string.IsNullOrEmpty(geometry.applicationId))
    {
      throw new ArgumentException("Geometry must have an applicationId for deduplication", nameof(geometry));
    }

    lock (_lock)
    {
      if (geometry.applicationId != null && _geometriesByApplicationId.ContainsKey(geometry.applicationId))
      {
        return false; // Already exists
      }

      _geometries.Add(geometry);
      if (geometry.applicationId != null)
      {
        _geometriesByApplicationId[geometry.applicationId] = geometry;
      }

      return true; // Added successfully
    }
  }

  /// <summary>
  /// Checks if a geometry with the specified application ID already exists in the store.
  /// </summary>
  /// <param name="applicationId">The application ID to check for.</param>
  /// <returns>True if a geometry with the application ID exists, false otherwise.</returns>
  public bool Contains(string applicationId)
  {
    if (string.IsNullOrEmpty(applicationId))
    {
      return false;
    }

    lock (_lock)
    {
      return _geometriesByApplicationId.ContainsKey(applicationId);
    }
  }

  /// <summary>
  /// Clears all stored geometries for a new conversion session.
  /// </summary>
  public void Clear()
  {
    lock (_lock)
    {
      _geometries.Clear();
      _geometriesByApplicationId.Clear();
    }
  }
}
