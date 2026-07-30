! Capture one OpticStudio analysis exactly as rendered by the desktop UI.
! Arguments: Lens, Output, Code.

lens_file$ = $GETARG("Lens")
output_dir$ = $GETARG("Output")
analysis_code$ = $GETARG("Code")

LOADLENS lens_file$
UPDATE ALL

text_file$ = output_dir$ + "\zemax-zpl-data.txt"
image_file$ = output_dir$ + "\screenshot.jpg"
status_file$ = output_dir$ + "\zpl-complete.txt"

GETTEXTFILE text_file$, analysis_code$
OPENANALYSISWINDOW analysis_code$
PAUSE THREADS
window_number = WINL()
EXPORTJPG window_number, image_file$
CLOSEWINDOW window_number

OUTPUT status_file$
PRINT analysis_code$
CLOSEWINDOW
