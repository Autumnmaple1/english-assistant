<p align="center"><img src="ScreenEnglish/Assets/ScreenEnglish.svg" width="88" alt="Screen English open-book icon"></p>

# Screen English

Read, translate, and understand English without leaving your Windows desktop.

Screen English is a small tray application built with **C# and WinUI 3**. It combines local screen recognition, an offline English dictionary, and an optional AI endpoint.

## What it does

- **Capture and translate:** select English on your screen and read a Chinese translation.
- **Capture and rewrite:** turn selected English into more natural English, with optional explanations.
- **Panel lookup:** hold a modifier over words in the learning panel, or select a phrase.
- **Screen lookup:** hold Ctrl+Shift+E, then hover over words on a frozen copy of the current monitor.
- **Offline definitions:** WordNet provides English meanings with tags such as `n.`, `v.`, `adj.`, and `adv.` on each definition line.
- **Focused output:** disable explanation fields you do not need; they are omitted from the AI response schema.

The learning panel uses a neutral light/dark design, compact controls, a draggable header, and automatic content sizing. It expands within the monitor's usable area; content longer than the screen remains scrollable.

## Requirements

This repository currently produces an **unpackaged x64 development build**.

- Windows 10 version 2004 or later, or Windows 11. The current verification machine uses Windows 11; appearance can differ on Windows 10.
- .NET 8 SDK to build; the build is framework-dependent and requires a compatible .NET runtime to run.
- Windows SDK / Windows build tools suitable for WinUI 3 development.
- Microsoft Visual C++ 2015–2022 x64 runtime for the native OCR library.
- Internet access for initial NuGet/data downloads and AI requests. Local OCR and WordNet work offline after setup.

The Windows App SDK runtime is included in the build output. Keep the complete output directory together when running or moving the app.

## Quick start

Open PowerShell in the `english-assistant` directory:

```powershell
.\setup.ps1
.\run.ps1
```

`setup.ps1` downloads English Tesseract data and WordNet, verifies the recorded data hashes, restores dependencies, and builds the application. Existing data files are reused. `run.ps1` launches the development executable; it runs setup if that executable is absent.

The app starts **in the system tray**. Look in the tray overflow area if the icon is hidden. Right-click the open-book icon for capture actions, Settings, startup preferences, and Exit. Double-click it to open Settings.

### Connect an AI service

Open **Settings → Connection**:

1. Enter the provider's base URL. Include `/v1` only when the provider requires it.
2. Enter the exact model name available to your account.
3. Enter the API key and choose **Save changes**.
4. Capture a short sentence with Ctrl+Shift+T.

The app appends `/chat/completions` to the base URL. Enter the service base URL in Settings.

The configuration used for live verification in this project is:

| Field | Value |
|---|---|
| Base URL | `https://api.deepseek.com` |
| Model | `deepseek-v4-flash` |
| Advanced parameters | `{"thinking":{"type":"disabled"}}` |

The example uses DeepSeek with thinking disabled. Choose advanced parameters supported by your configured provider.

The app includes its JSON response schema directly in the prompt. Responses are validated locally; malformed output receives one correction retry.

## Shortcuts

| Action | Default | Behavior |
|---|---|---|
| Capture and translate | Ctrl+Shift+T | Freeze the monitor under the pointer, then drag a region |
| Capture and rewrite | Ctrl+Shift+R | Select a new region and rewrite its English |
| Screen lookup | Hold Ctrl+Shift+E | Freeze the monitor and hover over recognized words |
| Panel lookup | Hold Ctrl | Hover over a word inside the learning panel |
| Phrase lookup | Hold Ctrl and select text | Look up selected panel text on mouse release |
| Exit / cancel | Esc | Close the panel or cancel the active overlay |

The two capture shortcuts, panel modifier, and panel hover delay are configurable. Ctrl+Shift+E is reserved for screen lookup. Another app using a shortcut can prevent registration; the app reports the conflict.

### Capture and learning panel

1. Place the pointer on the monitor you want to read.
2. Press a capture shortcut and drag around readable English.
3. Release the mouse. OCR runs locally; the selected text is sent to the configured AI service.
4. Read the result first, followed by any other enabled content.

The selection has one thin white edge and dimmed surroundings. Esc cancels selection. A canceled or failed capture retains the last successful capture.

- **Translate / Rewrite** reuse the current captured text using the stored capture.
- **Explain** looks up the captured text through the panel dictionary workflow.
- **Copy** copies the primary translation or improved English.
- **Pin** keeps the panel above other windows.
- **Drag the header**, outside its buttons, to move the panel.
- **Minimize** hides the panel; use the tray's Show learning panel action to restore it.
- **Close / Esc** dismisses the panel and cancels its pending lookup work.

The panel grows to fit visible content and can widen for long results. It stays inside the monitor work area and preserves scrolling when content cannot fit.

### Panel hover

Hold the configured modifier over an English word in the panel. WinUI maps the pointer to a character and identifies the containing word in the displayed text. After the configured wait (500 ms by default), the app checks its lookup cache and dictionary mode.

The hover timer continues through small movements inside the same word. Moving to another word invalidates the previous lookup. Releasing the modifier hides ordinary hover results. Requests that complete after dismissal are discarded.

Use panel hover for displayed text and Ctrl+Shift+E for words elsewhere on the screen.

### Hold-to-look-up screen mode

1. Press and keep holding **all three keys: Ctrl+Shift+E**.
2. The app shows a frozen screenshot of the monitor under the pointer and performs local OCR once.
3. Hover over a recognized word for about 200 ms. A subtle highlight marks the word, and an English definition appears nearby.
4. Release **Ctrl, Shift, or E**, or press Esc, to exit.

The underlying application continues running. The overlay intercepts mouse interaction while visible. Releasing the shortcut dismisses it even if OCR or an online lookup is unfinished; late results are discarded. OCR computation already running may finish in the background.

Screen mode always checks **local WordNet first**. If an entry is missing, it uses the configured AI endpoint and requests only `simple_english_meaning`. This local-first behavior is independent of the panel's dictionary selector and field toggles. Definitions are cached for the current hold session.

Local definitions list general senses, for example:

```text
n. a customary way of operation or behavior
v. carry out or practice
```

AI fallback is prompted to put an appropriate part-of-speech tag on every meaning line. WordNet presents general dictionary senses; AI fallback uses surrounding text as context.

## Settings

| Page | Controls |
|---|---|
| Connection | Base URL, model, saved key, timeout, additional JSON parameters |
| Shortcuts | Capture hotkeys, panel modifier, hover delay, start with Windows |
| Appearance | Light/dark theme, layout, text size, opacity, presets, toolbar buttons |
| Content | Translation, rewrite, and lookup fields; select all/none, reset, reorder |
| Dictionary | Automatic, Local WordNet, AI, or Cambridge browser link |

**Panel dictionary modes:**

- **Automatic:** use the local WordNet meaning whenever one exists, even if other enabled fields are unavailable locally. AI is used only when no local meaning is found. The local definition remains the displayed result.
- **Local WordNet:** offline English definitions, parts of speech, and available examples.
- **AI:** request the enabled contextual fields from your endpoint.
- **Cambridge:** show a link to open the dictionary website.

Disabled optional fields are removed from both the prompt schema and local validation contract. Primary source/result fields remain required for translate/rewrite. Source/target language codes are hidden in the panel. Turning off every panel lookup field skips lookup. Screen mode still requests its English definition.

After saving preferences, run the panel action again to generate the newly selected fields. Changes to field order are subject to the result-first panel layout.

## Privacy, storage, and logs

| Item | Location / behavior |
|---|---|
| Settings | `%LOCALAPPDATA%\ScreenEnglish\settings.json` |
| API keys | Windows Credential Manager, endpoint-specific `ScreenEnglish:` entries |
| Runtime log | `%LOCALAPPDATA%\ScreenEnglish\runtime.log` |
| Previous log | `runtime.log.previous`, rotated when the active log exceeds about 512 KB |
| OCR/dictionary data | `data/` in this repository, copied into the build output |
| Test artifacts | `test-output/` in the test working directory |

Production screenshots are processed in memory. AI requests contain extracted text and selected-word context. OCR and WordNet run locally; online lookup uses the configured endpoint credentials.

Runtime logs record events, character counts, HTTP status, validation state, and whether a key/model exists. Diagnostic self-tests save synthetic screenshots and results.

Leave the key field blank to keep the saved key for that endpoint. The removal checkbox deletes it. Each endpoint uses its own saved credential.

**Start with Windows** manages one per-user Run entry named `ScreenEnglish`, pointing to the current development build. If you move the project, turn startup off and back on from the new location.

## Build, test, and diagnose

Exit the tray app before rebuilding or testing; a running executable may be locked, and its global shortcuts can conflict with self-tests.

```powershell
$env:DOTNET_CLI_HOME = Join-Path $PWD '.dotnet'
$env:MSBuildEnableWorkloadResolver = 'false'
dotnet build .\ScreenEnglish\ScreenEnglish.csproj -p:Platform=x64
.\test.ps1
.\run.ps1
```

The integration suite covers real local OCR and WordNet, word coordinates, JSON contracts, disabled fields, correction retries, release during screen OCR, native shortcut registration, panel lifecycle, and rendered WinUI views. It uses synthetic text and an in-memory HTTP handler for request verification. It briefly opens test windows on the normal Windows desktop and writes results under `test-output/`.

The executable also accepts these diagnostic arguments:

| Argument | Purpose |
|---|---|
| `--self-test` | Run the integration suite; normally use `test.ps1` |
| `--check-settings` | Write `settings-check.json` with settings path, load warning, model, and a key-present boolean |
| `--check-provider` | Make three live synthetic AI requests and write `test-output/provider-check.json` |

Provider checks use your saved key and can consume API quota. Their result file includes synthetic model output. Diagnostic output paths are relative to the executable's working directory. Run a provider check to verify the current connection.

### Troubleshooting

| Symptom | What to check |
|---|---|
| Nothing appears after launch | The app starts in the tray; check the overflow area |
| Shortcut does nothing | Exit duplicate copies; check for another app using the shortcut |
| Screen lookup closes immediately | Keep Ctrl, Shift, and E all held down |
| No words detected | Use clear, sufficiently large horizontal English text; confirm OCR data exists |
| Missing model warning | Check Connection settings and the settings diagnostic |
| HTTP 401 / 403 | Check the key saved for this exact endpoint and model access |
| HTTP 400 / 422 | Check model spelling and provider-specific advanced parameters |
| HTTP 429 | Check provider rate limits or account quota |
| Lookup feels slow | Use Local WordNet for panel lookup, shorten the hover delay, or disable optional fields; AI still adds network time |
| Build cannot overwrite the EXE | Exit Screen English from the tray and rebuild |
| Startup stops working after a move | Toggle Start with Windows off and on at the new location |

## Icon and visual assets

The icon is a simple ivory open book on charcoal, with a restrained sage accent. It is used by the executable, native windows, tray, and panel header.

- `ScreenEnglish/Assets/ScreenEnglish.svg`: editable vector artwork.
- `ScreenEnglish/Assets/ScreenEnglish.png`: 256 px transparent PNG preview.
- `ScreenEnglish/Assets/ScreenEnglish.ico`: 16, 20, 24, 32, 40, 48, 64, 128, and 256 px Windows frames.
- `build-icon.ps1`: regenerates PNG/ICO from matching drawing geometry using Windows System.Drawing. At tiny sizes the accent lines are omitted for clarity. If editing the SVG, update the script geometry to match.

```powershell
.\build-icon.ps1
```

Normal builds use the generated assets included in the repository.

## Project map

```text
english-assistant/
├── ScreenEnglish/
│   ├── Assets/                 Icon source and Windows assets
│   ├── App.xaml.cs             Startup and diagnostic entry points
│   ├── Controller.cs           Tray, shortcuts, capture and panel workflows
│   ├── Native.cs               Windows interop, positioning and startup
│   ├── CaptureWindow.cs        Drag-to-select overlay
│   ├── ScreenLookupWindow.cs   Hold-to-look-up overlay and release lifecycle
│   ├── Views.cs                Learning panel and panel lookup popup
│   ├── SettingsWindow.cs       Settings interface
│   ├── SettingsStore.cs        Preferences and Windows credentials
│   ├── OcrService.cs           Local Tesseract recognition
│   ├── LocalDictionary.cs      Local WordNet definitions
│   ├── AiClient.cs             Prompt schemas, HTTP and response validation
│   ├── RuntimeLog.cs           Small privacy-conscious runtime log
│   └── SelfTests.cs            Integration checks and diagnostic helpers
├── data/                       OCR model and WordNet archive
├── build-icon.ps1
├── setup.ps1
├── run.ps1
├── test.ps1
└── THIRD_PARTY_NOTICES.md
```

The implementation uses straightforward WinUI code-behind within one C# application.

## Reading tips

- Capture clear, horizontal English at a readable size for best OCR results.
- Screen lookup uses the monitor under the pointer.
- WordNet supplies general dictionary entries; AI fallback handles missing entries using nearby text.
- Translation joins ordinary OCR line wraps while preserving blank paragraphs and list boundaries. The panel retains the captured original text.
- Recapture or re-enter screen mode after the underlying content changes.
- Long results remain scrollable after the panel reaches the usable screen size.
- Complete or exit the active capture or screen lookup before starting the other mode.

See [ACCEPTANCE.md](ACCEPTANCE.md) for the historical verification record and remaining manual checks. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for dependencies and dictionary/model attribution.
