using System.Diagnostics;
using System.Runtime.CompilerServices;
using Ara3D.Buffers;

namespace Speckle.Connectors.Ifc.Ara3D.StepParser;

public static class StepTokenizer
{
  public static readonly StepTokenType[] TokenLookup = CreateTokenLookup();

  public static readonly bool[] IsNumberLookup = CreateNumberLookup();

  public static readonly bool[] IsIdentLookup = CreateIdentLookup();

  public static StepTokenType[] CreateTokenLookup()
  {
    var r = new StepTokenType[256];
    for (var i = 0; i < 256; i++)
      r[i] = GetTokenType((byte)i);
    return r;
  }

  public static bool[] CreateNumberLookup()
  {
    var r = new bool[256];
    for (var i = 0; i < 256; i++)
      r[i] = IsNumberChar((byte)i);
    return r;
  }

  public static bool[] CreateIdentLookup()
  {
    var r = new bool[256];
    for (var i = 0; i < 256; i++)
      r[i] = IsIdentOrDigitChar((byte)i);
    return r;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static StepTokenType LookupToken(byte b) => TokenLookup[b];

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static bool IsNumberChar(byte b)
  {
    switch (b)
    {
      case (byte)'0':
      case (byte)'1':
      case (byte)'2':
      case (byte)'3':
      case (byte)'4':
      case (byte)'5':
      case (byte)'6':
      case (byte)'7':
      case (byte)'8':
      case (byte)'9':
      case (byte)'E':
      case (byte)'e':
      case (byte)'+':
      case (byte)'-':
      case (byte)'.':
        return true;
    }

    return false;
  }

  public static StepTokenType GetTokenType(byte b)
  {
    switch (b)
    {
      case (byte)'0':
      case (byte)'1':
      case (byte)'2':
      case (byte)'3':
      case (byte)'4':
      case (byte)'5':
      case (byte)'6':
      case (byte)'7':
      case (byte)'8':
      case (byte)'9':
      case (byte)'+':
      case (byte)'-':
        return StepTokenType.Number;

      case (byte)' ':
      case (byte)'\t':
        return StepTokenType.Whitespace;

      case (byte)'\n':
      case (byte)'\r':
        return StepTokenType.LineBreak;

      case (byte)'\'':
      case (byte)'"':
        return StepTokenType.String;

      case (byte)'.':
        return StepTokenType.Symbol;

      case (byte)'#':
        return StepTokenType.Id;

      case (byte)';':
        return StepTokenType.EndOfLine;

      case (byte)'(':
        return StepTokenType.BeginGroup;

      case (byte)'=':
        return StepTokenType.Definition;

      case (byte)')':
        return StepTokenType.EndGroup;

      case (byte)',':
        return StepTokenType.Separator;

      case (byte)'$':
        return StepTokenType.Unassigned;

      case (byte)'*':
        return StepTokenType.Redeclared;

      case (byte)'/':
        return StepTokenType.Comment;

      case (byte)'a':
      case (byte)'b':
      case (byte)'c':
      case (byte)'d':
      case (byte)'e':
      case (byte)'f':
      case (byte)'g':
      case (byte)'h':
      case (byte)'i':
      case (byte)'j':
      case (byte)'k':
      case (byte)'l':
      case (byte)'m':
      case (byte)'n':
      case (byte)'o':
      case (byte)'p':
      case (byte)'q':
      case (byte)'r':
      case (byte)'s':
      case (byte)'t':
      case (byte)'u':
      case (byte)'v':
      case (byte)'w':
      case (byte)'x':
      case (byte)'y':
      case (byte)'z':
      case (byte)'A':
      case (byte)'B':
      case (byte)'C':
      case (byte)'D':
      case (byte)'E':
      case (byte)'F':
      case (byte)'G':
      case (byte)'H':
      case (byte)'I':
      case (byte)'J':
      case (byte)'K':
      case (byte)'L':
      case (byte)'M':
      case (byte)'N':
      case (byte)'O':
      case (byte)'P':
      case (byte)'Q':
      case (byte)'R':
      case (byte)'S':
      case (byte)'T':
      case (byte)'U':
      case (byte)'V':
      case (byte)'W':
      case (byte)'X':
      case (byte)'Y':
      case (byte)'Z':
      case (byte)'_':
        return StepTokenType.Ident;

      default:
        return StepTokenType.Unknown;
    }
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static bool IsWhiteSpace(byte b) => b == ' ' || b == '\t';

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static bool IsLineBreak(byte b) => b == '\n' || b == '\r';

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static bool IsIdent(byte b) => b >= 'A' && b <= 'Z' || b >= 'a' && b <= 'z' || b == '_';

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static bool IsDigit(byte b) => b >= '0' && b <= '9';

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static bool IsIdentOrDigitChar(byte b) => IsIdent(b) || IsDigit(b);

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static unsafe byte* AdvancePast(byte* begin, byte* end, string s)
  {
    if (end - begin < s.Length)
      return null;
    foreach (var c in s)
      if (*begin++ != (byte)c)
        return null;
    return begin;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static unsafe StepToken ParseToken(byte* begin, byte* end)
  {
    var cur = begin;
    var tt = InternalParseToken(ref cur, end);
    Debug.Assert(cur < end);
    var span = new ByteSpan(begin, cur);
    return new StepToken(span, tt);
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static unsafe bool EatWSpace(ref StepToken cur, byte* end)
  {
    while (
      cur.Type == StepTokenType.Comment
      || cur.Type == StepTokenType.Whitespace
      || cur.Type == StepTokenType.LineBreak
    )
    {
      if (!ParseNextToken(ref cur, end))
        return false;
    }
    return true;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static unsafe bool ParseNextToken(ref StepToken prev, byte* end)
  {
    var cur = prev.Span.End();
    if (cur >= end)
      return false;
    prev = ParseToken(cur, end);
    return true;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static unsafe StepTokenType InternalParseToken(ref byte* cur, byte* end)
  {
    var type = TokenLookup[*cur++];

    switch (type)
    {
      case StepTokenType.Ident:
        while (IsIdentLookup[*cur])
          cur++;
        break;

      case StepTokenType.String:
        // usually it is as single quote,
        // but in rare cases it could be a double quote
        var quoteChar = *(cur - 1);
        while (cur < end)
        {
          if (*cur++ == quoteChar)
          {
            if (*cur != quoteChar)
              break;
            else
              cur++;
          }
        }

        break;

      case StepTokenType.LineBreak:
        while (IsLineBreak(*cur))
          cur++;
        break;

      case StepTokenType.Number:
        while (IsNumberLookup[*cur])
          cur++;
        break;

      case StepTokenType.Symbol:
        while (*cur++ != '.') { }

        break;

      case StepTokenType.Id:
        while (IsNumberLookup[*cur])
          cur++;
        break;

      case StepTokenType.Comment:
        var prev = *cur++;
        while (cur < end && (prev != '*' || *cur != '/'))
          prev = *cur++;
        cur++;
        break;
    }

    return type;
  }
}
