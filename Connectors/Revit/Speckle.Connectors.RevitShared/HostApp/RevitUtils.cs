namespace Speckle.Connectors.Revit.HostApp;

public class RevitUtils
{
  private const string REVIT_INVALID_CHARS = @"<>/\:;""?*|=,‘";

  public string RemoveInvalidChars(string str)
  {
    foreach (char c in REVIT_INVALID_CHARS)
    {
      str = str.Replace(c.ToString(), string.Empty);
    }

    return str;
  }
}
