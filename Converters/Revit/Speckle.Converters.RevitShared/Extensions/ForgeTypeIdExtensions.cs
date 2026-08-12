using Autodesk.Revit.DB;

namespace Speckle.Converters.RevitShared.Extensions;

public static class ForgeTypeIdExtensions
{
  public static string? GetSymbol(this ForgeTypeId forgeTypeId)
  {
    if (!FormatOptions.CanHaveSymbol(forgeTypeId))
    {
      return null;
    }
    var validSymbols = FormatOptions.GetValidSymbols(forgeTypeId);
    var typeId = validSymbols.Where(x => !x.Empty());
    foreach (DB.ForgeTypeId symbolId in typeId)
    {
      return LabelUtils.GetLabelForSymbol(symbolId);
    }
    return null;
  }

  public static string ToUniqueString(this ForgeTypeId forgeTypeId)
  {
    return forgeTypeId.TypeId;
  }

  /// <summary>
  /// Extracts a language-stable unit identifier from a unit ForgeTypeId, e.g. "squareMeters" from
  /// "autodesk.unit.unit:squareMeters-1.0.1". Unlike <c>LabelUtils.GetLabelForUnit</c>, the returned value
  /// does not depend on Revit's display language, and unlike the raw <c>ForgeTypeId.TypeId</c> it does not
  /// include the schema version suffix, which can differ between Revit releases.
  /// </summary>
  /// <returns>The unit identifier, or null if the ForgeTypeId is empty or not in the expected format.</returns>
  public static string? GetStableUnitsId(this ForgeTypeId forgeTypeId)
  {
    if (forgeTypeId.Empty())
    {
      return null;
    }

    // TypeId format: "autodesk.unit.unit:squareMeters-1.0.1" (namespace:name-version)
    string typeId = forgeTypeId.TypeId;
    int nameStart = typeId.IndexOf(':') + 1;
    int versionStart = typeId.LastIndexOf('-');
    if (nameStart <= 0 || versionStart <= nameStart)
    {
      return typeId;
    }

    return typeId[nameStart..versionStart];
  }
}
