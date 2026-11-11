# Changelog

## v-0.1

### Added
- "+" menu: new **Make**, **Spec**, **Buzz** and **Site** files, next to Design, FigJam and Slides.
  The Spec URL (`/spec/new`) is unverified.
- `spec` URLs are treated as file tabs.

### Fixed
- Opening a file could open several tabs. A file is now identified by its file key, so a repeat open
  focuses the existing tab. Repeated "new" URLs within 1.5 seconds also reuse the tab.
- Home and Community go back to where you left them after you open a file. A bare `/files` or
  `/community` link (such as "Back to files") no longer resets them. If the step-back after opening a
  file lands on the root, the tab returns to the remembered page. A new session still starts at the root.
- Local fonts in editors other than Design (FigJam, Slides, Buzz and others). Permission requests from
  `figma.com` subdomains are now allowed, as are permission kinds this WebView2 SDK doesn't name, which
  may include the local-network prompt for the Figma Agent. Camera, microphone and location are not
  affected. This fix is untested.

### Changed
- README updated for the new "+" menu items.
