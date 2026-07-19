# Embedded Glass Catalog

`glass-catalog.json` is generated from the glass portion of the database shipped with
Python Optiland 0.5.8 by `tools/python-reference/generate_glass_catalog.py`.

The underlying refractiveindex.info database files state that they are dedicated to
the public domain under CC0 1.0. The generated resource retains only the manufacturer,
glass name, valid wavelength interval, dispersion coefficients, and tabulated optical
constants required at runtime.
