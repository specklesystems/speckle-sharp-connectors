// MicroStation 2026 COM automation API (used for selection, idle, model events, raw element
// iteration, and the legacy COM-based top-level converters).
global using Application = Bentley.Interop.MicroStationDGN.Application;
global using Element = Bentley.Interop.MicroStationDGN.Element;
// Bentley.DgnPlatformNET managed API (mixed-mode C++/CLI). This is the proper typed surface
// equivalent to AutoCAD's acmgd.dll / Revit's RevitAPI.dll — when an element flows through
// the Send pipeline (filter → SendBinding → RootObjectBuilder → RootToSpeckleConverter), it's
// a MgdElement so the debugger shows real types instead of System.__ComObject. Bridges to
// the COM Element on demand via ModelReference.GetElementByID64(id) inside the dispatcher.
global using MgdElement = Bentley.DgnPlatformNET.Elements.Element;
global using ModelReference = Bentley.Interop.MicroStationDGN.ModelReference;
global using MSIDGN = Bentley.Interop.MicroStationDGN;
