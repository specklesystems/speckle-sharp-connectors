using Microsoft.Extensions.Logging;
using Speckle.Converters.Common.Extensions;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Common.Registration;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Common.ToHost;

public sealed class ConverterWithoutFallback(
  IConverterManager<IToHostTopLevelConverter> converterResolver,
  ILogger<ConverterWithoutFallback> logger)
  : IRootToHostConverter
{
  private readonly ILogger _logger = logger;

  public HostResult Convert(Base target)
  {
    var converter = converterResolver.ResolveConverter(target.GetType());
    var result = converter.ConvertAndLog(target, _logger);
    return result;
  }
}
