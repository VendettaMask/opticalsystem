# STAROPT Project Format

`.staropt` is the native, lossless project format for Optical System Design. It is a
versioned binary container rather than a renamed JSON file.

## Goals

- identify projects independently of the filename extension
- reject truncated or modified files before constructing an optical model
- reject checksum-valid payloads whose optical state is semantically invalid
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

SHA-256 protects the payload against accidental corruption or modification; it
does not make the decoded optical state trustworthy. After the container checks
pass, each configuration is independently validated before any current document
state is changed.

## Optical Snapshot Validation

The current optical snapshot schema is `3`. Schemas `1` and `2` remain readable
through a bounded migration step, while snapshots outside the supported range are
rejected.

Schema validation enforces these invariants:

- field, wavelength, and surface tables are present and non-empty;
- every wavelength is finite and positive, and exactly one wavelength is primary;
- field coordinates, weights, and vignetting values are finite;
- surface numbers are unique, ordered, and contiguous from `0`;
- thickness, semi-diameter, conic, coordinate transforms, aperture values,
  environment values, pickup scale/offset, solve values, and merit-operand numeric
  values are finite and within their applicable ranges;
- radius pickups and merit operands reference existing surfaces, fields, and
  wavelengths using the format's zero/default and one-based conventions;
- component kinds, recursive child layouts, encoded collection counts, and all
  required collection entries are valid before component factories run.

`NaN` is never accepted. Infinity is accepted only where it has an explicit
optical meaning in the model: plane/infinite curvature radii, infinite grating
periods, and infinite thin-lens focal lengths. It is rejected for wavelengths,
thicknesses, apertures, coordinates, environment state, references, and ordinary
component parameters.

Schemas `1` and `2` used two historical sentinels that are not legal schema-3
state. During migration, an infinite object-space Z coordinate is normalized to
the finite object-plane representation, and a non-finite legacy semi-diameter is
replaced by the historical 10 mm fallback. Dangling legacy pickups or merit
operands are removed. The migrated snapshot is then subjected to the same schema-3
validator, so selecting an older schema version cannot bypass validation.

## Transactional Construction

Loading and applying a snapshot follows this order:

1. validate the STAROPT framing, lengths, compression, version, and checksum;
2. deserialize and migrate a supported legacy snapshot;
3. validate the complete optical state and every cross-reference;
4. construct all fields, wavelengths, surfaces, components, pickups, solves, and
   merit operands in a temporary `Optic`;
5. replace the current optic state only after temporary construction succeeds.

An unknown component, invalid material, malformed phase grid, or other constructor
failure therefore leaves the current document unchanged. Save paths run the same
snapshot validator before serializing or creating a temporary output file, so an
invalid in-memory state cannot be written as a native project.

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
