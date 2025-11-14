# GMentor

PC assistant for gamers: select part of your screen, hit a hotkey, and let AI explain what to do — quests, guns, loot, keys.  
Runs locally, uses your own Gemini API key, and never proxies your data.

---

## ✨ What GMentor does

- 🕹 **Game-aware prompts**  
  Recognizes supported games (starting with **Arc Raiders**) and swaps to game-specific shortcuts and prompts.

- 🔫 **Gun builds & mods**  
  Look at your weapon screen, hit `Ctrl+Alt+G`, get a concise build suggestion with reasoning.

- 📦 **Loot / keys / cards**  
  Crop any item, keycard, or loot screen and get “where it spawns / how to use it”.

- 🔍 **Search-backed answers**  
  Uses Gemini’s web search to cross-check with **arcraiders.wiki** and other sources when needed.

- 🧠 **Local, key stays on your PC**  
  Your Gemini API key is encrypted in the Windows credential store. Requests go straight from your PC to Google.

---

## 🚀 Getting started

1. **Download GMentor**

   > 🔗 **Download (Windows)** – _to be filled with your release URL_  
   Example: link to the latest `.zip` or `.exe` in GitHub Releases.

2. **Get a free Gemini API key**

   - Go to Google AI Studio and create an API key for `gemini-2.5-flash` or `gemini-2.5-pro`.
   - Free tier is enough for most casual use.

3. **First launch**

   - Start `GMentor.exe`.
   - Go to **File → Change Provider/Key…**
   - Paste your Gemini API key and hit **Test**, then **Continue**.

4. **Use in-game**

   - Open your game and the screen you care about.
   - Press one of the global hotkeys and drag a rectangle over the area:
     - `Ctrl+Alt+Q` – Quest / Mission
     - `Ctrl+Alt+G` – Gun Mods
     - `Ctrl+Alt+L` – Loot / Item
     - `Ctrl+Alt+K` – Keys / Cards
   - GMentor sends **only that cropped image** to Gemini with a game-specific prompt.
   - The answer appears in the GMentor window (you can copy it or open a related YouTube search).

GMentor lives in the system tray when minimized; hotkeys stay active.

---

## 🖼️ Screenshots

### 🔍 In-game usage (Arc Raiders – Loot Scan)

GMentor detects the game, captures only the selected screen region, and generates a search-backed explanation.

![GMentor Screenshot](docs/images/gmentor-loot-example.png)

---

## 🔐 Privacy & security

- Your **Gemini API key never leaves your machine**. It’s stored via Windows DPAPI using a simple secure key store.
- GMentor **does not proxy** calls. The cropped image + text go **directly from your PC to Google**.
- No analytics, no telemetry. The only logs are local structured logs to help you debug issues if you want to.

---

## 🧠 Why Gemini?

- **Free tier** that’s actually usable for hobby play.
- Multimodal: can handle both screenshots and text instructions.
- Strong web search integration: good enough to pull wiki/guide context for new games like Arc Raiders.

You are free to fork and wire this to other providers if you want (OpenAI, Claude, etc.).

---

## 🏗️ Tech stack

- **Client:** WPF / .NET (C#)
- **AI:** Gemini 2.5 (flash / pro), via HTTP client
- **Packs:** signed JSON `.gpack` files with prompt manifests per game (e.g. `ArcRaiders.gpack`)

---

## 🤝 Contributing

Contributions are welcome. Some high-leverage areas:

- New game packs (prompts for other games, with manifests).
- Better prompt tuning for:
  - Gun builds (less hallucination, more wiki-grounded).
  - Quest routes and loot locations.
- UX improvements in the WPF app.

Typical flow:

1. Fork the repo.
2. Create a feature branch: `feature/add-xyz-game-pack`.
3. Open a PR with:
   - The new manifest in `manifests/`.
   - Any code changes in `GMentor.Core` or the WPF client.
   - A short description and screenshots or examples.

---

## 💸 Supporting development

GMentor is free, but it costs time to maintain and tune (especially for game-specific packs).

If it’s useful for you and you want to help it survive:

- ⭐ **Star this repo** on GitHub – it seriously helps visibility.
- 🧡 **Sponsor on GitHub** – via the _Sponsor_ button on this page.
- ☕ **Direct support (Stripe)** – https://donate.stripe.com/6oUcN6els87m7TS1ZagjC00.

All support is handled via my **company Stripe account**, not a personal wallet.

---

## 📜 License

_Choose one and put the actual license text in `LICENSE`_:

- **MIT** if you want max adoption, including commercial forks.
- **Apache-2.0** if you want patent protection language.
- **AGPL / GPL** if you want to force improvements to stay open.

For now, this repo uses: **MIT License** (recommended for a tool like this).

---

## 📬 Contact

- Website: https://gmentor.ai
- GitHub: [@MaDeRkAn](https://github.com/MaDeRkAn)

PRs, ideas, and bug reports are welcome.
