﻿using Rhino.DocObjects;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Rhino.ToSpeckle.TopLevel;

[NameAndRankValue(typeof(PointCloudObject), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class PointCloudObjectToSpeckleTopLevelConverter
  : RhinoObjectToSpeckleTopLevelConverter<PointCloudObject, RG.PointCloud, SOG.Pointcloud>
{
  public PointCloudObjectToSpeckleTopLevelConverter(ITypedConverter<RG.PointCloud, SOG.Pointcloud> conversion)
    : base(conversion) { }

  protected override RG.PointCloud GetTypedGeometry(PointCloudObject input) => input.PointCloudGeometry;
}
