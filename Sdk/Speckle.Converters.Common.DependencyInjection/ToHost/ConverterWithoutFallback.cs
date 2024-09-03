﻿using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Speckle.Converters.Common.Extensions;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Common.DependencyInjection.ToHost;

// POC: CNX-9394 Find a better home for this outside `DependencyInjection` project
/// <summary>
/// Provides an implementation for <see cref="IRootToHostConverter"/>
/// that resolves a <see cref="IToHostTopLevelConverter"/> via the injected <see cref="IConverterResolver{TConverter}"/>
/// </summary>
/// <seealso cref="ConverterWithFallback"/>
public sealed class ConverterWithoutFallback : IRootToHostConverter
{
  private readonly IConverterResolver<IToHostTopLevelConverter> _toHost;
  private readonly ILogger _logger;

  public ConverterWithoutFallback(
    IConverterResolver<IToHostTopLevelConverter> converterResolver,
    ILogger<ConverterWithoutFallback> logger
  )
  {
    _toHost = converterResolver;
    _logger = logger;
  }

  public object Convert(Base target)
  {
    if (!TryGetConverter(target.GetType(), out IToHostTopLevelConverter? converter))
    {
      throw new NotSupportedException($"No conversion found for {target.GetType()}");
    }

    object result = converter.ConvertAndLog(target, _logger);
    return result;
  }

  internal bool TryGetConverter(Type target, [NotNullWhen(true)] out IToHostTopLevelConverter? result)
  {
    // Direct conversion if a converter is found
    var objectConverter = _toHost.GetConversionForType(target);
    if (objectConverter != null)
    {
      result = objectConverter;
      return true;
    }

    result = null;
    return false;
  }
}
