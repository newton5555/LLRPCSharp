# Protocol Definitions

This directory contains the machine-readable protocol definitions maintained by the project. External or legacy LTK XML inputs live under `imports/`; after import and validation they become the normalized `ProtocolModel`. Hand-maintained version deltas and larger extensions should use YAML.

## Layout

```text
imports/xml/
├─ llrp-1.0.1/      LLRP 1.0.1 XML/XSD import sources
└─ extensions/
   └─ impinj/       Local Impinj XML/XSD import sources
extensions/         Vendor and customer extension definitions
llrp-1.1.yaml       LLRP 1.1 delta
llrp-2.0-delta.yaml LLRP 2.0 delta, adapter pending
```

`llrp-definition.schema.yaml` documents the YAML definition shape. It is a format example, not a protocol input to generate.

## YAML Rules

- Use `typeNumber` and `cardinality`; valid cardinality values are `1`, `0-1`, `1-N`, and `0-N`.
- Keep each YAML file focused on one protocol version or one extension.
- YAML Loader and XML Importer both output the same `ProtocolDefinition`; Validator and Generator do not branch on the input format.
- Version deltas are composed with repeatable `--base` inputs. The first base is the complete definition; later bases are merged as deltas before the input delta is validated and generated.
- New items that duplicate names from a base are rejected to avoid silently changing wire identity.

## Extension Generation

```powershell
dotnet run --project src/LlrpNet.ProtocolGenerator.Tool -- `
  --input definitions/my-extension.yaml --output src/MyExtension.Protocol `
  --root-namespace MyExtension.Protocol --version-namespace V1_0_1 `
  --protocol-version 1 --dependency definitions/imports/xml/llrp-1.0.1/llrp-1x0-def.xml `
  --dependency-root-namespace LlrpNet.Protocol `
  --registry-module-name MyExtensionProtocolModule --codecs --verify
```

## LLRP 2.0 Generation

LLRP 2.0 is composed on top of the 1.0.1 XML model plus the 1.1 delta:

```powershell
dotnet run --project src/LlrpNet.ProtocolGenerator.Tool -- `
  --input definitions/llrp-2.0-delta.yaml `
  --base definitions/imports/xml/llrp-1.0.1/llrp-1x0-def.xml `
  --base definitions/llrp-1.1.yaml `
  --output src/LlrpNet.Protocol --root-namespace LlrpNet.Protocol `
  --version-namespace V2_0 --protocol-version 3 --registry-module-name Llrp20StandardModule --codecs
```

## Impinj Generation

The local Impinj 1.0.1 input comes from LTK Impinj Definition Files 10.58.0. The source XML remains local and ignored by Git; generated `.g.cs` files are committed under `LlrpSdk.Extensions.Impinj`.

```powershell
dotnet run --project src/LlrpNet.ProtocolGenerator.Tool -- `
  --input definitions/imports/xml/extensions/impinj/Impinjdef.xml `
  --dependency definitions/imports/xml/llrp-1.0.1/llrp-1x0-def.xml `
  --dependency-root-namespace LlrpNet.Protocol `
  --output src/LlrpSdk.Extensions.Impinj --root-namespace LlrpSdk.Extensions.Impinj `
  --version-namespace V1_0_1 --protocol-version 1 `
  --registry-module-name ImpinjProtocolModule --codecs --verify
```

Without `--verify`, the generator writes missing or changed `.g.cs` files. With `--verify`, it only checks that generated assets are current, which is suitable for CI.

Current input versions, SHA-256 hashes, and usage constraints are tracked in [`docs/references/README.md`](../docs/references/README.md).
