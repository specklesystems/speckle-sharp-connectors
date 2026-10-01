namespace Speckle.Converters.MicroStation.Services;

/// <summary>
/// Shared-cell placement math — the port of dgnextract's <c>sharedPlacement</c> (GetSharedPlacement,
/// verified-by-construction against the baked path there): a definition-local point p maps to
/// <c>M·(p − defOrigin) + refOrigin</c>, where M is the reference's FULL transformation
/// (rotation·scale). As a transform, the linear part is M and the translation is
/// <c>refOrigin − M·defOrigin</c> (UOR frame; the GeometryMapper handles global origin + scaling).
/// <para>
/// The managed <c>DisplayableElement.GetBasisTransform</c> is NOT this — the first live send showed
/// it drops the instance scale (definitions rendered at raw local size, e.g. a 275-ft door), so M is
/// composed manually: <c>GetOrientation() · diag(Scale)</c>. Mirror placements survive via negative
/// scale components; a general skew matrix beyond that is not representable here (dgnextract warns
/// on non-uniform scale for the same reason).
/// </para>
/// </summary>
public static class SharedCellPlacement
{
  /// <summary>
  /// Resolves the instance's definition element: <c>GetDefinition(file)</c> first, then the file's
  /// named shared-cell definitions by cell name (<c>SharedCellQuery.FindDefinitionByName</c>) —
  /// office-furniture files resolve only through the fallback.
  /// </summary>
  public static MgdElement? FindDefinition(MgdElements.SharedCellElement instance)
  {
    try
    {
      DPN.DgnFile? file = instance.DgnModel?.GetDgnFile();
      if (file == null)
      {
        return null;
      }
      MgdElement? definition = instance.GetDefinition(file);
      if (definition != null)
      {
        return definition;
      }
      string name = instance.CellName;
      if (string.IsNullOrEmpty(name))
      {
        return null;
      }
      return MgdElements.SharedCellQuery.FindDefinitionByName(name, file);
    }
    catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
    {
      return null;
    }
  }

  public static bool TryCompute(
    MgdElements.SharedCellElement instance,
    MgdElement definition,
    out BG.DTransform3d placement
  )
  {
    placement = default;
    try
    {
      if (definition is not MgdElements.DisplayableElement displayableDefinition)
      {
        return false;
      }

      instance.GetOrientation(out BG.DMatrix3d m);
      BG.DVector3d scale = instance.Scale;
      m.ScaleColumnsInPlace(NonZero(scale.X), NonZero(scale.Y), NonZero(scale.Z));

      instance.GetTransformOrigin(out BG.DPoint3d refOrigin);
      displayableDefinition.GetTransformOrigin(out BG.DPoint3d defOrigin);

      BG.DVector3d md = m.Multiply(
        new BG.DVector3d
        {
          X = defOrigin.X,
          Y = defOrigin.Y,
          Z = defOrigin.Z,
        }
      );
      placement = BG.DTransform3d.FromMatrixAndTranslation(
        m,
        new BG.DVector3d
        {
          X = refOrigin.X - md.X,
          Y = refOrigin.Y - md.Y,
          Z = refOrigin.Z - md.Z,
        }
      );
      return true;
    }
    catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
    {
      return false;
    }
  }

  private static double NonZero(double v) => Math.Abs(v) < 1e-12 ? 1.0 : v;
}
