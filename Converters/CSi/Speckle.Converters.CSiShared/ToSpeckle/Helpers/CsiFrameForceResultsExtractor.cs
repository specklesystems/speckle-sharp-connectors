using Speckle.Converters.Common;

namespace Speckle.Converters.CSiShared.ToSpeckle.Helpers;

public sealed class CsiFrameForceResultsExtractor
{
  private readonly IConverterSettingsStore<CsiConversionSettings> _settingsStore;

  public CsiFrameForceResultsExtractor(IConverterSettingsStore<CsiConversionSettings> settingsStore)
  {
    _settingsStore = settingsStore;
  }

  public void GetResults(List<string> frameNames)
  {
    int nuberResults = 0;
    string[] obj = [];
    double[] objSta = [];
    string[] elm = [];
    double[] elmSta = [];
    string[] loadCase = [];
    string[] stepType = [];
    double[] stepNumber = [];
    double[] p = [];
    double[] v2 = [];
    double[] v3 = [];
    double[] t = [];
    double[] m2 = [];
    double[] m3 = [];

    foreach (string frameName in frameNames)
    {
      int success = _settingsStore.Current.SapModel.Results.FrameForce(
        frameName,
        eItemTypeElm.ObjectElm,
        ref nuberResults,
        ref obj,
        ref objSta,
        ref elm,
        ref elmSta,
        ref loadCase,
        ref stepType,
        ref stepNumber,
        ref p,
        ref v2,
        ref v3,
        ref t,
        ref m2,
        ref m3
      );

      if (success != 0)
      {
        throw new InvalidOperationException("Failed to extract frame forces.");
      }
    }
  }
}
