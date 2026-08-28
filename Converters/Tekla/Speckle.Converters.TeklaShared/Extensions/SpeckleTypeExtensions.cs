namespace Speckle.Converters.TeklaShared.Extensions;

public static class SpeckleTypeExtensions
{
  /// <summary>
  /// Gets the object type Tekla itself reports, falling back to the API class name.
  /// </summary>
  /// <remarks>
  /// Tekla's classes are coarser than its object types: beams, columns, panels and footings are all
  /// <see cref="TSM.Beam"/>, plates and slabs are all <see cref="TSM.ContourPlate"/>. Publishing the class name
  /// alone therefore labelled most of a model "Beam". Objects without a type of their own (bolts, rebar, grids)
  /// keep the class name, which is already specific.
  /// </remarks>
  public static string GetSpeckleType(this TSM.ModelObject modelObject) =>
    modelObject switch
    {
      TSM.Beam beam => ToPascalCase(beam.Type.ToString()),
      TSM.PolyBeam polyBeam => ToPascalCase(polyBeam.Type.ToString()),
      // NOTE: the plate type is derived from the material and can come back unknown, which tells us less than the class
      TSM.ContourPlate plate when plate.Type != TSM.ContourPlate.ContourPlateTypeEnum.UNKNOWN => ToPascalCase(
        plate.Type.ToString()
      ),
      _ => modelObject.GetType().Name,
    };

  // tekla reports its types as PAD_FOOTING, we publish them as PadFooting
  private static string ToPascalCase(string teklaType) =>
    string.Concat(
      teklaType
        .Split('_')
        .Where(word => word.Length > 0)
        .Select(word => char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant())
    );
}
