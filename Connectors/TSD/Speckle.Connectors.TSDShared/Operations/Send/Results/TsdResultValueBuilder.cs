using TSD.API.Remoting.Common;
using TSD.API.Remoting.Loading;

namespace Speckle.Connectors.TSDShared.Operations.Send.Results;

internal static class TsdResultValueBuilder
{
  public static Dictionary<string, object?> Force(IForce3d force) =>
    new()
    {
      ["Fx"] = new TsdQuantityValue("Fx", force.Fx, Quantity.Force, "N"),
      ["Fy"] = new TsdQuantityValue("Fy", force.Fy, Quantity.Force, "N"),
      ["Fz"] = new TsdQuantityValue("Fz", force.Fz, Quantity.Force, "N"),
      ["Mx"] = new TsdQuantityValue("Mx", force.Mx, Quantity.Moment, "Nmm"),
      ["My"] = new TsdQuantityValue("My", force.My, Quantity.Moment, "Nmm"),
      ["Mz"] = new TsdQuantityValue("Mz", force.Mz, Quantity.Moment, "Nmm"),
    };

  public static Dictionary<string, object?> Displacement(IDisplacement displacement) =>
    new()
    {
      ["Ux"] = new TsdQuantityValue("Ux", displacement.Mx, Quantity.Deflection, "mm"),
      ["Uy"] = new TsdQuantityValue("Uy", displacement.My, Quantity.Deflection, "mm"),
      ["Uz"] = new TsdQuantityValue("Uz", displacement.Mz, Quantity.Deflection, "mm"),
      ["Rx"] = new TsdQuantityValue("Rx", displacement.Rx, Quantity.Angle, "rad"),
      ["Ry"] = new TsdQuantityValue("Ry", displacement.Ry, Quantity.Angle, "rad"),
      ["Rz"] = new TsdQuantityValue("Rz", displacement.Rz, Quantity.Angle, "rad"),
    };
}
