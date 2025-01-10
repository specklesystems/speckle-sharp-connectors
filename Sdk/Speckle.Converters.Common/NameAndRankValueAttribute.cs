namespace Speckle.Converters.Common;

// POC: maybe better to put in utils/reflection
[AttributeUsage(AttributeTargets.Class)]
public sealed class NameAndRankValueAttribute(Type type, int rank) : Attribute
{
  // DO NOT CHANGE! This is the base, lowest rank for a conversion
  public const int SPECKLE_DEFAULT_RANK = 0;

  public Type Type { get; private set; } = type;
  public int Rank { get; private set; } = rank;
}
