# Rhino / Grasshopper on macOS

> **Alpha.** A working prototype, not a supported release: no test coverage beyond manual
> smoke checks, and no compatibility guarantee across Rhino point releases.

Rhino 8 on macOS only loads .NET Core plug-ins, so `Speckle.Connectors.Rhino8.Mac` and
`Speckle.Connectors.Grasshopper8.Mac` are separate `net8.0` projects that share almost all of
their code with the Windows heads (`RhinoShared`, `GrasshopperShared`). Design notes and
decisions: `atlas/specs/2026-09-rhino-mac-connector/` in the root Speckle checkout.

Requires Rhino 8 for Mac **8.20 or later**, Apple Silicon or Intel.

## Install

Builds are packaged by specklesystems/connector-installers (`mac/connectors/rhino.conf`): one
`.pkg` installs both heads. Rhino scans `MacPlugIns` at launch, so there is no load step —
restart Rhino and run the `Speckle` command; the Speckle tab appears in Grasshopper.

| Head | Installed to (under `~/Library/Application Support/McNeel/Rhinoceros/8.0/`) |
|---|---|
| Rhino | `MacPlugIns/Speckle.Connectors.Rhino8.Mac/` |
| Grasshopper | `Plug-ins/Grasshopper (b45a29b1-4343-4035-989e-044e8580d9cf)/Libraries/Speckle.Connectors.Grasshopper8.Mac/` |

To test a local build, `dotnet build -c Release` the head and copy its `bin/Release/net8.0/`
output over the installed folder above. Don't install anywhere else: a second copy in another
folder can be the one Rhino loads.

## Which DUI it talks to

The panel loads whatever `GlobalConfigResolver.GetDuiUrl()` resolves to — by default
`https://dui.speckle.systems`, the same production DUI every other connector uses. You
don't need to run anything locally for normal use.

If you're developing against a local DUI checkout instead, point the connector at it with:

```bash
launchctl setenv SPECKLE_DUI_URL http://localhost:3000   # GUI apps read the launchctl env, not your shell's
```

then quit and restart Rhino (`launchctl setenv` only affects processes started after you
set it). Unset with `launchctl unsetenv SPECKLE_DUI_URL` to go back to production.

## If the Rhino panel is blank

The panel bridges to the DUI over WKWebView script messages (not the `WebView2` host-object
mechanism Windows uses), since WKWebView on macOS blocks the `wss://`/`ws://` loopback the
older transport needs. If you see a docked panel with nothing rendered in it:

1. Confirm Rhino now shows up in Safari's **Develop** menu (Safari → Settings → Advanced →
   "Show features for web developers" first, if you don't have a Develop menu at all).
   `DUI3EtoWebView` marks the WebView inspectable on launch; if Rhino isn't listed, the
   panel never finished constructing.
2. If it is listed, open the inspector on it and check the console — look for lines like
   `✔ <binding> connector binding added succesfully` or `Failed to bind <binding> binding`.
3. Check Rhino's command line / history for `Speckle: DUI loaded ...` and
   `Speckle: first DUI message received — Eto bridge is live`. If neither appears, the
   WebView never navigated or the bridge never received a message — check
   `SPECKLE_DUI_URL` is actually reachable.

## What isn't finished

- Rhino 7 for Mac is out of scope (Mono runtime; Rhino 8 Mac is .NET Core only).
- Alpha means alpha: this hasn't been through the review, testing, and hardening a
  Speckle release normally gets. Treat it as a prototype you're trying, not something to
  build a production workflow on yet.
