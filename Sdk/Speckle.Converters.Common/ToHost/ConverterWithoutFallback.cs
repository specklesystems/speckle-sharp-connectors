using Microsoft.Extensions.Logging;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Common.Registration;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Common.ToHost;

// POC: CNX-9394 Find a better home for this outside `DependencyInjection` project
/// <summary>
/// Provides an implementation for <see cref="IRootToHostConverter"/>
/// that resolves a <see cref="IToHostTopLevelConverter"/> via the injected <see cref="IConverterManager"/>
/// </summary>
/// <seealso cref="ConverterWithFallback"/>
public sealed class ConverterWithoutFallback : IRootToHostConverter
{
  private readonly IConverterManager _toHost;
  private readonly ILogger _logger;

  public ConverterWithoutFallback(
    IConverterManager converterResolver,
    ILogger<ConverterWithoutFallback> logger
  )
  {
    _toHost = converterResolver;
    _logger = logger;
  }

  public object Convert(Base target)
  {
    var type = target.GetType();
    var (objectConverter, destinationType) = _toHost.GetHostConverter(type);
    var interfaceType = typeof(ITypedConverter<,>).MakeGenericType(type, destinationType);
    var convertedObject = interfaceType.GetMethod("Convert")!.Invoke(objectConverter, new object[] { target })!;
    return convertedObject;
  }
}
