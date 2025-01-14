namespace Speckle.Converters.CSiShared.Utils;

public enum ModelObjectType
{
  NONE = 0,
  JOINT = 1,
  FRAME = 2,
  CABLE = 3,
  TENDON = 4,
  SHELL = 5,
  SOLID = 6,
  LINK = 7
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
  OTHER
}

[Flags]
public enum FramePropertyType
{
  NONE = 0,
  I = 1,
  CHANNEL = 2,
  T = 3,
  ANGLE = 4,
  DBL_ANGLE = 5,
  BOX = 6,
  PIPE = 7,
  RECTANGULAR = 8,
  CIRCLE = 9,
  GENERAL = 10,
  DB_CHANNEL = 11,
  AUTO = 12,
  SD = 13,
  VARIABLE = 14,
  JOIST = 15,
  BRIDGE = 16,
  COLD_C = 17,
  COLD_2_C = 18,
  COLD_Z = 19,
  COLD_L = 20,
  COLD_2_L = 21,
  COLD_HAT = 22,
  BUILTUP_I_COVERPLATE = 23,
  PCCGIRDERU = 25,
  BUILTUP_I_HYBRID = 26,
  BUILTUP_U_HYBRID = 27,
  CONCRETE_L = 28,
  FILLED_TUBE = 29,
  FILLED_PIPE = 30,
  ENCASED_RECTANGLE = 31,
  ENCASED_CIRCLE = 32,
  BUCKLING_RESTRAINED_BRACE = 33,
  CORE_BRACE_BRB = 34,
  CONCRETE_TEE = 35,
  CONCRETE_BOX = 36,
  CONCRETE_PIPE = 37,
  CONCRETE_CROSS = 38,
  STEEL_PLATE = 39,
  STEEL_ROD = 40,
  PCC_GIRDER_SUPER_T = 41,
  COLD_BOX = 42,
  COLD_I = 43,
  COLD_PIPE = 44,
  COLD_T = 45,
  TRAPEZOIDAL = 46,
  PCC_GIRDER_BOX = 47
}
