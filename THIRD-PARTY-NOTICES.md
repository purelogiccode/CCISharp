# Third-Party Notices

CCISharp is an independent, clean-room MIT implementation of the CCI
("CCIM") container format. It does not include or link any GPL code.

## CCI format specification and reference implementation

- **Team-Resurgent/libCCI** — <https://github.com/Team-Resurgent/libCCI>
- `FORMAT.md` (the normative CCI format description) and the reference
  `cci_encode` / `cci_extract` tools were used to implement and verify this
  library. libCCI is licensed **GPL-2.0-or-later**.
- No libCCI source code is copied into CCISharp. The reference tools are used
  only as an external interoperability oracle.
- Format derived by Team Resurgent / Team Cerbios for original-Xbox CCI images.

## LZ4 block codec

- The LZ4 block encoder/decoder in `CCISharp/Lz4.cs` is a port of
  **lz4_flex 0.11.3** (<https://github.com/PSeitz/lz4_flex>), MIT License,
  Copyright (c) 2019 Pascal Seitz. It is byte-compatible with `lz4_flex`
  block compression and decodes any conforming LZ4 block.
- The CCI container itself uses raw LZ4 blocks; no frame format is involved.
- `libCCI` bundles LZ4 under the **BSD-2-Clause** license; that code is not
  included here.

## xdvdfs

- **antangelo/xdvdfs** (<https://github.com/antangelo/xdvdfs>) is MIT licensed.
  Its open CCI pull request (#185) and CISO implementation were consulted as
  secondary format references. No xdvdfs code is copied into CCISharp.
