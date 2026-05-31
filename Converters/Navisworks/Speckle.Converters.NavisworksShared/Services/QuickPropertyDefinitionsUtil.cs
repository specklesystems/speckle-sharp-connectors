using Autodesk.Navisworks.Api.Interop.ComApi;
using ComApiBridge = Autodesk.Navisworks.Api.ComApi.ComApiBridge;

namespace Speckle.Converter.Navisworks.Services;

public static class QuickPropertyDefinitionsUtil
{
  public static IReadOnlyList<QuickPropertyDefinition> Load()
  {
    List<QuickPropertyDefinition> fromSmartTags = TryLoadFromSmartTags();
    return fromSmartTags.Count > 0 ? fromSmartTags : DefaultDefinitions();
  }

  private static List<QuickPropertyDefinition> TryLoadFromSmartTags()
  {
    var definitions = new List<QuickPropertyDefinition>();

    try
    {
      var state = ComApiBridge.State;
      var smartTagOptions = (InwSmartTagsOpts)
        state.ObjectFactory(nwEObjectType.eObjectType_nwSmartTagsOpts, null, null);
      smartTagOptions.Copy();

      InwOpFindConditionsColl conditions = smartTagOptions.Conditions();
      int conditionCount = conditions.Count;
      for (int index = 1; index <= conditionCount; index++)
      {
        if (conditions[index] is not InwOpFindCondition condition)
        {
          continue;
        }

        if (condition.Condition != nwEFindCondition.eFind_HAS_PROP)
        {
          continue;
        }

        string categoryDisplayName = condition.AttributeUserName;
        string propertyDisplayName = condition.PropertyUserName;
        if (string.IsNullOrWhiteSpace(categoryDisplayName) || string.IsNullOrWhiteSpace(propertyDisplayName))
        {
          continue;
        }

        definitions.Add(
          new QuickPropertyDefinition(
            categoryDisplayName.Trim(),
            propertyDisplayName.Trim(),
            NullIfWhiteSpace(condition.AttributeInternalName),
            NullIfWhiteSpace(condition.PropertyInternalName)
          )
        );
      }
    }
    catch (System.Runtime.InteropServices.COMException)
    {
      definitions.Clear();
    }
    catch (InvalidCastException)
    {
      definitions.Clear();
    }

    return definitions;
  }

  private static IReadOnlyList<QuickPropertyDefinition> DefaultDefinitions() =>
  [
    new QuickPropertyDefinition("Item", "Display Name", null, null),
  ];

  private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
}
