namespace Speckle.Converters.CSiShared.Utils;

// NOTE: Should number of enums become too large -> dedicated files.
public enum ModelObjectType
{
  NONE = 0,
  JOINT = 1,
  FRAME = 2,
  CABLE = 3,
  TENDON = 4,
  SHELL = 5,
  SOLID = 6,
  LINK = 7,
}

public enum ElementCategory
{
  COLUMN,
  BEAM,
  BRACE,
  WALL,
  FLOOR,
  RAMP,
  JOINT,
  OTHER,
}

public enum DirectionalSymmetryType
{
  ISOTROPIC,
  ORTHOTROPIC,
  ANISOTROPIC,
  UNIAXIAL,
}

public enum AreaPropertyType
{
  NONE = 0,
  SHELL = 1,
  PLANE = 2,
  ASOLID = 3,
}
