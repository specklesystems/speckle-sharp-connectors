using Speckle.Converters.Common.Objects;
using Speckle.Converters.Common.Registration;
using Speckle.InterfaceGenerator;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Common;

[GenerateAutoInterface]
public class RootToSpeckleConverter : IRootToSpeckleConverter
{
  private readonly IConverterManager<IToSpeckleTopLevelConverter> _toSpeckle;

  public RootToSpeckleConverter(IConverterManager<IToSpeckleTopLevelConverter> toSpeckle)
  {
    _toSpeckle = toSpeckle;
  }

  public Base Convert(object target)
  {
    Type type = target.GetType();
    try
    {
      var objectConverter = _toSpeckle.ResolveConverter(type.Name); //poc: would be nice to have supertypes resolve

      if (objectConverter == null)
      {
        throw new NotSupportedException($"No conversion found for {type.Name}");
      }
      var convertedObject = objectConverter.Convert(target);

      return convertedObject;
    }
    catch (SpeckleConversionException e)
    {
      Console.WriteLine(e);
      throw; // Just rethrowing for now, Logs may be needed here.
    }
  }
}
