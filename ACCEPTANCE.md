# Verification record

Verified on this Windows machine on 2026-09-13. Development implementation: **C# + WinUI 3 + local Tesseract**, superseding the handoff's proposed Python/PaddleOCR stack.

## Automated results

The development build passes with **zero warnings and zero errors**. The updated suite contains **15 integration checks**, covering the visual redesign, prompt-only JSON contracts, and capture selection. The tray checks run on the normal Windows desktop, because the isolated test desktop does not provide Explorer's tray.

`test-output/results.json` contains the full result list. `test-output/ocr-result.json` contains the actual OCR result and six separate word boxes for a synthetic English sentence. Eight `panel-*.png` files show the real WinUI learning panel in every theme/layout combination; `settings-{theme}-{page}.png`, `hover.png`, and `capture-selection.png` cover settings navigation, the lookup panel, and the high-contrast capture overlay.

Verified:

- Tray-only startup and registration of both default global shortcuts.
- Actual local OCR text recognition and word bounding boxes, mapped back to source pixels after local upscaling.
- Actual local WordNet definition lookup.
- Negative screen-coordinate word hit testing and reverse-direction phrase ordering.
- Word hit testing against the real rendered WinUI text positions.
- Light/dark and compact/vertical/side-by-side/tabbed panel rendering.
- Panel minimize/close disables its hover eligibility.
- Strict output contract validation, including wrong action/type/unknown fields.
- Invalid AI JSON receives one correction retry and then a friendly failure.
- Authentication failure does not cause retries.
- JSON schemas are embedded in the prompt for every provider. No API-level `response_format` is sent; old mode preferences cannot re-enable it.
- Legacy appearance migrates to an opaque window without changing the user's DeepSeek endpoint or model.
- Capture selection preserves the bright original image, displays a high-contrast border and corners, and maps its dimensions back to physical pixels.
- Escape close shortcuts are registered on the learning panel and lookup popup; the controller also handles Escape while the original application is focused.
- Text-only AI request construction and preservation of the original source text.
- Duplicate-hotkey and reserved-request-parameter rejection; no API key in settings serialization.

## Manual checks still needed

These require interactive use or your own AI endpoint and are not claimed as completed by the automated suite:

1. Press each default shortcut while using another application. Confirm the correct monitor freezes, drag selects a region, and Esc cancels.
2. Repeat on a second monitor with a different DPI, including a monitor to the left of the primary display. Coordinate math is tested; a physical mixed-DPI configuration has not been exercised.
3. Try a real captured paragraph with the configured DeepSeek model. Live translation, rewrite, and context-lookup requests using synthetic text have already passed; the unit/integration suite still uses an in-memory HTTP handler.
4. Hold the modifier over captured words, drag across a phrase in each direction, and repeat inside the learning panel. Confirm popups disappear on modifier release, close, minimize, and successful recapture.
5. Start a second capture during a slow AI request. Confirm the older response does not overwrite the new result. Cancel or fail the new capture and confirm the last successful region remains active.
6. Toggle startup from the tray and Settings, then verify it after a Windows sign-in. The suite deliberately does not change your startup entries.
7. Save/reopen preferences and an API key through the real Windows Credential Manager. The suite deliberately does not alter your saved credentials or settings.
8. Confirm pin/unpin, opacity, clipboard actions, field reordering, and Cambridge links during normal desktop use.

## Limits

- No installer or packaged distribution is produced; this is a development build.
- OCR targets clear, horizontal English. Very small, stylized, rotated, or low-contrast text may require a clearer capture.
- Original-region hover uses a frozen snapshot and is invalid after the underlying content moves. Recapture after scrolling.
- Local WordNet provides general English meanings; Chinese and contextual grammar/explanations require AI.
- AI semantic correctness is not guaranteed by valid JSON. The preservation instructions are always included, and the displayed source is always the actual OCR text.

## Live DeepSeek verification

The configured `https://api.deepseek.com` endpoint and exact model `deepseek-v4-flash` passed three live requests: translation, rewrite, and `hover_lookup`. Each returned a locally validated JSON object using the prompt-only schema. The API key was read directly by the app from its existing Windows Credential Manager entry and was not displayed, logged, changed, or exported.

The synthetic input was “Learning English takes practice every day.” Results are in `test-output/provider-check.json`. Observed request times were approximately 1.7, 2.1, and 4.0 seconds respectively. These are individual test observations, not latency guarantees.
