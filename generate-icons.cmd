@echo off
REM Generates all MSIX packaging icons and the window .ico from TP_ICON.png.
REM Run from the repo root: generate-icons.cmd

set SRC=TP_ICON.png
set PKG=packaging\assets
set APP=src\TermPoint\Assets

if not exist %SRC% (
    echo ERROR: %SRC% not found in repo root.
    exit /b 1
)

echo Generating MSIX assets...
magick %SRC% -resize 44x44   %PKG%\Square44x44Logo.png
magick %SRC% -resize 150x150 %PKG%\Square150x150Logo.png
magick %SRC% -resize 50x50   %PKG%\StoreLogo.png
magick %SRC% -resize x150 -gravity center -background none -extent 310x150 %PKG%\Wide310x150Logo.png

echo Generating targetsize variants...
magick %SRC% -resize 16x16  %PKG%\Square44x44Logo.targetsize-16_altform-unplated.png
magick %SRC% -resize 24x24  %PKG%\Square44x44Logo.targetsize-24.png
magick %SRC% -resize 24x24  %PKG%\Square44x44Logo.targetsize-24_altform-unplated.png
magick %SRC% -resize 32x32  %PKG%\Square44x44Logo.targetsize-32.png
magick %SRC% -resize 32x32  %PKG%\Square44x44Logo.targetsize-32_altform-unplated.png
magick %SRC% -resize 44x44  %PKG%\Square44x44Logo.targetsize-44_altform-unplated.png
magick %SRC% -resize 48x48  %PKG%\Square44x44Logo.targetsize-48.png
magick %SRC% -resize 48x48  %PKG%\Square44x44Logo.targetsize-48_altform-unplated.png
magick %SRC% -resize 256x256 %PKG%\Square44x44Logo.targetsize-256_altform-unplated.png

echo Generating window icon...
magick %SRC% -define icon:auto-resize=256,48,32,24,16 %APP%\app.ico

echo Done. Run build-msix.ps1 to rebuild the package.
