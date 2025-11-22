# GMentor

AI assistant for gamers — select part of your screen, press a hotkey, and get instant, game-specific guidance.  
Works with **any game**, with enhanced support for **Arc Raiders** and **Escape From Tarkov**.  
Runs locally and uses your own AI key; your data never leaves your PC except for the cropped image sent to your provider.

---

## ✨ What GMentor does

- 🎮 **Universal game analysis**  
  Works on *any* PC game. GMentor reads whatever is on your screen and generates fast, accurate guidance.

- 🧩 **Enhanced support packs**  
  Extra-deep prompts for **Arc Raiders**, **Escape From Tarkov**, **Rust**, **DayZ** (quests, guns, items, extracts, ammo, armor, etc.).

- 🔫 **Weapons & builds**  
  Highlight your gun → get a clean build or mod recommendation tailored to the game.

- 📦 **Loot & items**  
  Instantly get “what it is, where it’s used, should you keep it”.

- 🔍 **Optional search-backed answers**  
  GMentor can check game wikis or official sources automatically when needed.

- 🔐 **Local & safe**  
  Your API key is encrypted in Windows. GMentor never proxies requests.

---

## 🚀 Getting Started

1. **Download (Windows)**  
   👉 **Latest:** https://github.com/MaDeRkAn/GMentor/releases/latest/download/GMentor.zip
   > You may see a SmartScreen warning because GMentor is a new app.  
   > Click **“More info” → “Run anyway”**.

2. **Get an AI API key**

   - Create a key for a Gemini-compatible model (e.g. `gemini-2.5-flash`).
   - Free tier is usually enough for casual use.

3. **First launch**

   - Start `GMentor.exe`.
   - Go to **File → Change Provider/Key…**
   - Paste your API key, hit **Test**, then **Continue**.

4. **Use in-game**

   - Open your game and the screen you care about.
   - Press a global hotkey and drag a rectangle over the area:
     - `Ctrl+Alt+Q` – Quest / Mission
     - `Ctrl+Alt+G` – Gun Mods / Builds
     - `Ctrl+Alt+L` – Loot / Item
     - `Ctrl+Alt+K` – Keys / Cards
   - GMentor sends **only that cropped image** to the AI with a game-aware prompt.
   - The answer appears in the GMentor window; you can copy it or open a related YouTube search.

GMentor lives in the system tray when minimized; hotkeys stay active.

---

## 🖼️ Example

GMentor detects what you captured, identifies the game context (if a manifest exists), and produces a fast, minimal, actionable answer.

![GMentor Screenshot](docs/images/gmentor-loot-example.png)

---

## 🔐 Privacy

- Your API key is stored via **Windows DPAPI** (encrypted on your machine).
- GMentor **does not proxy** requests; calls go directly from your PC to the AI provider.
- No telemetry, no external analytics.  
  Optional local logs exist only to help you debug if you choose.

---

## 🧠 Why GMentor?

- Works with **any game** out of the box.
- Becomes smarter with **per-game JSON manifests** (Arc Raiders, EFT, more to come).
- Lets you stay in the game instead of alt-tabbing through wikis and YouTube.
- Fully open source; you can inspect and extend everything.

---

## 🏗️ Tech Stack

- **Client:** WPF / .NET (C#)  
- **AI:** Gemini-compatible HTTP client  
- **Game support:** JSON manifests under `manifests/` describing shortcuts + prompt templates per game

---

## 🤝 Contributing

Contributions are welcome.

High-impact ways to help:

- Add **new game manifests** under `manifests/` (JSON format, using the existing files as reference).
- Improve prompt templates for:
  - Gun builds (less hallucination, more grounded in trusted sources)
  - Quests, loot, keys, extracts, and map guidance
- UX and usability improvements in the WPF app.

Typical flow:

1. Fork the repo.  
2. Create a feature branch, for example:  
   `feature/add-<game>-manifest`  
3. Add or update:
   - A JSON manifest in `manifests/` following the supported format  
   - Any required changes in `GMentor.Core` or the WPF client  
4. Open a PR with a short description and, if possible, screenshots or example outputs.

---

## 💸 Supporting Development

GMentor is free, but tuning prompts, adding new games, and eventually training a **game-focused AI model** takes real time.

If you want to help it grow:

- ⭐ **Star this repo** – improves visibility.  
- 🧡 **Sponsor on GitHub** – via the **Sponsor** button.  
- ☕ **Direct support (Stripe)** – https://donate.stripe.com/6oUcN6els87m7TS1ZagjC00  

All support is processed via my company Stripe account.

---

## 📜 License

This project uses the **MIT License**.  
See [`LICENSE`](LICENSE) for details.

---

## 📬 Contact

- Website: https://gmentor.ai  
- GitHub: [@MaDeRkAn](https://github.com/MaDeRkAn)

PRs, ideas, and bug reports are always welcome.
