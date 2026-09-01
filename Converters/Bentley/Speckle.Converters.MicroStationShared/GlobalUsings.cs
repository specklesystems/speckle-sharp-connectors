// MicroStation 2026 COM automation API. The element-converter pipeline is fully managed —
// these aliases are kept for the few places that legitimately need COM (settings factory,
// DgnPoint extension utilities, etc.).
global using Application = Bentley.Interop.MicroStationDGN.Application;
global using MeasurementUnit = Bentley.Interop.MicroStationDGN.MeasurementUnit;
// Bentley.DgnPlatformNET managed API — typed surface flowing through the entire Send pipeline.
global using MgdElement = Bentley.DgnPlatformNET.Elements.Element;
global using MgdMeshHeader = Bentley.DgnPlatformNET.Elements.MeshHeaderElement;
global using MSIDGN = Bentley.Interop.MicroStationDGN;
// Speckle SDK
global using SSC = Speckle.Sdk.Common;
