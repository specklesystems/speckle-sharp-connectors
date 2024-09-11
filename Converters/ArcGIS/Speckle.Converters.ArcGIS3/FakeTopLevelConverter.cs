﻿using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects.Geometry;
using Speckle.Sdk.Models;

namespace Speckle.Converters.ArcGIS3;

[NameAndRankValue(nameof(String), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class FakeTopLevelConverter : IToSpeckleTopLevelConverter, ITypedConverter<String, Point>
{
  public Base Convert(object target) => Convert((String)target);

  public Point Convert(String target)
  {
    return new Point(0, 0, 100) { ["customText"] = target };
  }
}