# RingDeck

Radial rings of macros, text snippets and shortcuts for Windows. Hold a shortcut, flick toward a slice, release.

<a href="https://apps.microsoft.com/detail/9N75XDTQ48GV?mode=direct"><img src="https://get.microsoft.com/images/en-us%20dark.svg" width="200" alt="Get it from Microsoft"></a>

[![Download](https://img.shields.io/github/v/release/rykou/ringdeck?label=Download&logo=windows)](https://github.com/rykou/ringdeck/releases/latest)
[![Buy me a coffee](https://img.shields.io/badge/Buy%20me%20a%20coffee-PayPal-003087?logo=paypal&logoColor=white)](https://paypal.me/rykou893)

[![RingDeck 0.3.4: rings that branch into a tree, slices that wait before they run, web requests and ring icons](docs/demo.gif)](docs/demo.mp4)

- Any number of rings, each on its own global shortcut (keyboard or mouse button).
- Slices paste text, play recorded macros (keys, clicks, mouse moves, scrolling), open apps, files and links, run flows of linked actions, or open another ring.
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

## Macros

A slice set to **Macro / workflow** plays a list of steps, top to bottom: key presses, key down and key up,
clicks, mouse moves, scrolling, typed or pasted text, opening apps and links, focusing a window, screenshots (to the
clipboard, or of an area you drag out when it runs), asking your AI, and web requests.
Each pause is a **Wait** row in milliseconds.

- **⏺ Record**: press F9, do the thing in any window, press F9 again. Mouse movement, clicks, scrolling and keys
  are recorded with the real time between them; while recording, only a small red circle shows in the bottom
  left corner (click it to stop). Esc cancels.
- **Mouse move** steps follow the recorded path; a speed slider plays it up to 3 times faster.
- **Show key down and key up separately** (off by default) splits each press into its down and up rows.
- **Pop out** opens the steps in their own window, where you can drag them into order, clear them all, or remove
  or set all the waits at once.

Esc stops a running macro, and any key or button it still holds is let go.

## Flows

A slice set to **Flow** opens a canvas where actions are cards linked by wires, from **Start**:

- Pick cards from the palette (Open, Recorded steps, Type text, Key press, Click, Screenshot, Screenshot area, Ask AI,
  Send to web and more);
  drag from a card's right dot onto another card to link them. Select a card to edit its steps on the right.
- A card with several links out starts those branches **at the same time**; a card with several links in waits for
  all of them. At least one branch must end at a **Stop**; others may simply end.
- Cards that use the keyboard, mouse or clipboard take turns, so branches never type over each other.
- For example: open the browser, take a screenshot, then at once play recorded steps in one branch and send the
  screenshot to Discord or Teams in another.

- **Ask AI** sends the clipboard picture and/or text to one of your AI connections with your question; the answer
  replaces what's on the clipboard, for the next cards to paste or send (`{clipboard}`). For example: Screenshot area →
  Ask AI "Read the error and suggest a fix" → Send to Teams.

A failing card stops the whole flow and the notification names it; Esc stops a running flow.

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

To ask about a picture, tick **Also send the picture on the clipboard** on the slice: take a screenshot (Win+Shift+S),
open the ring and pick the slice, and the AI reads it along with your text. The model must read images (GPT-4o,
Claude, Gemini and others).

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
- Mouse moves and clicks in macros go to fixed places on the screen: if a window moves, record those steps again.
  Some games ignore input sent this way.
- The MSI on GitHub is not code-signed, so SmartScreen warns on first run. The Microsoft Store version is
  signed by Microsoft and installs without a warning.

## Build (from WSL)

    ./build.sh test    # unit tests
    ./build.sh run     # build and start on Windows
    ./build.sh         # dist/RingDeck-<version>.msi

Needs a .NET 10 SDK and the WiX 3.14 binaries; see the header of `build.sh` for where they are looked up.

## Privacy

RingDeck does not send information to other computers, except when you choose a slice that does:

- **AI chat slices and Ask AI steps** send the text of that chat (and the clipboard picture, if you ticked that or the
  step reads it) to the AI service you set up for it, and nowhere else.
- **Web request slices** send the message you wrote (and the clipboard picture, if you ticked that) to the web service
  or address you set up for that slice, and nowhere else.

Nothing is sent in the background, and there is no telemetry. Your rings, snippets and
settings stay on your PC, in `%APPDATA%\RingDeck`.

## Support

RingDeck is free. If it saves you time, you can [buy me a coffee ☕](https://paypal.me/rykou893). Thank you!

## License

[MIT](LICENSE). Slice emoji come from Microsoft's [Fluent Emoji](https://github.com/microsoft/fluentui-emoji) and icons
from [Fluent UI System Icons](https://github.com/microsoft/fluentui-system-icons), both MIT; brand logos come from
[Simple Icons](https://github.com/simple-icons/simple-icons) (CC0) and are trademarks of their owners; see
[THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt). To rebuild those assets, run `tools/make_icon_assets.mjs`.

