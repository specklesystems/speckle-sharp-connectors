using System.Diagnostics;
using Ara3D.Buffers;
using Ara3D.Utils;

namespace Speckle.Connectors.Ifc.Ara3D.StepParser;

/// <summary>
/// The base class of the different type of value items that can be found in a STEP file.
/// * Entity
/// * List
/// * String
/// * Symbol
/// * Unassigned token
/// * Redeclared token
/// * Number
/// </summary>
public class StepValue;

public class StepEntity : StepValue
{
  public readonly ByteSpan EntityType;
  public readonly StepList Attributes;

  public StepEntity(ByteSpan entityType, StepList attributes)
  {
    Debug.Assert(!entityType.IsNull());
    EntityType = entityType;
    Attributes = attributes;
  }

  public override string ToString() => $"{EntityType}{Attributes}";
}

public class StepList : StepValue
{
  public readonly List<StepValue> Values;

  public StepList(List<StepValue> values) => Values = values;

  public override string ToString() => $"({Values.JoinStringsWithComma()})";

  public static StepList CreateDefault() => new(new List<StepValue>());
}

public class StepString : StepValue
{
  public readonly ByteSpan Value;

  public static StepString Create(StepToken token)
  {
    var span = token.Span;
    Debug.Assert(token.Type == StepTokenType.String);
    Debug.Assert(span.Length >= 2);
    Debug.Assert(span.First() == '\'' || span.First() == '"');
    Debug.Assert(span.Last() == '\'' || span.Last() == '"');
    return new StepString(span.Trim(1, 1));
  }

  public StepString(ByteSpan value) => Value = value;

  public override string ToString() => $"'{Value}'";
}

public class StepSymbol : StepValue
{
  public readonly ByteSpan Name;

  public StepSymbol(ByteSpan name) => Name = name;

  public override string ToString() => $".{Name}.";

  public static StepSymbol Create(StepToken token)
  {
    Debug.Assert(token.Type == StepTokenType.Symbol);
    var span = token.Span;
    Debug.Assert(span.Length >= 2);
    Debug.Assert(span.First() == '.');
    Debug.Assert(span.Last() == '.');
    return new StepSymbol(span.Trim(1, 1));
  }
}

public class StepNumber : StepValue
{
  public readonly ByteSpan Span;
  public double Value => Span.ToDouble();

  public StepNumber(ByteSpan span) => Span = span;

  public override string ToString() => $"{Value}";

  public static StepNumber Create(StepToken token)
  {
    Debug.Assert(token.Type == StepTokenType.Number);
    var span = token.Span;
    return new(span);
  }
}

public class StepId : StepValue
{
  public readonly uint Id;

  public StepId(uint id) => Id = id;

  public override string ToString() => $"#{Id}";

  public static unsafe StepId Create(StepToken token)
  {
    Debug.Assert(token.Type == StepTokenType.Id);
    var span = token.Span;
    Debug.Assert(span.Length >= 2);
    Debug.Assert(span.First() == '#');
    var id = 0u;
    for (var i = 1; i < span.Length; ++i)
    {
      Debug.Assert(span.Ptr[i] >= '0' && span.Ptr[i] <= '9');
      id = id * 10 + span.Ptr[i] - '0';
    }
    return new StepId(id);
  }
}

public class StepUnassigned : StepValue
{
  public static readonly StepUnassigned Default = new();

  public override string ToString() => "$";

  public static StepUnassigned Create(StepToken token)
  {
    Debug.Assert(token.Type == StepTokenType.Unassigned);
    var span = token.Span;
    Debug.Assert(span.Length == 1);
    Debug.Assert(span.First() == '$');
    return Default;
  }
}

public class StepRedeclared : StepValue
{
  public static readonly StepRedeclared Default = new();

  public override string ToString() => "*";

  public static StepRedeclared Create(StepToken token)
  {
    Debug.Assert(token.Type == StepTokenType.Redeclared);
    var span = token.Span;
    Debug.Assert(span.Length == 1);
    Debug.Assert(span.First() == '*');
    return Default;
  }
}
