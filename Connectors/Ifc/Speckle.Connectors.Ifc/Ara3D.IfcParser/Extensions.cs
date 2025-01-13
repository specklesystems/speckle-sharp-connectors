using System.Text;
using Ara3D.Buffers;
using Ara3D.StepParser;

namespace Ara3D.IfcParser;

public static class Extensions
{
  public static void Add<TKey, TValue>(
    this IDictionary<TKey, List<TValue>> self,
    TKey key,
    TValue value
  )
  {
    if (!self.ContainsKey(key))
      self[key] = new List<TValue>();
    self[key].Add(value);
  }

  public static uint AsId(this StepValue value) =>
    value is StepUnassigned ? 0u : ((StepId)value).Id;

  public static string AsString(this StepValue value) =>
    value is StepString ss ? ss.AsString()
    : value is StepNumber sn ? sn.Value.ToString()
    : value is StepId si ? si.Id.ToString()
    : value is StepSymbol ssm ? ssm.Name.ToString()
    : "";

  public static double AsNumber(this StepValue value) =>
    value is StepUnassigned ? 0 : ((StepNumber)value).Value;

  public static List<StepValue> AsList(this StepValue value) =>
    value is StepUnassigned ? new List<StepValue>() : ((StepList)value).Values;

  public static List<uint> AsIdList(this StepValue value) =>
    value is StepUnassigned ? new List<uint>() : value.AsList().Select(AsId).ToList();

  // Uses Latin1 encoding (aka ISO-8859-1)
  // Extended characters converted using an IFC specific system
  public static string AsString(this ByteSpan span) =>
    Encoding.Latin1.GetString(span.ToSpan()).IfcToUnicode();

  // https://technical.buildingsmart.org/resources/ifcimplementationguidance/string-encoding/
  public static string IfcToUnicode(this string input)
  {
    if (!input.Contains('\\'))
      return input;

    var output = new StringBuilder();
    var i = 0;
    var length = input.Length;
    while (i < length)
    {
      if (input[i] != '\\')
      {
        // Regular character, append to output
        output.Append(input[i++]);
        continue;
      }

      i++; // Move past '\'
      if (i >= length)
      {
        output.Append('\\');
        break;
      }

      var escapeChar = input[i++];

      if (escapeChar == 'S' && i < length && input[i] == '\\')
      {
        i++; // Move past '\'
        if (i < length)
        {
          var c = input[i++];
          var code = c + 128;
          output.Append((char)code);
        }
        else
        {
          output.Append("\\S\\");
        }
        continue;
      }

      if (escapeChar == 'X')
      {
        if (i < length && input[i] == '\\')
        {
          // Handle \X\XX escape sequence (8-bit character code)
          i++; // Move past '\'
          if (i + 1 < length)
          {
            var hex = input.Substring(i, 2);
            i += 2;
            var code = Convert.ToInt32(hex, 16);
            output.Append((char)code);
          }
          else
          {
            output.Append("\\X\\");
          }

          continue;
        }

        // Handle extended \Xn\...\X0\ escape sequence
        // Skip 'n' until the next '\'
        while (i < length && input[i] != '\\')
          i++;
        if (i < length)
          i++; // Move past '\'

        // Collect hex digits until '\X0\'
        var hexDigits = new StringBuilder();
        while (i + 3 <= length && input.Substring(i, 3) != "\\X0")
        {
          hexDigits.Append(input[i++]);
        }

        if (i + 3 <= length && input.Substring(i, 3) == "\\X0")
        {
          i += 3; // Move past '\X0'
          if (i < length && input[i] == '\\')
            i++; // Move past '\'

          var hexStr = hexDigits.ToString();

          // Process hex digits in chunks of 4 (representing Unicode code points)
          for (var k = 0; k + 4 <= hexStr.Length; k += 4)
          {
            var codeHex = hexStr.Substring(k, 4);
            var code = Convert.ToInt32(codeHex, 16);
            output.Append(char.ConvertFromUtf32(code));
          }
          continue;
        }

        // Invalid format, append as is
        output.Append("\\X");
        continue;
      }

      // Unrecognized escape sequence, append as is
      output.Append('\\').Append(escapeChar);
    }

    return output.ToString();
  }

  public static string AsString(this StepString ss) => ss.Value.AsString();

  public static object ToJsonObject(this StepValue sv)
  {
    switch (sv)
    {
      case StepEntity stepEntity:
      {
        var attr = stepEntity.Attributes;
        if (attr.Values.Count == 0)
          return stepEntity.ToString();

        if (attr.Values.Count == 1)
          return attr.Values[0].ToJsonObject();

        return attr.Values.Select(ToJsonObject).ToList();
      }

      case StepId stepId:
        return stepId.Id;

      case StepList stepList:
        return stepList.Values.Select(ToJsonObject).ToList();

      case StepNumber stepNumber:
        return stepNumber.AsNumber();

      case StepRedeclared stepRedeclared:
        return null;

      case StepString stepString:
        return stepString.AsString();

      case StepSymbol stepSymbol:
        var tmp = stepSymbol.Name.AsString();
        if (tmp == "T")
          return true;
        if (tmp == "F")
          return false;
        return tmp;

      case StepUnassigned stepUnassigned:
        return null;

      default:
        throw new ArgumentOutOfRangeException(nameof(sv));
    }
  }
}
