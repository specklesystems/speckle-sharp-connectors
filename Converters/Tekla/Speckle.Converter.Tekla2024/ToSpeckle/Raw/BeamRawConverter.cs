﻿using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;
using SOG = Speckle.Objects.Geometry;
using TG = Tekla.Structures.Geometry3d;
using Tekla.Structures.Model;

namespace Speckle.Converter.Tekla2024.ToSpeckle.Raw;

public class BeamRawConverter: ITypedConverter<Beam, Base>
{
  private readonly ITypedConverter<TG.Point, SOG.Point> _pointConverter;
  private readonly IConverterSettingsStore<TeklaConversionSettings> _settingsStore;
  public Base Convert(Beam target)
  {
    return new Base() { applicationId = target.Name , properties = target.StartPoint};
  }
}
