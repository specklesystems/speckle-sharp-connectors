
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Common.Registration;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Common;

public interface IRootToSpeckleConverter
{
  Base Convert(object target);
}

public class RootToSpeckleConverter : IRootToSpeckleConverter
{
  private readonly IConverterManager _toSpeckle;

  public RootToSpeckleConverter(IConverterManager toSpeckle)
  {
    _toSpeckle = toSpeckle;
  }

  public Base Convert(object target)
  {    
    Type type = target.GetType();

    var objectConverter = _toSpeckle.GetHostConverter(type);
    var interfaceType = typeof(ITypedConverter<,>).MakeGenericType(type, typeof(Base));
    var convertedObject = interfaceType.GetMethod("Convert")!.Invoke(objectConverter, new object[] { target })!;

    return (Base)convertedObject;
  }
}
