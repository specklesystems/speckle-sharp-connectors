using Speckle.Converters.Autocad;
using Speckle.Converters.Autocad.ToSpeckle.Raw;
using Speckle.Converters.Common;

namespace Speckle.Converters.AutocadShared.ToSpeckle;

/// <summary>
/// Extracts the annotation payload of text entities (DBText/MText and the attribute flavours that derive from
/// DBText) as plain properties.
/// </summary>
/// <remarks>
/// The text itself rides the SGEO Text geometry blob, which is what the viewer renders and what receive bakes.
/// These properties are the DATA half of the same object: they make the content/height/position queryable in the
/// object explorer and in automations, and they survive even when a consumer can't decode the geometry [ENG-8827].
/// </remarks>
public class TextPropertiesExtractor
{
  private readonly IConverterSettingsStore<AutocadConversionSettings> _settingsStore;

  public TextPropertiesExtractor(IConverterSettingsStore<AutocadConversionSettings> settingsStore)
  {
    _settingsStore = settingsStore;
  }

  /// <summary>
  /// Returns the text properties of <paramref name="entity"/>, or null when it is not a text entity.
  /// </summary>
  public Dictionary<string, object?>? GetTextProperties(ADB.Entity entity) =>
    // MText first: it does NOT derive from DBText, but AttributeReference/AttributeDefinition do, so the
    // DBText arm has to come last to stay the fallback for all single-line flavours.
    entity switch
    {
      ADB.MText mText => FromMText(mText),
      ADB.DBText dbText => FromDBText(dbText),
      _ => null,
    };

  private Dictionary<string, object?> FromMText(ADB.MText target)
  {
    var properties = new Dictionary<string, object?>
    {
      ["value"] = MTextToSpeckleRawConverter.ConvertMTextToPlainText(target.Contents ?? string.Empty),
      ["height"] = target.TextHeight,
      ["rotation"] = target.Rotation,
      ["attachment"] = target.Attachment.ToString(),
      ["position"] = PositionDictionary(target.Location),
    };

    // The raw markup: kept so formatting (fonts, stacked fractions, colour codes) isn't lost to the plain-text
    // flattening. Only worth carrying when it actually differs from the plain value.
    string contents = target.Contents ?? string.Empty;
    if (contents != properties["value"] as string)
    {
      properties["formattedValue"] = contents;
    }
    if (target.Width > 0)
    {
      properties["wrapWidth"] = target.Width;
    }
    AddStyleName(properties, target.TextStyleId);
    return properties;
  }

  private Dictionary<string, object?> FromDBText(ADB.DBText target)
  {
    // Mirror the geometry converter's anchor choice: left-justified text is anchored by Position, everything
    // else by AlignmentPoint.
    bool isLeftJustified = target.Justify == ADB.AttachmentPoint.BaseLeft;
    var properties = new Dictionary<string, object?>
    {
      ["value"] = target.TextString ?? string.Empty,
      ["height"] = target.Height,
      ["rotation"] = target.Rotation,
      ["justification"] = target.Justify.ToString(),
      ["widthFactor"] = target.WidthFactor,
      ["oblique"] = target.Oblique,
      ["position"] = PositionDictionary(isLeftJustified ? target.Position : target.AlignmentPoint),
    };

    // Block attributes carry their tag as the field name the value belongs to — without it a bag of attribute
    // values is unattributable.
    switch (target)
    {
      case ADB.AttributeReference attributeReference:
        properties["tag"] = attributeReference.Tag;
        break;
      case ADB.AttributeDefinition attributeDefinition:
        properties["tag"] = attributeDefinition.Tag;
        break;
      default:
        break;
    }

    AddStyleName(properties, target.TextStyleId);
    return properties;
  }

  private static Dictionary<string, object?> PositionDictionary(AG.Point3d point) =>
    new()
    {
      ["x"] = point.X,
      ["y"] = point.Y,
      ["z"] = point.Z,
    };

  // The style NAME (not its id) is what a consumer can act on. Resolved in its own transaction: this extractor
  // runs after the converter's transaction has been committed (see AutocadRootToSpeckleConverter).
  private void AddStyleName(Dictionary<string, object?> properties, ADB.ObjectId textStyleId)
  {
    if (textStyleId == ADB.ObjectId.Null)
    {
      return;
    }
    using ADB.Transaction tr = _settingsStore.Current.Document.TransactionManager.StartTransaction();
    if (tr.GetObject(textStyleId, ADB.OpenMode.ForRead) is ADB.TextStyleTableRecord record)
    {
      properties["style"] = record.Name;
    }
    tr.Commit();
  }
}
