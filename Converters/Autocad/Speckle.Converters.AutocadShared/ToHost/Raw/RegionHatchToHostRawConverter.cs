using Speckle.Converters.Common.Objects;
using Speckle.Objects;

namespace Speckle.Converters.Autocad.ToHost.Raw;

public class RegionHatchToHostRawConverter : ITypedConverter<SOG.Region, ADB.Hatch>
{
  private readonly ITypedConverter<ICurve, ADB.Curve> _curveConverter;

  public RegionHatchToHostRawConverter(ITypedConverter<ICurve, ADB.Curve> curveConverter)
  {
    _curveConverter = curveConverter;
  }

  public ADB.Hatch Convert(SOG.Region target)
  {
    // Add converted boundary to the segmentCollection
    ADB.Curve boundaryCurve = _curveConverter.Convert(target.boundary);

    // Add converted loops to the list if segmentCollections
    var loopsCurves = new List<ADB.Curve>();
    foreach (var loop in target.innerLoops)
    {
      loopsCurves.Add(_curveConverter.Convert(loop));
    }

    // add all boundary segments to the ADB.DBObjectCollection
    ADB.ObjectIdCollection boundaryDBObjColl = new();
    boundaryDBObjColl.Add(boundaryCurve.ObjectId);

    using (ADB.Hatch acHatch = new())
    {
      // Set the properties of the hatch object
      // Associative must be set after the hatch object is appended to the
      // block table record and before AppendLoop
      acHatch.SetHatchPattern(ADB.HatchPatternType.PreDefined, "ANSI31");
      // acHatch.Associative = true;
      acHatch.AppendLoop(ADB.HatchLoopTypes.Outermost, boundaryDBObjColl);
      acHatch.EvaluateHatch(true);

      return acHatch;
    }
  }
}
