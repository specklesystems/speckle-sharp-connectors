using System.Xml.Linq;
using Autodesk.Navisworks.Api.Interop;
using static Autodesk.Navisworks.Api.Interop.LcUOption;

namespace Speckle.Converter.Navisworks.Services;

public static class QuickPropertyDefinitionsUtil
{
  private const string QUICK_PROPERTIES_FILE_NAME = "Quick Properties.xml";

  // Navisworks 2026 = Nw23 in PackageContents.xml. Property Set profile Quick Properties start here.
  private const int PROPERTY_SET_QUICK_PROPERTIES_MIN_RUNTIME_MAJOR = 23;

  public static IReadOnlyList<QuickPropertyDefinition> Load()
  {
    List<QuickPropertyDefinition> definitions = UsesPropertySetProfileQuickProperties()
      ? TryLoadFromPropertySetProfile()
      : TryLoadFromGlobalOptions();

    return definitions.Count > 0 ? definitions : DefaultDefinitions();
  }

  private static bool UsesPropertySetProfileQuickProperties() =>
    NavisworksApp.Version.RuntimeMajor >= PROPERTY_SET_QUICK_PROPERTIES_MIN_RUNTIME_MAJOR;

  private static List<QuickPropertyDefinition> TryLoadFromPropertySetProfile()
  {
    var definitions = new List<QuickPropertyDefinition>();

    try
    {
      string? propertySetsDirectory = TryGetPropertySetsDirectory();
      if (propertySetsDirectory == null || !Directory.Exists(propertySetsDirectory))
      {
        return definitions;
      }

      string quickPropertiesPath = Path.Combine(propertySetsDirectory, QUICK_PROPERTIES_FILE_NAME);
      if (!File.Exists(quickPropertiesPath))
      {
        return definitions;
      }

      TryParsePropertySetFile(quickPropertiesPath, definitions);
    }
    catch (IOException)
    {
      definitions.Clear();
    }
    catch (UnauthorizedAccessException)
    {
      definitions.Clear();
    }
    catch (System.Xml.XmlException)
    {
      definitions.Clear();
    }

    return definitions;
  }

  private static void TryParsePropertySetFile(string filePath, List<QuickPropertyDefinition> definitions)
  {
    XDocument document = XDocument.Load(filePath);
    if (document.Root == null)
    {
      return;
    }

    foreach (XElement entry in document.Root.Elements("Entries").Elements("Entry"))
    {
      QuickPropertyDefinition? definition = TryParsePropertySetEntry(entry);
      if (definition == null)
      {
        continue;
      }

      if (definitions.Any(existing => MatchesDefinition(existing, definition)))
      {
        continue;
      }

      definitions.Add(definition);
    }
  }

  private static QuickPropertyDefinition? TryParsePropertySetEntry(XElement entry)
  {
    XElement? categoryElement = entry.Element("Category");
    if (categoryElement == null)
    {
      return null;
    }

    string categoryDisplayName = categoryElement.Value;
    if (string.IsNullOrWhiteSpace(categoryDisplayName))
    {
      return null;
    }

    XElement? propertyElement = entry.Element("Property");
    string? propertyDisplayName = NullIfWhiteSpace(propertyElement?.Value);
    return propertyDisplayName == null
      ? new QuickPropertyDefinition(
        categoryDisplayName.Trim(),
        null,
        NullIfWhiteSpace(categoryElement.Attribute("Internal")?.Value),
        null
      )
      : new QuickPropertyDefinition(
        categoryDisplayName.Trim(),
        propertyDisplayName,
        NullIfWhiteSpace(categoryElement.Attribute("Internal")?.Value),
        NullIfWhiteSpace(propertyElement?.Attribute("Internal")?.Value)
      );
  }

  private static bool MatchesDefinition(QuickPropertyDefinition left, QuickPropertyDefinition right) =>
    string.Equals(left.CategoryDisplayName, right.CategoryDisplayName, StringComparison.OrdinalIgnoreCase)
    && (
      left.IsCategoryOnly && right.IsCategoryOnly
      || string.Equals(left.PropertyDisplayName, right.PropertyDisplayName, StringComparison.OrdinalIgnoreCase)
    );

  private static string? TryGetPropertySetsDirectory()
  {
    string productName = NavisworksApp.Version.RuntimeProductName;
    if (string.IsNullOrWhiteSpace(productName))
    {
      return null;
    }

    string profileFolderName = productName.StartsWith("Autodesk ", StringComparison.OrdinalIgnoreCase)
      ? productName["Autodesk ".Length..]
      : productName;

    return Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
      "Autodesk",
      profileFolderName,
      "PropertySets"
    );
  }

  private static List<QuickPropertyDefinition> TryLoadFromGlobalOptions()
  {
    var definitions = new List<QuickPropertyDefinition>();

    try
    {
      using var optionLock = new LcUOptionLock();
      LcUOptionSet rootOptions = GetRoot(optionLock);
      LcUOptionSet? quickPropertyDefinitions = rootOptions.GetSubOptions("interface.smart_tags.definitions");
      if (quickPropertyDefinitions == null)
      {
        return definitions;
      }

      int definitionCount = quickPropertyDefinitions.GetNumOptions();
      for (int index = 0; index < definitionCount; index++)
      {
        LcUOptionSet? entry = quickPropertyDefinitions.GetSubOptions(index);
        if (entry == null)
        {
          continue;
        }

        NAV.NamedConstant? category = TryGetNamedConstant(entry, "category");
        NAV.NamedConstant? property = TryGetNamedConstant(entry, "property");
        if (category == null || property == null)
        {
          continue;
        }

        string categoryDisplayName = category.DisplayName;
        string propertyDisplayName = property.DisplayName;
        if (string.IsNullOrWhiteSpace(categoryDisplayName) || string.IsNullOrWhiteSpace(propertyDisplayName))
        {
          continue;
        }

        definitions.Add(
          new QuickPropertyDefinition(
            categoryDisplayName.Trim(),
            propertyDisplayName.Trim(),
            NullIfWhiteSpace(category.Name),
            NullIfWhiteSpace(property.Name)
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
    catch (ObjectDisposedException)
    {
      definitions.Clear();
    }
    catch (InvalidOperationException)
    {
      definitions.Clear();
    }

    return definitions;
  }

  private static NAV.NamedConstant? TryGetNamedConstant(LcUOptionSet options, string optionName)
  {
    LcUNameRefPtr? nameRef = options.GetName(optionName);
    return nameRef?.GetPtr();
  }

  private static IReadOnlyList<QuickPropertyDefinition> DefaultDefinitions() =>
    [new QuickPropertyDefinition("Item", "Display Name", null, null)];

  private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
}
