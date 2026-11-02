# libCCI reference binaries (test oracle)

Unmodified binaries built from Team-Resurgent/libCCI at commit `1b0087e`:

| File | Purpose |
|---|---|
| `cci_encode.exe` | reference encoder (LZ4-HC level 12) |
| `cci_extract.exe` | reference decoder |
| `cci_info.exe` | header / size dump |

They are **test-only**: `CciReferenceInteropTests` checks both directions
(our reader on their output, their decoder on our output) on Windows, so CI
enforces interoperability instead of silently skipping when the tools are
absent. The library never links or redistributes them, and they are not part
of the NuGet package.

License: GPL-2.0-or-later (see the upstream `LICENSE`). Source for this exact
build: <https://github.com/Team-Resurgent/libCCI/tree/1b0087e>.
