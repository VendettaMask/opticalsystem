LOADLENS "D:\Projects\opticalsystem\validation\rechecks\2026-11-21\all-output-visual-comparison\representative-six\grating\zemax\screenshot-input.ZMX"
UPDATE ALL
OUTPUT "D:\Projects\opticalsystem\validation\rechecks\2026-11-21\all-output-visual-comparison\representative-six\grating\zemax\progress.txt"
PRINT "interferogram"
OPENANALYSISWINDOW "Int", "D:\Projects\opticalsystem\validation\rechecks\2026-11-21\all-output-visual-comparison\representative-six\grating\zemax\interferogram.CFG"
PAUSE THREADS
window_number = WINL()
EXPORTJPG window_number, "D:\Projects\opticalsystem\validation\rechecks\2026-11-21\all-output-visual-comparison\representative-six\grating\zemax\interferogram.jpg"
CLOSEWINDOW window_number
OUTPUT "D:\Projects\opticalsystem\validation\rechecks\2026-11-21\all-output-visual-comparison\representative-six\grating\zemax\progress.txt"
PRINT "jones-pupil"
OPENANALYSISWINDOW "Pmp", "D:\Projects\opticalsystem\validation\rechecks\2026-11-21\all-output-visual-comparison\representative-six\grating\zemax\jones-pupil.CFG"
PAUSE THREADS
window_number = WINL()
EXPORTJPG window_number, "D:\Projects\opticalsystem\validation\rechecks\2026-11-21\all-output-visual-comparison\representative-six\grating\zemax\jones-pupil.jpg"
CLOSEWINDOW window_number
OUTPUT "D:\Projects\opticalsystem\validation\rechecks\2026-11-21\all-output-visual-comparison\representative-six\grating\zemax\progress.txt"
PRINT "2d-layout"
OPENANALYSISWINDOW "Lay", "D:\Projects\opticalsystem\validation\rechecks\2026-11-21\all-output-visual-comparison\representative-six\grating\zemax\2d-layout.CFG"
PAUSE THREADS
window_number = WINL()
EXPORTJPG window_number, "D:\Projects\opticalsystem\validation\rechecks\2026-11-21\all-output-visual-comparison\representative-six\grating\zemax\2d-layout.jpg"
CLOSEWINDOW window_number
OUTPUT "D:\Projects\opticalsystem\validation\rechecks\2026-11-21\all-output-visual-comparison\representative-six\grating\zemax\progress.txt"
PRINT "3d-layout"
OPENANALYSISWINDOW "L3d", "D:\Projects\opticalsystem\validation\rechecks\2026-11-21\all-output-visual-comparison\representative-six\grating\zemax\3d-layout.CFG"
PAUSE THREADS
window_number = WINL()
EXPORTJPG window_number, "D:\Projects\opticalsystem\validation\rechecks\2026-11-21\all-output-visual-comparison\representative-six\grating\zemax\3d-layout.jpg"
CLOSEWINDOW window_number
OUTPUT "D:\Projects\opticalsystem\validation\rechecks\2026-11-21\all-output-visual-comparison\representative-six\grating\zemax\progress.txt"
PRINT "solid-model"
OPENANALYSISWINDOW "Lsh", "D:\Projects\opticalsystem\validation\rechecks\2026-11-21\all-output-visual-comparison\representative-six\grating\zemax\solid-model.CFG"
PAUSE THREADS
window_number = WINL()
EXPORTJPG window_number, "D:\Projects\opticalsystem\validation\rechecks\2026-11-21\all-output-visual-comparison\representative-six\grating\zemax\solid-model.jpg"
CLOSEWINDOW window_number
OUTPUT "D:\Projects\opticalsystem\validation\rechecks\2026-11-21\all-output-visual-comparison\representative-six\grating\zemax\progress.txt"
PRINT "zernike-vs-field"
OPENANALYSISWINDOW "Zvf", "D:\Projects\opticalsystem\validation\rechecks\2026-11-21\all-output-visual-comparison\representative-six\grating\zemax\zernike-vs-field.CFG"
PAUSE THREADS
window_number = WINL()
EXPORTJPG window_number, "D:\Projects\opticalsystem\validation\rechecks\2026-11-21\all-output-visual-comparison\representative-six\grating\zemax\zernike-vs-field.jpg"
CLOSEWINDOW window_number
OUTPUT "D:\Projects\opticalsystem\validation\rechecks\2026-11-21\all-output-visual-comparison\representative-six\grating\zemax\progress.txt"
PRINT "wavefront-map"
OPENANALYSISWINDOW "Wfm", "D:\Projects\opticalsystem\validation\rechecks\2026-11-21\all-output-visual-comparison\representative-six\grating\zemax\wavefront-map.CFG"
PAUSE THREADS
window_number = WINL()
EXPORTJPG window_number, "D:\Projects\opticalsystem\validation\rechecks\2026-11-21\all-output-visual-comparison\representative-six\grating\zemax\wavefront-map.jpg"
CLOSEWINDOW window_number
OUTPUT "D:\Projects\opticalsystem\validation\rechecks\2026-11-21\all-output-visual-comparison\representative-six\grating\zemax\complete.txt"
PRINT "completed"
CLOSEWINDOW
