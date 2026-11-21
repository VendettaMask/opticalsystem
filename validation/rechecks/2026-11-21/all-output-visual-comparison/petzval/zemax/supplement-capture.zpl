LOADLENS "D:\Projects\opticalsystem\validation\rechecks\2026-11-21\all-output-visual-comparison\petzval\zemax\screenshot-input.ZMX"
UPDATE ALL
OPENANALYSISWINDOW "Zvf", "D:\Projects\opticalsystem\validation\rechecks\2026-11-21\all-output-visual-comparison\petzval\zemax\zernike-vs-field.CFG"
PAUSE THREADS
window_number = WINL()
EXPORTJPG window_number, "D:\Projects\opticalsystem\validation\rechecks\2026-11-21\all-output-visual-comparison\petzval\zemax\zernike-vs-field.jpg"
CLOSEWINDOW window_number
OPENANALYSISWINDOW "Wfm", "D:\Projects\opticalsystem\validation\rechecks\2026-11-21\all-output-visual-comparison\petzval\zemax\wavefront.CFG"
PAUSE THREADS
window_number = WINL()
EXPORTJPG window_number, "D:\Projects\opticalsystem\validation\rechecks\2026-11-21\all-output-visual-comparison\petzval\zemax\wavefront-map.jpg"
CLOSEWINDOW window_number
OUTPUT "D:\Projects\opticalsystem\validation\rechecks\2026-11-21\all-output-visual-comparison\petzval\zemax\supplement-complete.txt"
PRINT "completed"
CLOSEWINDOW
