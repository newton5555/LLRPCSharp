# LlrpNet.Protocol.Zebra

Generated Zebra (Moto) LLRP 1.0.1 wire assets: vendor custom messages, custom
parameters, codecs, and the `ZebraProtocolModule` registry entry point.

- Source definition: `definitions/extensions/zebra.yml` (hand-maintained YAML,
  FX9600-supported subset transcribed from the Zebra Interface Control Guide,
  Rev A, March 2025; vendor ID 161).
- Wire identity: custom messages under MessageType 1023 / custom parameters
  under ParameterType 327, keyed by `(version, vendorId=161, subtype)`.
- This package only depends on `LlrpNet.Protocol` and `LlrpNet.Core`; it
  never references `LlrpSdk`. Register it with
  `ZebraProtocolModule.Register(registry)` before connecting.
- Do not hand-edit generated `.g.cs` files; regenerate from
  `definitions/extensions/zebra.yml` via the protocol generator tool.
