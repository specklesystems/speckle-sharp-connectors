// NOTE: This is an interim solution. API allows us to query the model, not get any instances from it
// Potentially move all of this to a new shared project at later stage

namespace Speckle.Converters.CSiShared;

public interface ICSiWrapper
{
  string Name { get; set; }
}

public class CSiPointWrapper : ICSiWrapper
{
  public string Name { get; set; }
}

public class CSiFrameWrapper : ICSiWrapper
{
  public string Name { get; set; }
}

public class CSiCableWrapper : ICSiWrapper
{
  public string Name { get; set; }
}

public class CSiTendonWrapper : ICSiWrapper
{
  public string Name { get; set; }
}

public class CSiAreaWrapper : ICSiWrapper
{
  public string Name { get; set; }
}

public class CSiSolidWrapper : ICSiWrapper
{
  public string Name { get; set; }
}

public class CSiLinkWrapper : ICSiWrapper
{
  public string Name { get; set; }
}
