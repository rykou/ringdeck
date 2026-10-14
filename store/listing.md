# Microsoft Store listing for RingDeck

Copy each block into the matching field in Partner Center (Submission > Store listings > English).

## Product name
RingDeck

## Short description
Hold a shortcut, flick toward a slice, release: paste text, run a macro, open an app or ask an AI from a ring around your mouse.

## Description
RingDeck puts the things you do over and over one gesture away.

Hold a shortcut such as Alt+Q and a ring of slices opens around your mouse pointer. Move toward a slice and let go. The direction picks the slice, so after a day it becomes muscle memory and you stop reading labels.

Each slice can:
• paste a text snippet (signatures, canned replies, today's date)
• play a keystroke macro, which you can type in or record
• open an app, file, folder or web page
• open another ring, for menus inside menus
• send your selected text to an AI model and paste the answer back

Make as many rings as you like, each on its own keyboard shortcut or mouse side button. The editor shows a live preview while you build them: pick from about 1,600 colour emoji and 2,500 icons, use your own pictures with the built-in crop editor, and choose colours from a colour wheel.

AI slices work with OpenAI-compatible services (including local models through Ollama or LM Studio), Anthropic Claude, Google Gemini, or any HTTP API you describe. Each slice can use its own model and instructions, for example "Rewrite this so it sounds friendlier". You bring your own API key; RingDeck encrypts it for your Windows account.

Game mode switches the rings off automatically while a fullscreen game has focus, and you can list programs where they always stay off, so your shortcuts go straight to the game.

RingDeck is free and open source (MIT): https://github.com/rykou/ringdeck

## What's new in this version
First release in the Microsoft Store.

## Product features (one per line)
Radial menus that open around your mouse on any keyboard shortcut or mouse side button
Paste text snippets with placeholders such as today's date or the clipboard
Keystroke macros you can type in or record
Open apps, files, folders and links from a ring
Rings inside rings for deeper menus
AI slices: rewrite, translate or ask about selected text and paste the answer back
Works with OpenAI-compatible services, Claude, Gemini, Ollama, LM Studio or your own API
About 1,600 colour emoji and 2,500 icons, plus your own pictures with a crop editor
Colour wheel for slices and rings
Game mode turns rings off in fullscreen games and chosen programs
Drag slices between rings, duplicate slices and clone whole rings
Import and export rings to share them

## Search terms (up to 7)
radial menu
pie menu
launcher
text snippets
macros
shortcuts
AI rewrite

## Copyright and trademark info
© 2026 Murylo Batista

## Privacy policy URL
https://github.com/rykou/ringdeck#privacy

## Website
https://github.com/rykou/ringdeck

## Support contact info
https://github.com/rykou/ringdeck/issues

## Category
Productivity (subcategory, if asked: Personal productivity or Utilities & tools)

## Screenshots (1920x1080, in this order)
1-flick.png, 2-ai.png, 3-launch.png, 4-editor.png, 5-icons.png

## Restricted capability: runFullTrust (justification)
RingDeck is a desktop (WPF) app that shows a radial menu when the user holds a global shortcut in any application. It needs full trust to register the global keyboard and mouse shortcut (low-level input hook), to show its menu above other windows, and to perform the user's chosen actions in the app they are using: pasting text, sending keystroke macros and launching programs. Nothing runs until the user presses a shortcut they configured.

## Notes for certification
RingDeck runs in the system tray. On first start it opens its editor with a sample ring.
To test: close the editor, open Notepad, hold Ctrl+Alt+Space, move the mouse toward "Today's date" and release the keys; the date is pasted into Notepad. The editor reopens from the tray icon (double-click).
AI slices need an API key the user provides and are not part of the sample ring; no AI service is contacted unless the user sets one up.
"Start with Windows" uses the package's startup task (off by default, switchable in the tray menu).

## Age rating questionnaire (guidance)
- No violence, sexual content, gambling, purchases or ads.
- Users cannot communicate with each other and nothing is shared publicly.
- The app can connect to an AI service the user chooses with their own key, and shows that service's replies. If the questionnaire asks about unrestricted web content or AI-generated content, answer yes for that reason.
