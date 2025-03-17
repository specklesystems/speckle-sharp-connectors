namespace Speckle.Connectors.Revit.HostApp;

public class RevitUtils
{
  // see Revit Parameter Name Limitations here
  // https://www.autodesk.com/support/technical/article/caas/tsarticles/ts/3RVyShGL7OMlDJPLasuKFL.html
  private const string REVIT_INVALID_CHARS = @"\:{}[]|;<>?`~";

  public string RemoveInvalidChars(string str)
  {
    foreach (char c in REVIT_INVALID_CHARS)
    {
      str = str.Replace(c.ToString(), string.Empty);
    }

    return str;
  }
}
