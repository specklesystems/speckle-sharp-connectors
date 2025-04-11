namespace Speckle.Importers.Ifc.Ara3D.StepParser;

public static unsafe class StepFactory
{
  public static StepList GetAttributes(this StepRawInstance inst, byte* lineEnd)
  {
    if (!inst.IsValid())
    {
      return StepList.CreateDefault();
    }

    var ptr = inst.Type.End();
    var token = StepTokenizer.ParseToken(ptr, lineEnd);
    // TODO: there is a potential bug here when the line is split across multiple line
    return CreateAggregate(ref token, lineEnd);
  }

  public static StepValue Create(ref StepToken token, byte* end)
  {
    switch (token.Type)
    {
      case StepTokenType.STRING:
        return StepString.Create(token);

      case StepTokenType.SYMBOL:
        return StepSymbol.Create(token);

      case StepTokenType.ID:
        return StepId.Create(token);

      case StepTokenType.REDECLARED:
        return StepRedeclared.Create(token);

      case StepTokenType.UNASSIGNED:
        return StepUnassigned.Create(token);

      case StepTokenType.NUMBER:
        return StepNumber.Create(token);

      case StepTokenType.IDENT:
        var span = token.Span;
        StepTokenizer.ParseNextToken(ref token, end);
        var attr = CreateAggregate(ref token, end);
        return new StepEntity(span, attr);

      case StepTokenType.BEGIN_GROUP:
        return CreateAggregate(ref token, end);

      case StepTokenType.NONE:
      case StepTokenType.WHITESPACE:
      case StepTokenType.COMMENT:
      case StepTokenType.UNKNOWN:
      case StepTokenType.LINE_BREAK:
      case StepTokenType.END_OF_LINE:
      case StepTokenType.DEFINITION:
      case StepTokenType.SEPARATOR:
      case StepTokenType.END_GROUP:
      default:
        throw new SpeckleIfcException($"Cannot convert token type {token.Type} to a StepValue");
    }
  }

  public static StepList CreateAggregate(ref StepToken token, byte* end)
  {
    var values = new List<StepValue>();
    StepTokenizer.EatWSpace(ref token, end);
    if (token.Type != StepTokenType.BEGIN_GROUP)
    {
      throw new SpeckleIfcException("Expected '('");
    }

    while (StepTokenizer.ParseNextToken(ref token, end))
    {
      switch (token.Type)
      {
        // Advance past comments, whitespace, and commas
        case StepTokenType.COMMENT:
        case StepTokenType.WHITESPACE:
        case StepTokenType.LINE_BREAK:
        case StepTokenType.SEPARATOR:
        case StepTokenType.NONE:
          continue;

        // Expected end of group
        case StepTokenType.END_GROUP:
          return new StepList(values);
      }

      var curValue = Create(ref token, end);
      values.Add(curValue);
    }

    throw new SpeckleIfcException("Unexpected end of input");
  }
}
