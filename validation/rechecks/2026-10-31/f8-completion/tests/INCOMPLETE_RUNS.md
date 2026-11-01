# Incomplete invocations

`final-v4-regression.trx` selected InitialStructure classes in the main product test project, which contains no such tests. It executed zero tests and is not counted as verification.

The first final-v4 module invocation (session50614) was stopped after more than four minutes without any test-result/progress output, while Cold6 was being prepared. It has no completed verdict and is excluded. Final Cold6 model and headless desktop checks are run separately with explicit per-test hang diagnostics and preserved output. Earlier completion-regression105/106 includes one old save-failure UI timeout, whose independent rerun passed; that first failure remains retained.
