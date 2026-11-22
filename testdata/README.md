# testdata

Test corpora shared by the test projects under `tests/`. Following AGENTS.md, test projects hold code only;
data files that must ship with the repository live here. Temporary output belongs in `.agents/temp` instead.

- Files under this folder are committed byte for byte (`.gitattributes` marks them `-text`), because
  round-trip tests compare exact bytes, including line endings.
- `tests/Quill.Editor.Tests` copies this folder into its output directory as `testdata/`.
- Every corpus taken from a third party sits in its own subfolder with a `README.md` that records the
  source URL, version or commit, SHA-256 of each file, and license / attribution.
