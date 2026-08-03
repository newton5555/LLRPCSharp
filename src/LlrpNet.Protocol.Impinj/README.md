# LLRP Impinj Protocol Extension

This package contains the generated Impinj LLRP 1.0.1 messages, parameters,
enumerations, codecs, and registry module. It is intentionally independent of
`LlrpSdk` and can be used by raw protocol applications that only need Impinj
wire types and codec registration.

The higher-level `LlrpSdk.Extensions.Impinj` package builds on this package and
adds reader settings, inventory contributors, capability policy, and typed
report projections.
