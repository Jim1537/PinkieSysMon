# Documentation migration notes

- Source: two user-provided DokuWiki ZIP exports, 14 pages and 20 images.
- Source semantics are not revised to match older PinkieSysMon snapshots.
- The dictionary is authoritative for property descriptions. Properties by tab uses `pymdownx.snippets` named sections.
- The DokuWiki `section` inclusion is replaced with a Markdown include, not pasted copies.
- Original image binaries are kept byte-for-byte, and all 20 files are included.
- DokuWiki image `lightbox` is approximated with an image linked to its original.
- DokuWiki layout-only `WRAP` markup is simplified to responsive Markdown, while info/important notes become admonitions.
- Material for MkDocs 9.7.7 is pinned because the theme is in maintenance mode.
- The site must be built and checked by GitHub Actions on a PR before merging.
- GitHub Pages must be set to GitHub Actions as its deployment source in repository Settings.
- This conversion only changes documentation; Runtime, Editor, build.ps1, dashboard/schema are untouched.
