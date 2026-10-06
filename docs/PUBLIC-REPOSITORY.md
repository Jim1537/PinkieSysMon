# Public repository boundary

The public PinkieSysMon repository is intentionally narrower than a developer's local workstation installation.

Not tracked in the public repository:

- machine-specific `config` state;
- logs;
- the private production dashboard and its private/local assets;
- additional font/icon collections that are not part of the public redistribution baseline;
- Libre Hardware Monitor binaries;
- generated build/test/publish artifacts;
- code-signing private keys or certificate bundles.

The project application icon at `src/PinkieSysMon/PinkieSysMon.ico` is part of the public repository and is used by both Runtime and Editor builds.
