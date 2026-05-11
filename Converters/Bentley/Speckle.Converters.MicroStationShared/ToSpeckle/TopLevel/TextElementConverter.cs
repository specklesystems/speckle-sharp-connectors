using Speckle.Converter.MicroStation.Settings;
using Speckle.Converters.Common;
using Speckle.Sdk.Models;
using MgdText = Bentley.DgnPlatformNET.Elements.TextElement;

namespace Speckle.Converter.MicroStation.ToSpeckle.TopLevel;

/// <summary>
/// Converts a managed <see cref="MgdText"/> into a Speckle <see cref="Base"/> carrying the
/// text content. Speckle's typed <c>Text</c> object requires a Plane / origin / alignment
/// description that the managed <c>TextElement</c> doesn't expose directly — those live in
/// <c>TextString</c> obtained via <c>GetTextPart(TextPartId)</c>. For now we ship a lightweight
/// Base with the text value; upgrading to typed Speckle.Text with proper plane / placement is
/// a follow-up.
/// </summary>
public class TextElementConverter(IConverterSettingsStore<MicroStationConversionSettings> settingsStore)
{
  public Base Convert(MgdText mgdText)
  {
    var s = settingsStore.Current;
    var applicationId = ((ulong)mgdText.ElementId).ToString();

    string textValue = "";
    try
    {
      var ids = mgdText.GetTextPartIds(new Bentley.DgnPlatformNET.TextQueryOptions());
      if (ids != null)
      {
        // GetTextPart returns the first text-block payload; the managed API has no direct
        // "GetText" string accessor, so we walk the part ids in order.
        foreach (var id in ids)
        {
          var block = mgdText.GetTextPart(id);
          if (block != null)
          {
            textValue = block.ToString() ?? "";
            if (!string.IsNullOrEmpty(textValue))
            {
              break;
            }
          }
        }
      }
    }
    catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
    {
      // Defensive: text-string read paths can throw on some annotation-scaled elements.
      _ = ex;
    }

    return new Base
    {
      applicationId = applicationId,
      ["type"] = "Text",
      ["value"] = textValue,
      ["units"] = s.SpeckleUnits,
    };
  }
}
