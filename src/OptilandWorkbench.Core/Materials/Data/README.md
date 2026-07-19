# Embedded Glass Catalog

`glass-catalog.json` is generated from the glass portion of the database shipped with
Python Optiland 0.5.8 by `tools/python-reference/generate_glass_catalog.py`.

The underlying refractiveindex.info database files state that they are dedicated to
the public domain under CC0 1.0. The generated resource retains only the manufacturer,
glass name, valid wavelength interval, dispersion coefficients, and tabulated optical
constants required at runtime.

`zemax-glass-catalogs.ogdb` is the Workbench-owned, schema-versioned and GZip-compressed
database generated from 63 Zemax AGF catalogs by
`tools/OptilandWorkbench.GlassCatalogConverter`. It contains 5,502 source records and
retains catalog identity, all 13 dispersion formula types, thermal/mechanical data,
durability data, valid wavelength ranges, internal transmission, and stress data.
