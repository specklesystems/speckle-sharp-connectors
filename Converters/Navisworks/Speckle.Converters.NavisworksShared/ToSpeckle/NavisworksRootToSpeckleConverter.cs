﻿using Microsoft.Extensions.Logging;
using Speckle.Converter.Navisworks.Helpers;
using Speckle.Converter.Navisworks.Settings;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Common.Registration;
using Speckle.Sdk.Common;

namespace Speckle.Converter.Navisworks.ToSpeckle;

/// <summary>
/// Converts Navisworks ModelItem objects to Speckle Base objects.
/// </summary>
public class NavisworksRootToSpeckleConverter : IRootToSpeckleConverter
{
  private readonly IConverterManager<IToSpeckleTopLevelConverter> _toSpeckle;
  private readonly IConverterSettingsStore<NavisworksConversionSettings> _settingsStore;
  private readonly ILogger<NavisworksRootToSpeckleConverter> _logger;

  public NavisworksRootToSpeckleConverter(
    IConverterSettingsStore<NavisworksConversionSettings> settingsStore,
    ILogger<NavisworksRootToSpeckleConverter> logger,
    IConverterManager<IToSpeckleTopLevelConverter> toSpeckle
  )
  {
    _toSpeckle = toSpeckle;
    _logger = logger;
    _settingsStore = settingsStore;
  }

  public BaseResult Convert(object target)
  {
    if (target == null)
    {
      throw new ArgumentNullException(nameof(target));
    }

    if (target is not NAV.ModelItem modelItem)
    {
      return BaseResult.NoConversion($"The target object is not a ModelItem. It's a ${target.GetType()}.");
    }

    Type type = target.GetType();

    var converterResult = _toSpeckle.ResolveConverter(type, true);
    if (converterResult.IsFailure)
    {
      return BaseResult.NoConversion(converterResult.Message);
    }

    var result = converterResult.Converter.NotNull().Convert(modelItem);

    if (result.IsSuccess)
    {
      result.Value.applicationId = ElementSelectionHelper.ResolveModelItemToIndexPath(modelItem);
    }

    return result;
  }
}
