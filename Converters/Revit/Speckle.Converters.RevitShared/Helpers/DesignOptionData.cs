namespace Speckle.Converters.RevitShared.Helpers;

/// <summary>
/// Values must match rvextract's (speckle-converters#305, ENG-10900): a desktop publish and a server conversion of
/// the same file must be indistinguishable [ENG-10901].
/// </summary>
public static class DesignOptionData
{
  public const string MAIN_MODEL_NAME = "Main Model";

  // rvextract (ODA) reports the bare option name, so OPTION_NAME comes first. The fallback strips only the English
  // " (primary)" marker Element.Name may carry; a localised marker would survive it.
  public static string OptionName(DB.DesignOption option)
  {
    string? name = option.get_Parameter(DB.BuiltInParameter.OPTION_NAME)?.AsString();
    if (!string.IsNullOrEmpty(name))
    {
      return name!;
    }

    name = option.Name;
    const string PRIMARY_SUFFIX = " (primary)";
    return option.IsPrimary && name.EndsWith(PRIMARY_SUFFIX, StringComparison.Ordinal)
      ? name[..^PRIMARY_SUFFIX.Length]
      : name;
  }

  public static DB.ElementId SetId(DB.DesignOption option) =>
    option.get_Parameter(DB.BuiltInParameter.OPTION_SET_ID)?.AsElementId() ?? DB.ElementId.InvalidElementId;

  public static long IdValue(DB.ElementId id) =>
#if REVIT2024_OR_GREATER
    id.Value;
#else
    id.IntegerValue;
#endif
}
