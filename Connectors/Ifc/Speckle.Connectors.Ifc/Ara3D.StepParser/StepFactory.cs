namespace Ara3D.StepParser;

public static unsafe class StepFactory
{
  public static StepList GetAttributes(this StepRawInstance inst, byte* lineEnd)
  {
    if (!inst.IsValid())
      return default;
    var ptr = inst.Type.End();
    var token = StepTokenizer.ParseToken(ptr, lineEnd);
    // TODO: there is a potential bug here when the line is split across multiple line
    return CreateAggregate(ref token, lineEnd);
  }

  public static StepValue Create(ref StepToken token, byte* end)
  {
    switch (token.Type)
    {
      case StepTokenType.String:
        return StepString.Create(token);

      case StepTokenType.Symbol:
        return StepSymbol.Create(token);

      case StepTokenType.Id:
        return StepId.Create(token);

      case StepTokenType.Redeclared:
        return StepRedeclared.Create(token);

      case StepTokenType.Unassigned:
        return StepUnassigned.Create(token);

      case StepTokenType.Number:
        return StepNumber.Create(token);

      case StepTokenType.Ident:
        var span = token.Span;
        StepTokenizer.ParseNextToken(ref token, end);
        var attr = CreateAggregate(ref token, end);
        return new StepEntity(span, attr);

      case StepTokenType.BeginGroup:
        return CreateAggregate(ref token, end);

      case StepTokenType.None:
      case StepTokenType.Whitespace:
      case StepTokenType.Comment:
      case StepTokenType.Unknown:
      case StepTokenType.LineBreak:
      case StepTokenType.EndOfLine:
      case StepTokenType.Definition:
      case StepTokenType.Separator:
      case StepTokenType.EndGroup:
      default:
        throw new Exception($"Cannot convert token type {token.Type} to a StepValue");
    }
  }

  public static StepList CreateAggregate(ref StepToken token, byte* end)
  {
    var values = new List<StepValue>();
    StepTokenizer.EatWSpace(ref token, end);
    if (token.Type != StepTokenType.BeginGroup)
      throw new Exception("Expected '('");

    while (StepTokenizer.ParseNextToken(ref token, end))
    {
      switch (token.Type)
      {
        // Advance past comments, whitespace, and commas
        case StepTokenType.Comment:
        case StepTokenType.Whitespace:
        case StepTokenType.LineBreak:
        case StepTokenType.Separator:
        case StepTokenType.None:
          continue;

        // Expected end of group
        case StepTokenType.EndGroup:
          return new StepList(values);
      }

      var curValue = Create(ref token, end);
      values.Add(curValue);
    }

    throw new Exception("Unexpected end of input");
  }
}
