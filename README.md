# RingDeck

Radial rings of macros, text snippets and shortcuts for Windows. Hold a shortcut, flick toward a slice, release.

[![Download](https://img.shields.io/github/v/release/rykou/ringdeck?label=Download&logo=windows)](https://github.com/rykou/ringdeck/releases/latest)
[![Buy me a coffee](https://img.shields.io/badge/Buy%20me%20a%20coffee-PayPal-003087?logo=paypal&logoColor=white)](https://paypal.me/rykou893)

[![RingDeck: hold a shortcut, flick toward a slice, release](docs/demo.gif)](docs/demo.mp4)

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

## AI chat slices

A slice can open a chat with any AI model you set up: OpenAI-compatible services (OpenAI, Ollama, LM Studio,
OpenRouter, Groq and others), Anthropic, Google Gemini, or any HTTP API through a custom request template.

1. In the editor, open **Settings → AI connections → New**: pick the format, then fill in the address, model and key.
   Once a key is in, the Model list fills with the models it can use (OpenAI-compatible, Anthropic and Gemini).
   Add extra headers if your service needs them. **Test** checks it works.
2. Set a slice's **When chosen** to **AI chat**, pick the connection, and optionally give it an instruction such as
   "Translate to English".
3. Select some text anywhere, open the ring and pick the slice. The chat opens with your text, the reply streams in,
   and **Ctrl+Enter** pastes it back over your selection.

Make as many slices as you like, each on its own connection and instruction. Keys are encrypted for your Windows
account and are never included when you export rings.

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

## Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by
[SignPath Foundation](https://signpath.org). Signing is being set up: releases up to 0.2.4 are not signed yet.

- Committers and reviewers: [rykou](https://github.com/rykou)
- Approvers: [rykou](https://github.com/rykou)

Only installers built by this repository's [GitHub Actions workflow](.github/workflows/build.yml) from a tagged commit
are signed. Nothing built on a personal computer is.

## Privacy

RingDeck does not send information to other computers, with one exception you control: when you use an AI chat slice,
the text of that chat goes to the AI service you set up for that slice, and nowhere else. Your rings, snippets and
settings stay on your PC, in `%APPDATA%\RingDeck`.

## Support

RingDeck is free. If it saves you time, you can [buy me a coffee ☕](https://paypal.me/rykou893). Thank you!

## License

[MIT](LICENSE)

