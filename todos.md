# TODOs

## Packfile send route for importers

Enable the importer flow to use the packfile send path (`SendViaPackfile` via `SendPipeline` + `IRootContinuousTraversalBuilder`). In this route, the server creates the version at the end of its processing job, so the job processor must skip calling `Ingestion.Complete()`.

The `null` root object ID signals "server handles completion":

1. **`Importers/Rhino/Speckle.Importers.Rhino/Internal/Sender.cs`**: When packfile route is used, return `null` for `RootId` (e.g. `SerializeProcessResults` won't have one — write `RootObjectId = null` in `Program.cs:61`).
2. **`Importers/Rhino/Speckle.Importers.JobProcessor/JobHandlers/RhinoJobHandler.cs` (line 69-73)**: Stop treating `null` `RootObjectId` as an error. Null means "server handles it", not failure.
3. **`Importers/Rhino/Speckle.Importers.JobProcessor/JobProcessor.cs` (line 189-192)**: Make `rootObjectId` nullable, skip `ReportSuccess()` when null.
