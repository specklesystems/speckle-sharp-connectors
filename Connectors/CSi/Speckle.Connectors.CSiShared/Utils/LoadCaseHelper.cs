namespace Speckle.Connectors.CSiShared.Utils;

public static class LoadCaseHelper
{
  public static List<string> GetLoadCasesAndCombinations(cSapModel sapModel)
  {
    var loadCasesAndCombos = new List<string>();

    try
    {
      // Check if model is loaded to prevent crashes
      var modelFilename = sapModel.GetModelFilename();
      if (string.IsNullOrEmpty(modelFilename))
      {
        return loadCasesAndCombos; // Return empty list if no model
      }

      // Get Load Cases
      int numberItems = 0;
      string[]? names = null;

      int ret = sapModel.LoadCases.GetNameList(ref numberItems, ref names);
      if (ret == 0 && names != null)
      {
        for (int i = 0; i < numberItems; i++)
        {
          loadCasesAndCombos.Add(names[i]);
        }
      }

      // Get Load Combinations
      numberItems = 0;
      names = null;
      ret = sapModel.RespCombo.GetNameList(ref numberItems, ref names);
      if (ret == 0 && names != null)
      {
        for (int i = 0; i < numberItems; i++)
        {
          loadCasesAndCombos.Add(names[i]);
        }
      }
    }
    catch (System.Runtime.InteropServices.COMException)
    {
      // Return empty list on COM errors to prevent crashes
      return new List<string>();
    }
    catch (System.InvalidOperationException)
    {
      // Return empty list on invalid operations to prevent crashes
      return new List<string>();
    }

    return loadCasesAndCombos.Distinct().OrderBy(x => x).ToList();
  }
}
