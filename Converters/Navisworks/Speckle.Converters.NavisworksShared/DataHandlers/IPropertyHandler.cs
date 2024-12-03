namespace Speckle.Converter.Navisworks.ToSpeckle.PropertyHandlers;

/// <summary>
/// Defines the contract for handling property assignment to Speckle objects.
/// </summary>
public interface IPropertyHandler
{
  /// <summary>
  /// Processes and adds properties to a Speckle object from a Navisworks model item.
  /// </summary>
  void AssignProperties(SSM.Base speckleObject, NAV.ModelItem modelItem);
}
