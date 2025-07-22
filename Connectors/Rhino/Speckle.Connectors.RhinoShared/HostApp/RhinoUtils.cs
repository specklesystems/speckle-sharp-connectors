namespace Speckle.Connectors.Rhino.HostApp;

public class RhinoUtils
{
  public string CleanBaseLayerName(string str)
  {
    //return ReplaceChars(str, @"\/[](){}:;?*", "_");
    return ReplaceChars(str, @"\/", "_");
  }

  public string CleanLayerName(string str)
  {
    //return ReplaceChars(str, @"[](){}:;", "");
    str = ReplaceChars(str, @"[](){}", "");
    return ReplaceChars(str, @":;", "-");
  }

  private string ReplaceChars(string str, string invalidChars, string replaceString)
  {
    foreach (char c in invalidChars)
    {
      str = str.Replace(c.ToString(), replaceString);
    }

    return str;
  }
}
