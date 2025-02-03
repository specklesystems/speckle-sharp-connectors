// ReSharper disable InconsistentNaming
namespace Speckle.Connectors.DUI.Eventing;

[Flags]
public enum EventFeatures
{
  None = 0,
  OneTime = 1,
  IsAsync = 2,
  ForceStrongReference = 4
}
