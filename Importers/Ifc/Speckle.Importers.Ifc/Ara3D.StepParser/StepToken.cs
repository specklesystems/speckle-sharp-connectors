using System.Diagnostics;
using Ara3D.Buffers;

namespace Speckle.Importers.Ifc.Ara3D.StepParser;

public readonly struct StepToken
{
  public readonly ByteSpan Span;
  public readonly StepTokenType Type;

  public StepToken(ByteSpan span, StepTokenType type)
  {
    Span = span;
    Debug.Assert(span.Length > 0);
    Type = type;
  }
}
