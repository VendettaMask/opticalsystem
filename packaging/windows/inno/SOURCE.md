# Inno Setup resources

Compiler: Inno Setup 6.7.3, official release:
https://github.com/jrsoftware/issrc/releases/tag/is-6_7_3

The optional portable compiler bootstrap pins the official release asset SHA-256:
`9c73c3bae7ed48d44112a0f48e66742c00090bdb5bef71d9d3c056c66e97b732`.
The hash was checked against the official GitHub release metadata on 2026-09-04.
The compiler is cached under ignored `artifacts/tools`, not committed or installed system-wide.

Bundled resources from the same release tag:

- `ChineseSimplified.isl`: `Files/Languages/Unofficial/ChineseSimplified.isl`, maintained by Zhenghan Yang (Kira); original translator attribution is preserved.
- `LICENSE.txt`: upstream `license.txt`, copyright Jordan Russell and Martijn Laan.

One trailing space in a comment in `ChineseSimplified.isl` was removed for repository whitespace checks; translations and attribution are unchanged. `LICENSE.txt` is unmodified.

The upstream license permits commercial use subject to its conditions. The authors request that commercial users purchase a license; consult https://jrsoftware.org/isorder.php for their current policy. This project does not purchase or register a commercial license automatically.
