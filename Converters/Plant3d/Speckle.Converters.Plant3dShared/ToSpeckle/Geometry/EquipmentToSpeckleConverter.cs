using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Common.Registration;

namespace Speckle.Converters.Plant3dShared.ToSpeckle.Geometry;

[NameAndRankValue(typeof(PP.PnP3dObjects.Equipment), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class EquipmentToSpeckleConverter(IConverterManager<IToSpeckleTopLevelConverter> converterManager)
  : Plant3dEntityToSpeckleConverter(converterManager);
