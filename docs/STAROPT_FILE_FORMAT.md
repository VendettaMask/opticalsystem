# STAROPT Project Format

`.staropt` is the native, lossless project format for Optical System Design. It is a
versioned binary container rather than a renamed JSON file.

## Goals

- identify projects independently of the filename extension
- reject truncated or modified files before constructing an optical model
- preserve every optical configuration and the active configuration
- keep the optical snapshot schema extensible
- write projects atomically so a failed save does not replace the previous file

## Container Layout

All integer values are little-endian.

| Offset | Size | Meaning |
| ---: | ---: | --- |
| 0 | 8 | magic bytes `STAROPT` followed by `0x1A` |
| 8 | 2 | container version, currently `1` |
| 10 | 2 | flags, currently `1` for Brotli compression |
| 12 | 4 | uncompressed payload length |
| 16 | 4 | compressed payload length |
| 20 | 32 | SHA-256 of the uncompressed payload |
| 52 | variable | Brotli-compressed UTF-8 project payload |

The payload has its own project-format version and contains the producing
application name, active-configuration index, and an ordered list of
`OpticSnapshot` objects. The container and payload versions are intentionally
separate so compression/framing can evolve independently from the optical model.

The loader limits the uncompressed payload to 256 MiB, validates all declared
lengths, verifies the SHA-256 digest with a fixed-time comparison, and rejects
unsupported versions. Saving writes a temporary file in the destination directory
and atomically replaces the target only after the complete container is flushed.

## Compatibility

The desktop **Save** command writes only `.staropt`. Opening a `.staropt` project
uses both its extension and its magic bytes, so a renamed project can still be
recognized.

Legacy `.optiland.json`, `.optic.json`, `.json`, and `.optiland` files remain
available as compatibility imports. Python Optiland JSON, Zemax ZMX, CODE V SEQ,
OSLO LEN, and plain sequential text are exchange formats rather than native
projects. They do not replace the source file when the user saves the imported
system; the application prompts for a `.staropt` destination instead.

Workspace layout sessions, application preferences, cached analysis results, and
plugin binaries are deliberately stored outside the project file.
