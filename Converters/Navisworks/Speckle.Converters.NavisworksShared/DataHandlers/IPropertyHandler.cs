namespace Speckle.Converter.Navisworks.ToSpeckle.PropertyHandlers;

/// <summary>
/// Defines the contract for handling property assignment to Speckle objects.
/// </summary>
public interface IPropertyHandler
{
  /// <summary>
  /// Gets the properties from a Navisworks model item.
  /// </summary>
  Dictionary<string, object?> GetProperties(NAV.ModelItem modelItem);
}
