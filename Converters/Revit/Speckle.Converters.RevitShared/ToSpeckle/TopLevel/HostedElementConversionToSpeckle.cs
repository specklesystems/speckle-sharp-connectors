using Autodesk.Revit.DB;
using Speckle.Converters.Common;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Sdk;
using Speckle.Sdk.Models;

namespace Speckle.Converters.RevitShared.ToSpeckle;

[Obsolete(
  "Do not use while as we're rethinking hosted element relationships. This class will most likely go away.",
  true
)]
public class HostedElementConversionToSpeckle
{
  private readonly IRootToSpeckleConverter _converter;
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;

  public HostedElementConversionToSpeckle(
    IRootToSpeckleConverter converter,
    IConverterSettingsStore<RevitConversionSettings> converterSettings
  )
  {
    _converter = converter;
    _converterSettings = converterSettings;
  }

  public IEnumerable<Base> ConvertHostedElements(IEnumerable<ElementId> hostedElementIds)
  {
    foreach (var elemId in hostedElementIds)
    {
      Element element = _converterSettings.Current.Document.GetElement(elemId);

      Base @base;
      try
      {
        @base = _converter.Convert(element);
      }
      catch (SpeckleException)
      {
        // POC: Handling this correctly should be high priority, as it affects the quality of the report.
        // POC: we could aggregate all host element exceptions and throw a single one in the end.
        continue;
      }

      yield return @base;
    }
  }
}
