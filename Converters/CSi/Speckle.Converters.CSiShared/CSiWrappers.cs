namespace Speckle.Converters.CSiShared;

public interface ICSiWrapper
{
  string Name { get; set; }
  int ObjectType { get; }
}

public abstract class CSiWrapperBase : ICSiWrapper
{
  public required string Name { get; set; }
  public abstract int ObjectType { get; }
}

public class CSiJointWrapper : CSiWrapperBase
{
  public override int ObjectType => 1;
}

public class CSiFrameWrapper : CSiWrapperBase
{
  public override int ObjectType => 2;
}

public class CSiCableWrapper : CSiWrapperBase
{
  public override int ObjectType => 3;
}

public class CSiTendonWrapper : CSiWrapperBase
{
  public override int ObjectType => 4;
}

public class CSiShellWrapper : CSiWrapperBase
{
  public override int ObjectType => 5;
}

public class CSiSolidWrapper : CSiWrapperBase
{
  public override int ObjectType => 6;
}

public class CSiLinkWrapper : CSiWrapperBase
{
  public override int ObjectType => 7;
}

public static class CSiWrapperFactory
{
  public static ICSiWrapper Create(int objectType, string name) =>
    objectType switch
    {
      1 => new CSiJointWrapper { Name = name },
      2 => new CSiFrameWrapper { Name = name },
      3 => new CSiCableWrapper { Name = name },
      4 => new CSiTendonWrapper { Name = name },
      5 => new CSiShellWrapper { Name = name },
      6 => new CSiSolidWrapper { Name = name },
      7 => new CSiLinkWrapper { Name = name },
      _ => throw new ArgumentOutOfRangeException(nameof(objectType), $"Unsupported object type: {objectType}")
    };
}
