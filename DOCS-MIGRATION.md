# Documentation migration notes (historical)

- Source: two user-provided DokuWiki ZIP exports, 14 pages and 20 images.
- Source semantics are not revised to match older PinkieSysMon snapshots.
- The dictionary is authoritative for property descriptions. Properties by tab uses `pymdownx.snippets` named sections.
- The DokuWiki `section` inclusion is replaced with a Markdown include, not pasted copies.
- Original image binaries are kept byte-for-byte, and all 20 files are included.
- DokuWiki image `lightbox` is approximated with an image linked to its original.
- DokuWiki layout-only `WRAP` markup is simplified to responsive Markdown, while info/important notes become admonitions.
- Material for MkDocs 9.7.7 is pinned because the theme is in maintenance mode.
- At migration time, PR documentation validation and GitHub Pages deployment used GitHub Actions. Current checks and deployment rules are owned by the live [`.github/workflows/docs.yml`](.github/workflows/docs.yml) and [`mkdocs.yml`](mkdocs.yml); repository Pages settings must be verified from GitHub, not inferred from this archival record.
- This conversion only changes documentation; Runtime, Editor, build.ps1, dashboard/schema are untouched.
