# Test Lens Samples

The Zemax sequential sources for manual UI and importer testing now live under
`local-data/lens-library/originals/user-zmx/project/samples/lenses`:

- `achromatic-doublet.zmx`: cemented N-BK7/N-F2 doublet with angle fields.
- `double-gauss-50mm.zmx`: symmetric four-group photographic layout with a central stop.
- `telephoto-four-element.zmx`: positive front group, negative telephoto group, and rear field lens.
- `finite-conjugate-macro.zmx`: finite-object, object-height system.
- `real-image-height-demo.zmx`: Zemax `FTYP 3` fields whose chief rays target local image coordinates.

Open the source files from that directory with **File > Open**. Every catalog
glass resolves against the bundled Zemax database; no external AGF installation
is required. This directory retains the converted `.staropt` viewer samples.

The repository-level `Convert-Zemax-Lens.cmd` utility can add a converted
`.staropt` project here while simultaneously installing the same project and its
metadata into the packaged **Database > Lens Library** catalog.
