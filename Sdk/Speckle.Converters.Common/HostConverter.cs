using Speckle.Converters.Common.Objects;
using Speckle.Converters.Common.Registration;
using Speckle.InterfaceGenerator;
using Speckle.Sdk.Common;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Common;

[GenerateAutoInterface]
public class HostConverter(IConverterManager converterManager) : ConverterBase(converterManager), IHostConverter
{
  public object Convert(Base target) => Convert(target, (manager, sourceType) => manager.GetHostConverter(sourceType));
}

public abstract class ConverterBase(IConverterManager converterManager)
{
  private readonly Dictionary<Type, Type> _sourceTypeToInvokerType = new();
  private static readonly object s_emptyObject = new();
  private readonly object[] _invokerArgs = [s_emptyObject];

  protected object Convert(object target, Func<IConverterManager, Type, (object, Type)> getConverter)
  {
    var type = target.GetType();
    (object objectConverter, Type destinationType) = getConverter(converterManager, type);
    if (!_sourceTypeToInvokerType.TryGetValue(type, out var invokerType))
    {
      invokerType = typeof(ITypedConverter<,>).MakeGenericType(type, destinationType);
      _sourceTypeToInvokerType.Add(type, invokerType);
    }
    _invokerArgs[0] = target;
    try
    {
      var convertedObject = invokerType.GetMethod("Convert").NotNull().Invoke(objectConverter, _invokerArgs).NotNull();
      return convertedObject;
    }
    finally
    {
      _invokerArgs[0] = s_emptyObject;
    }
  }
}
