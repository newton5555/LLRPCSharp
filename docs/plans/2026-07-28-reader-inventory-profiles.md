# Reader Inventory Profile Plan

## Goal

Provide a safe, explainable inventory starting point without confusing it with the reader's persisted configuration.

## Inputs and ownership

| Input | Source | Meaning |
|---|---|---|
| Identity / capabilities | Standard connection messages | Facts: model, firmware, antenna count, valid Tx/Rx/RF/frequency tables. |
| Current configuration | `QueryConfigurationAsync()` | Device state currently stored or active on the reader. |
| Inventory Profile | SDK or vendor extension | Local recommendation that selects valid capability values. |
| Application settings | `ReaderSettings` | Explicit business overrides for one inventory operation. |

Precedence is: explicit application settings > matched profile > standard sparse baseline. A profile never writes the reader; `ApplyConfigurationAsync()` remains the only explicit persistent-configuration path.

## Initial extension point

`IInventoryProfileContributor` is a reader-extension contract. It may return `InventoryCompilationDefaults` for a matched model. The core SDK consumes at most one active profile and compiles its values into the standard AISpec; it does not contain vendor/model branches.

The first implementation is `LlrpSdk.Extensions.Seuic` for UF40. It uses only standard LLRP parameters and has no custom message/type dependency.

## Milestones

1. **M1 — completed baseline:** core sparse ROSpec, explicit `START_ROSPEC`, identity/capability snapshot, and extension-owned Seuic UF40 compatibility defaults.
2. **M2 — profile result API:** expose a typed `GetDefaultInventorySettings` result with profile ID, source and explanation; do not mutate reader state.
3. **M3 — merge model:** represent per-field provenance and ensure `ReaderSettings` overrides profile recommendations without making unspecified fields look explicit.
4. **M4 — vendor profiles:** add validated Impinj and Zebra profiles only after device captures; vendor custom report/config choices remain contributor-owned.
5. **M5 — CLI:** show the selected profile and resolved values in `inventory settings` / `inventory start`; offer preview before any persistent configuration write.

## Non-goals

- Do not infer a profile from a codec registration alone.
- Do not turn a profile into a `SET_READER_CONFIG` write.
- Do not deserialize arbitrary `ReaderSettings.Extensions` as a profile format.
- Do not claim a model is supported until its resulting ROSpec is hardware-verified.
