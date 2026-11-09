namespace Speckle.Converters.CSiShared.ToSpeckle.Helpers;

public interface IShellThicknessResolver
{
  /// <summary>Section thickness in model units; NaN when the section has none (openings, "None").</summary>
  double GetThickness(string sectionName);
}
