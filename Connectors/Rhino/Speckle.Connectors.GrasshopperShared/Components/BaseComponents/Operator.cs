namespace Speckle.Connectors.GrasshopperShared.Components.BaseComponents;

internal static class Operator
{
  public enum CompareMethod
  {
    Nothing,
    Equals,
    StartsWith, // <
    EndsWith, // >
    Contains, // ?
    Wildcard, // :
    Regex, // ;
  }

  public static CompareMethod CompareMethodFromPattern(string pattern)
  {
    bool not = false;
    return CompareMethodFromPattern(ref pattern, ref not);
  }

  public static CompareMethod CompareMethodFromPattern(ref string pattern, ref bool not)
  {
    if (pattern is null)
    {
      return CompareMethod.Nothing;
    }

    if (string.IsNullOrEmpty(pattern))
    {
      return CompareMethod.Equals;
    }

    switch (pattern[0])
    {
      case '~':
        not = !not;
        pattern = pattern[1..];
        return CompareMethodFromPattern(ref pattern, ref not);
      case '<':
        pattern = pattern[1..];
        return string.IsNullOrEmpty(pattern) ? CompareMethod.Equals : CompareMethod.StartsWith;
      case '>':
        pattern = pattern[1..];
        return string.IsNullOrEmpty(pattern) ? CompareMethod.Equals : CompareMethod.EndsWith;
      case '?':
        pattern = pattern[1..];
        return string.IsNullOrEmpty(pattern) ? CompareMethod.Equals : CompareMethod.Contains;
      case ':':
        pattern = pattern[1..];
        return string.IsNullOrEmpty(pattern) ? CompareMethod.Equals : CompareMethod.Wildcard;
      case ';':
        pattern = pattern[1..];
        return string.IsNullOrEmpty(pattern) ? CompareMethod.Equals : CompareMethod.Regex;
      default:
        return CompareMethod.Equals;
    }
  }

  public static bool IsSymbolNameLike(this string source, string pattern)
  {
    if (pattern is null)
    {
      return true;
    }

    if (pattern == source)
    {
      return true;
    }

    bool not = false;
    switch (CompareMethodFromPattern(ref pattern, ref not))
    {
      case CompareMethod.Nothing:
        return not ^ false;
      case CompareMethod.Equals:
        return not ^ string.Equals(source, pattern, StringComparison.Ordinal);
      case CompareMethod.StartsWith:
        return not ^ source.StartsWith(pattern, StringComparison.Ordinal);
      case CompareMethod.EndsWith:
        return not ^ source.EndsWith(pattern, StringComparison.Ordinal);
      case CompareMethod.Contains:
        return not ^ (source.IndexOf(pattern, StringComparison.Ordinal) >= 0);
      /*
      case CompareMethod.Wildcard:
        return not
          ^ Microsoft.VisualBasic.CompilerServices.LikeOperator.LikeString(
            source,
            pattern,
            Microsoft.VisualBasic.CompareMethod.Text
          );
        */
      case CompareMethod.Regex:
        var regex = new System.Text.RegularExpressions.Regex(pattern);
        return not ^ regex.IsMatch(source);
    }

    return false;
  }
}
