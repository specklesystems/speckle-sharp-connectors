# Rhino / Grasshopper on macOS — build it yourself

> **Alpha.** This is a working prototype, not a supported release: no official
> distribution, no signing/notarization, no test coverage beyond manual smoke checks,
> and no compatibility guarantee across Rhino point releases. Expect rough edges, and
> expect things to change under you without notice.

There is no official macOS distribution for the Rhino and Grasshopper connectors yet
(no Yak package, no signed installer). What's here works end to end — sign-in, publish,
load — and is genuinely usable today; it just isn't packaged for you, and it hasn't had
the hardening a real release gets. Build it from source and install it locally, in the
same spirit as any other open-source tool without a binary release yet.

This is **not** the same connector head as Windows. Rhino 8 on macOS only loads .NET
Core plug-ins, so `Speckle.Connectors.Rhino8.Mac` and `Speckle.Connectors.Grasshopper8.Mac`
are separate `net8.0` projects that share almost all of their code with the Windows heads
(`RhinoShared`, `GrasshopperShared`) — see `atlas/specs/2026-09-rhino-mac-connector/` in the
root Speckle checkout for the full design notes and decisions if you want the "why", not
just the "how".

## Prerequisites

- macOS, Apple Silicon or Intel.
- Rhino 8 for Mac, **8.20 or later**.
- [.NET SDK 10](https://dotnet.microsoft.com/download) (`dotnet --version`). Check this
  repo's `global.json` for the exact floor.
- This repo cloned locally.

## Build and install

Each head has its own `install-mac.sh` next to its `.csproj`. Both build in Release and
copy the output into the right Rhino/Grasshopper folder for you — no manual file copying.

```bash
# Rhino
./Connectors/Rhino/Speckle.Connectors.Rhino8.Mac/install-mac.sh

# Grasshopper
./Connectors/Rhino/Speckle.Connectors.Grasshopper8.Mac/install-mac.sh
```

Pass a config name (`Debug`/`Release`/`Local`) as the one argument if you don't want
Release; it defaults to Release.

### Rhino: one-time load step

Rhino 8 Mac has **no Install button for plug-ins** — dropping the `.rhp` in the Plug-ins
folder is not enough on its own the first time. After running `install-mac.sh`, open
Rhino's ScriptEditor and run (Python):

```python
import Rhino
print(Rhino.PlugIns.PlugIn.LoadPlugIn(r'/Users/<you>/Library/Application Support/McNeel/Rhinoceros/8.0/Plug-ins/Speckle.Connectors.Rhino8.Mac/Speckle.Connectors.Rhino8.Mac.rhp'))
```

(the script prints the exact path it installed to — copy it from there). It should print
`True`. Rhino remembers the plug-in after that; restarting Rhino is enough on subsequent
launches, no need to repeat the ScriptEditor step unless the plug-in id changes. Run the
`Speckle` command to open the panel.

### Grasshopper: just restart

No load step needed. Quit and restart Rhino, open Grasshopper, and the Speckle tab
appears on the ribbon with the same components as Windows.

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

- No signed/notarized distribution — that's a separate, ongoing effort (Apple Developer
  account, native library signing for the two bundled dylibs). This self-build path exists
  because that work doesn't need to block you from using the connector today.
- Rhino 7 for Mac is out of scope (Mono runtime; Rhino 8 Mac is .NET Core only).
- Alpha means alpha: this hasn't been through the review, testing, and hardening a
  Speckle release normally gets. Treat it as a prototype you're trying, not something to
  build a production workflow on yet.
