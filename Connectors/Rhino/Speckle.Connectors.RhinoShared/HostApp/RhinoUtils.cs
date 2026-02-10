using System.Text;

namespace Speckle.Connectors.Rhino.HostApp;

public static class RhinoUtils
{
  private static readonly HashSet<char> s_skipChars = ['[', ']', '(', ')', '{', '}'];
  private static readonly HashSet<char> s_replaceWithHyphen = [':', ';'];

  public static string CleanBlockDefinitionName(string str) => str.Replace('/', '_').Replace('\\', '_');

  // Cleans up layer names to be "rhino" proof. Note this can be improved, as "()[] and {}" are illegal only at the start.
  // https://docs.mcneel.com/rhino/6/help/en-us/index.htm#information/namingconventions.htm?Highlight=naming
  public static string CleanLayerName(string str)
  {
    var sb = new StringBuilder(str.Length);
    bool lastWasSpace = true;

    foreach (char c in str)
    {
      if (char.IsControl(c))
      {
        continue; // skip control characters (shoutout cnx-2809)
      }

      if (s_skipChars.Contains(c))
      {
        continue; // skip brackets
      }

      if (s_replaceWithHyphen.Contains(c))
      {
        sb.Append('-');
        lastWasSpace = false;
        continue;
      }

      // Collapse double spaces into one and skip leading spaces.
      // e.g. "  Items  Name " -> "Items Name"
      if (c == ' ')
      {
        if (!lastWasSpace)
        {
          sb.Append(c);
          lastWasSpace = true;
        }
        continue;
      }

      sb.Append(c);
      lastWasSpace = false;
    }

    if (sb.Length > 0 && sb[^1] == ' ')
    {
      sb.Length--;
    }

    return sb.ToString();
  }
}
