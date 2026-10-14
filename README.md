# RingDeck

Radial rings of macros, text snippets and shortcuts for Windows. Hold a shortcut, flick toward a slice, release.

[![Download](https://img.shields.io/github/v/release/rykou/ringdeck?label=Download&logo=windows)](https://github.com/rykou/ringdeck/releases/latest)
[![Buy me a coffee](https://img.shields.io/badge/Buy%20me%20a%20coffee-PayPal-003087?logo=paypal&logoColor=white)](https://paypal.me/rykou893)

[![RingDeck 0.3.4: rings that branch into a tree, slices that wait before they run, web requests and ring icons](docs/demo.gif)](docs/demo.mp4)

- Any number of rings, each on its own global shortcut (keyboard or mouse button).
- Slices paste text, play keystroke macros, open apps, files and links, or open another ring.
- Editor with live preview; settings in `%APPDATA%\RingDeck\config.json`.

## Use

| Do this | Result |
|---|---|
| Hold a ring's shortcut, move toward a slice, release | Runs the slice |
| Release in the centre, or press Esc | Closes the ring |
| Point at a slice that opens another ring (or push past it) | That ring grows out of the slice, linked to this one, which stays on screen |
| Move back onto an earlier ring | The rings after it fold away |
| Point back toward the previous ring and click, or right click, or Backspace | Back one ring |
| Esc while a macro is playing | Stops the macro |
| A slice with a wait (a circle around it) | Keep pointing until the circle fills, then release or click; let go early and nothing runs |

Rings can have an icon of their own: it shows in the ring list, in the middle of the ring, and on slices that open
that ring. Rings come in five styles (Settings → Ring style, or per ring): Classic, plus Soft, Clean, Neon and Labels.

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

## Web request slices

A slice (or a workflow step) can send a message to a web service: **Discord, Slack** (and Mattermost or
Rocket.Chat), **Microsoft Teams, Google Chat, Telegram, ntfy, Pushover, Home Assistant**, an automation in **n8n,
Make, Zapier or IFTTT**, or any web address with your own method, headers and body.

1. Set a slice's **When chosen** to **Send to a web service** and pick the service. The editor says where to find the
   webhook URL or token for it.
2. Write the message. `{selection}` is the text selected where you used the ring; `{clipboard}`, `{date}` and
   `{time}` work too.
3. For Discord, Telegram, ntfy, Pushover, Teams and the automations, tick **Also send the picture on the clipboard**: take a
   screenshot with Win+Shift+S, open the ring and flick, and the picture is posted with your message.
   Teams only takes small messages, so there the picture is made smaller to fit.

New web request slices wait 1 second before they can run (a circle fills while you point at them), so nothing is sent by a slip of the mouse; any slice can have a wait, under **Wait before it can run**. **Send a test** checks it from the editor. Webhook addresses and tokens are encrypted for your Windows account and are
never included when you export rings, since anyone with a webhook URL can post to that channel.

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

[MIT](LICENSE). Slice emoji come from Microsoft's [Fluent Emoji](https://github.com/microsoft/fluentui-emoji) and icons
from [Fluent UI System Icons](https://github.com/microsoft/fluentui-system-icons), both MIT; brand logos come from
[Simple Icons](https://github.com/simple-icons/simple-icons) (CC0) and are trademarks of their owners; see
[THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt). To rebuild those assets, run `tools/make_icon_assets.mjs`.

