# RingDeck

Radial rings of macros, text snippets and shortcuts for Windows. Hold a shortcut, flick toward a slice, release.

- Any number of rings, each on its own global shortcut (keyboard or mouse button).
- Slices paste text, play keystroke macros, open apps, files and links, or open another ring.
- Editor with live preview; settings in `%APPDATA%\RingDeck\config.json`.

## Use

| Do this | Result |
|---|---|
| Hold a ring's shortcut, move toward a slice, release | Runs the slice |
| Release in the centre, or press Esc | Closes the ring |
| Push the pointer well past a slice that opens another ring | Enters that ring |
| Right click or Backspace | Back one ring |
| Esc while a macro is playing | Stops the macro |

Placeholders in pasted and typed text: `{date}`, `{time}`, `{datetime}`, `{clipboard}`, with optional formats such as
`{date:yyyy-MM-dd}`. Type `{{` for a literal brace.

## Limits

- Windows blocks input to programs running as administrator; RingDeck shows a notification instead.
- Emoji used as icons are drawn in one colour.
- Exported rings do not include image icons.
- The installer is not code-signed, so SmartScreen warns on first run.

## Build (from WSL)

    ./build.sh test    # unit tests
    ./build.sh run     # build and start on Windows
    ./build.sh         # dist/RingDeck-<version>.msi

Needs a .NET 10 SDK and the WiX 3.14 binaries; see the header of `build.sh` for where they are looked up.
