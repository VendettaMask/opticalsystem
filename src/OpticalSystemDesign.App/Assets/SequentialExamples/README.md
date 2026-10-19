# Sequential examples

These 95 ZMX files come from the 14 folders in the locally installed OpticStudio
2026 R1 `Zemax/Samples/Sequential` directory. They replace the application's former
generated Cooke/Tessar sample commands. Folder names and source bytes are retained;
`index.json` records every relative path and SHA-256. Zemax sample content retains
its original third-party provenance and is not authored by this project.
Git attributes preserve prescription bytes without line-ending normalization.

The application uses one ZMX entry per sample. Paired ZOS prescriptions can differ
and are not substituted. ZOS files, ZDA caches, CFG analysis settings and XML
optimization settings are not bundled or interpreted as additional examples.
This example directory is copied to `Examples/Sequential` in build and publish
outputs; opening it does not depend on a local Zemax installation.

Menu inclusion is not numerical certification. Files use the shared ZMX importer
without modification, approximation or relaxed tolerances. Unsupported imports
report their existing error and retain the current document. Successfully imported
special surfaces may still have analysis limitations; see the repository's
Sequential corpus and import-repair comparison reports.
