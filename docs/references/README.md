# Protocol Source References

> Verification date: 2026-07-24  
> Hash algorithm: SHA-256

This file records the exact protocol-definition and standards-reference versions used in the current workspace. The user supplied these materials. Until authoritative download URLs, download dates, and redistribution permissions are recorded, standards PDFs are local verification material and are not published with source releases.

| Version / Purpose | Workspace Path | SHA-256 | Current Use |
|---|---|---|---|
| LLRP 1.0.1 LTK definition | `definitions/imports/xml/llrp-1.0.1/llrp-1x0-def.xml` | `53D07A1A8493E6540F8CA8E1DFD934A4C548A035065036138618B88D0E3C18EC` | Primary machine-readable import source; cross-checked with the standard PDF |
| Impinj LTK Definition Files 10.58.0 | `definitions/imports/xml/extensions/impinj/Impinjdef.xml` | `5AE82816476153B4BB3CA52EE5269886F4F4D917C339FB881252C0D6ED4E0BD2` | Local generation input; 4 custom messages, 104 custom parameters, 49 custom enumerations; original file is not committed |
| LLRP 1.0.1 XML Schema | `definitions/imports/xml/llrp-1.0.1/llrp-1x0.xsd` | `2B07B257848F934C102E5048A2F9748D87DE85F37A663C04D7800AA09E7B74DF` | XML representation validation; not the sole source of truth for binary field widths |
| LLRP 1.0.1 Standard (2007-08-13) | `references/standards/llrp-1.0.1/llrp_1_0_1-standard-20070813.pdf` | `113C91782926B289286914CFFD743C2D7D623CA5CE255A4D0B7FE08B404D7264` | 1.0.1 field, constraint, and binary layout verification |
| LLRP 1.1 Standard (2010-10-13) | `references/standards/llrp-1.1/llrp_1_1-standard-20101013.pdf` | `23C7BDFD382B7F76918A712EF86E8867FE6DB2262B62B7B4CB529B1B82F3F47C` | 1.1 definition and version-delta verification |
| LLRP 1.1 Conformance (2010-10-13) | `references/standards/llrp-1.1/llrp_1_1-conformance-20101013.pdf` | `A2A09874FF0708C59B028D1E1DB2906A487D09D79209EAE0853F453B94E2B25D` | Conformance test design |
| LLRP 2.0 Standard (2021-01-27) | `references/standards/llrp-2.0/LLRP_standard_i2_r_2021-01-27.pdf` | `C886D011086737EEAED3DBEFBCB472F5A7D6AE70B19BC26DE825D38761BBB7B1` | 2.0 delta, Gen2v2, and version-negotiation verification |

## Known Constraints

- The core definition XML can currently be used as the main 1.0.1 import input; its file header declares Apache License 2.0.
- XSD and definition XML differ in message counts and some field-width expressions. Binary codec generation should use the cross-check between definition XML and the standard PDF.
- The local Impinj definition is marked confidential/proprietary and remains excluded by `.gitignore`. The hash here is only for local traceability and generation consistency; it is not redistribution permission.
- The current input was verified from the user-provided `LTK_Impinj_Definition_Files_10_58_0.zip`. Future updates must record source version, updated hashes, and protocol regression results.
