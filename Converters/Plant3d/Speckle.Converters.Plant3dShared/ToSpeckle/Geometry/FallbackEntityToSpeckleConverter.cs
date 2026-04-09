using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Common.Registration;

//using Speckle.Objects.Data;
//using Speckle.Sdk.Models;

namespace Speckle.Converters.Plant3dShared.ToSpeckle.Geometry;

/// <summary>
/// Fallback converter for any ADB.Entity that doesn't have a specific Plant3D converter.
/// Uses a rank lower than SPECKLE_DEFAULT_RANK so specific converters always win.
/// </summary>
[NameAndRankValue(typeof(ADB.Entity), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK - 1)]
public class FallbackEntityToSpeckleConverter(IConverterManager<IToSpeckleTopLevelConverter> converterManager)
  : Plant3dEntityToSpeckleConverter(converterManager);
