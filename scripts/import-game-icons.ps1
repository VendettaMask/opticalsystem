param(
    [Parameter(Mandatory = $true)]
    [string] $SourceRoot,

    [string] $OutputPath = "src/OptilandWorkbench.App/Assets/Icons/game-icons-isekai.json"
)

$ErrorActionPreference = "Stop"

$icons = [ordered]@{
    "activity"                    = @{ source = "sbed/pulse.svg" }
    "aperture"                    = @{ source = "lorc/james-bond-aperture.svg" }
    "arrow-down"                  = @{ source = "delapouite/plain-arrow.svg"; rotation = 90 }
    "arrow-left"                  = @{ source = "delapouite/plain-arrow.svg"; rotation = 180 }
    "arrow-right"                 = @{ source = "delapouite/plain-arrow.svg" }
    "arrow-right-left"            = @{ source = "lorc/interleaved-arrows.svg" }
    "arrow-up"                    = @{ source = "delapouite/plain-arrow.svg"; rotation = 270 }
    "badge-percent"               = @{ source = "delapouite/pie-chart.svg" }
    "box"                         = @{ source = "delapouite/cardboard-box-closed.svg" }
    "boxes"                       = @{ source = "delapouite/cargo-crate.svg" }
    "calculator"                  = @{ source = "delapouite/abacus.svg" }
    "chart-column"                = @{ source = "delapouite/histogram.svg" }
    "chart-line"                  = @{ source = "delapouite/chart.svg" }
    "chart-no-axes-combined"      = @{ source = "delapouite/growth.svg" }
    "chart-scatter"               = @{ source = "delapouite/multiple-targets.svg" }
    "check"                       = @{ source = "delapouite/check-mark.svg" }
    "chevron-down"                = @{ source = "lorc/arrowhead.svg"; rotation = 90 }
    "chevron-left"                = @{ source = "lorc/arrowhead.svg"; rotation = 180 }
    "chevron-right"               = @{ source = "lorc/arrowhead.svg" }
    "chevron-up"                  = @{ source = "lorc/arrowhead.svg"; rotation = 270 }
    "circle"                      = @{ source = "delapouite/plain-circle.svg" }
    "circle-alert"                = @{ source = "lorc/laser-warning.svg" }
    "circle-check"                = @{ source = "delapouite/confirmed.svg" }
    "circle-dot"                  = @{ source = "lorc/concentration-orb.svg" }
    "circle-question-mark"        = @{ source = "sbed/help.svg" }
    "clipboard-check"             = @{ source = "delapouite/checklist.svg" }
    "clipboard-copy"              = @{ source = "lorc/papers.svg" }
    "cone"                        = @{ source = "delapouite/traffic-cone.svg" }
    "copy"                        = @{ source = "delapouite/files.svg" }
    "cylinder"                    = @{ source = "delapouite/barrel.svg" }
    "database"                    = @{ source = "delapouite/database.svg" }
    "disc-2"                      = @{ source = "delapouite/compact-disc.svg" }
    "download"                    = @{ source = "delapouite/cloud-download.svg" }
    "drafting-compass"            = @{ source = "lorc/compass.svg" }
    "ellipse"                     = @{ source = "delapouite/plain-circle.svg"; scaleX = 1.18; scaleY = 0.78 }
    "external-link"               = @{ source = "delapouite/exit-door.svg" }
    "file"                        = @{ source = "john-redman/paper.svg" }
    "file-down"                   = @{ source = "lorc/tied-scroll.svg" }
    "file-input"                  = @{ source = "delapouite/paper-tray.svg" }
    "file-plus"                   = @{ source = "delapouite/scroll-quill.svg" }
    "file-text"                   = @{ source = "lorc/scroll-unfurled.svg" }
    "focus"                       = @{ source = "delapouite/crosshair.svg" }
    "folder-open"                 = @{ source = "delapouite/open-folder.svg" }
    "gem"                         = @{ source = "lorc/cut-diamond.svg" }
    "grid-2x2"                    = @{ source = "skoll/divided-square.svg" }
    "image-up"                    = @{ source = "delapouite/cloud-upload.svg" }
    "infinity"                    = @{ source = "various-artists/infinity.svg" }
    "list-tree"                   = @{ source = "lorc/checkbox-tree.svg" }
    "lock"                        = @{ source = "delapouite/plain-padlock.svg" }
    "lock-keyhole"                = @{ source = "delapouite/dial-padlock.svg" }
    "maximize-2"                  = @{ source = "delapouite/expand.svg" }
    "move-vertical"               = @{ source = "delapouite/vertical-flip.svg" }
    "package-search"              = @{ source = "delapouite/archive-research.svg" }
    "panel-top"                   = @{ source = "delapouite/window.svg" }
    "panels-top-left"             = @{ source = "delapouite/window-bars.svg" }
    "picture-in-picture-2"        = @{ source = "delapouite/window.svg" }
    "play"                        = @{ source = "guard13007/play-button.svg" }
    "plus"                        = @{ source = "lorc/cross-mark.svg"; rotation = 45 }
    "power"                       = @{ source = "lord-berandas/power-button.svg" }
    "radius"                      = @{ source = "delapouite/convergence-target.svg" }
    "redo"                        = @{ source = "lorc/return-arrow.svg" }
    "refresh-cw"                  = @{ source = "lorc/cycle.svg" }
    "replace"                     = @{ source = "lorc/recycle.svg" }
    "rotate-ccw"                  = @{ source = "delapouite/anticlockwise-rotation.svg" }
    "route"                       = @{ source = "delapouite/direction-signs.svg" }
    "rows-3"                      = @{ source = "delapouite/ancient-columns.svg" }
    "ruler"                       = @{ source = "delapouite/measure-tape.svg" }
    "save"                        = @{ source = "delapouite/save.svg" }
    "scan-search"                 = @{ source = "lorc/radar-sweep.svg" }
    "search"                      = @{ source = "lorc/magnifying-glass.svg" }
    "settings"                    = @{ source = "lorc/gears.svg" }
    "shuffle"                     = @{ source = "delapouite/split-arrows.svg" }
    "sliders-horizontal"          = @{ source = "delapouite/settings-knobs.svg" }
    "sparkles"                    = @{ source = "delapouite/sparkles.svg" }
    "table"                       = @{ source = "delapouite/table.svg" }
    "table-2"                     = @{ source = "delapouite/round-table.svg" }
    "telescope"                   = @{ source = "delapouite/telescope.svg" }
    "thermometer-sun"             = @{ source = "delapouite/thermometer-hot.svg" }
    "trash"                       = @{ source = "delapouite/trash-can.svg" }
    "trash-2"                     = @{ source = "delapouite/trash-can.svg" }
    "type"                        = @{ source = "lorc/quill-ink.svg" }
    "undo"                        = @{ source = "lorc/return-arrow.svg"; rotation = 180 }
    "upload"                      = @{ source = "delapouite/cloud-upload.svg" }
    "wand-sparkles"               = @{ source = "lorc/crystal-wand.svg" }
    "zoom-in"                     = @{ source = "delapouite/expand.svg" }
    "zoom-out"                    = @{ source = "delapouite/contract.svg" }
}

$sourceRootPath = (Resolve-Path -LiteralPath $SourceRoot).Path
$revision = (& git -C $sourceRootPath rev-parse HEAD).Trim()
$resultIcons = [ordered]@{}

foreach ($entry in $icons.GetEnumerator()) {
    $relativeSource = $entry.Value.source
    $sourcePath = Join-Path $sourceRootPath $relativeSource
    if (-not (Test-Path -LiteralPath $sourcePath)) {
        throw "Missing upstream icon: $relativeSource"
    }

    [xml] $svg = Get-Content -Raw -LiteralPath $sourcePath
    $paths = @($svg.svg.path | ForEach-Object { $_.d } | Where-Object {
        $_ -and ($_ -replace '\s', '') -ne 'M00h512v512H0z'
    })
    if ($paths.Count -eq 0) {
        throw "No foreground SVG paths found: $relativeSource"
    }

    $folder = ($relativeSource -split '/')[0]
    $resultIcons[$entry.Key] = [ordered]@{
        source = $relativeSource
        author = $folder
        rotation = [double] ($entry.Value.rotation ?? 0)
        scaleX = [double] ($entry.Value.scaleX ?? 1)
        scaleY = [double] ($entry.Value.scaleY ?? 1)
        paths = $paths
    }
}

$result = [ordered]@{
    package = "Game-icons.net"
    repository = "https://github.com/game-icons/icons"
    revision = $revision
    license = "CC BY 3.0"
    icons = $resultIcons
}

$resolvedOutput = Join-Path (Get-Location) $OutputPath
$outputDirectory = Split-Path -Parent $resolvedOutput
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $resolvedOutput -Encoding utf8NoBOM

Write-Host "Imported $($resultIcons.Count) Game-icons.net mappings from $revision."
