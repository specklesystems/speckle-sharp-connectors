using System.Diagnostics.CodeAnalysis;

namespace Speckle.Importers.Ifc.Ara3D.StepParser;

[SuppressMessage("Design", "CA1028:Enum Storage should be Int32", Justification = "StepTokens are Bytes")]
public enum StepTokenType : byte
{
  NONE,
  IDENT,
  STRING,
  WHITESPACE,
  NUMBER,
  SYMBOL,
  ID,
  SEPARATOR,
  UNASSIGNED,
  REDECLARED,
  COMMENT,
  UNKNOWN,
  BEGIN_GROUP,
  END_GROUP,
  LINE_BREAK,
  END_OF_LINE,
  DEFINITION,
}
