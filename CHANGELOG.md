# Changelog

## [v1.0.3] - 2026-06-04

### Added
- **Category index popup** — click the ☰ button in the top-right to view a quick-reference table mapping folder numbers 0–9 to their Dead by Daylight skill-check categories (repair-heal, full white, full black, wiggle, etc.)
- **Hover feedback on primary buttons** — Get Started, Next, Save, and Finish buttons in the setup wizard and settings dialog now dim slightly on mouse-over

### Fixed
- **UNSURE counter inflation** — pressing Unsure when the image was already in folder 0 (e.g. source = folder 0) incorrectly incremented the counter even though no file was moved
- **UNSURE counter not decreasing on undo** — undoing an Unsure sort now correctly decrements the counter, keeping it in sync with the other stats

---

## [v1.0.2] - 2026-06-04

Initial public release.

### Features
- Sort images into HIT, MISS, and Unsure folders with keyboard shortcuts (`H`/`→`, `M`/`←`, `U`)
- Recoverable delete — deleted files move to `.trash` and can be restored with Ctrl+Z until the session ends
- Full undo stack for all actions including deletions
- Live session stats — HIT, MISS, UNSURE, DEL counters and progress bar
- Drag & drop folder support
- Dark UI with configurable folder assignments (0–9) via setup wizard and settings dialog
- First-run setup wizard with 3-step folder configuration
