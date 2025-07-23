namespace Speckle.Connectors.Rhino.HostApp;

public static class RhinoUtils
{
  public static string CleanBlockDefinitionName(string str)
  {
    return ReplaceChars(str, @"\/", "_");
  }

  // Cleans up layer names to be "rhino" proof. Note this can be improved, as "()[] and {}" are illegal only at the start.
  // https://docs.mcneel.com/rhino/6/help/en-us/index.htm#information/namingconventions.htm?Highlight=naming
  public static string CleanLayerName(string str)
  {
    str = ReplaceChars(str, @"[](){}", "");
    return ReplaceChars(str, @":;", "-");
  }

  private static string ReplaceChars(string str, string invalidChars, string replaceString)
  {
    foreach (char c in invalidChars)
    {
      str = str.Replace(c.ToString(), replaceString);
    }

    return str;
  }
}
