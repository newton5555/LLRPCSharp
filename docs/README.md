# Documentation Index

The detailed [SDK documentation site](https://newton5555.github.io/LLRPCSharp/)
is deployed automatically from docs-site/ on pushes to master.

The root [README](../README.md) is the short project introduction. This
directory contains only the deeper material needed to use, maintain, or extend
the project.

## Use The Project

- [SDK API Guide](guides/sdk-api-guide.md): managed `LlrpReader`, settings,
  inventory, and tag reports.
- [CLI User Guide](guides/cli-user-guide.md): Live Shell workflow, one-shot
  inventory, settings, and tag operations.
- [Current Status](status.md): what is implemented and what is not.

## Develop The Project

- [Architecture Overview](architecture/overview.md): layer boundaries and
  ownership.
- [Source Structure](architecture/source-structure.md): repository and project
  map.
- [Protocol Extension Guide](architecture/protocol-extension-guide.md): adding
  protocol or vendor extensions.
- [Decision Records](adr/README.md): decisions that affect long-term design.
- [Historical Virtual Reader Core and Manager Decision](adr/0006-preset-driven-virtual-reader-manager.md): superseded preset/Manager design retained for architectural history.

## Validate And Reference

- [Reader Interoperability Acceptance](acceptance/reader-interoperability.md):
  real-device and virtual-device release checks.
- [Protocol References](references/README.md): standards and vendor references.
- [Protocol Definitions](../definitions/README.md): definition and code
  generation workflow.

## Releases

- [v2.0.5 Release Notes](releases/v2.0.5.md)
- [v2.0.4 Release Notes](releases/v2.0.4.md)
- [v2.0.3 Release Notes](releases/v2.0.3.md)

The roadmap is maintained for project planning, but is intentionally not part
of the user-facing README path.
