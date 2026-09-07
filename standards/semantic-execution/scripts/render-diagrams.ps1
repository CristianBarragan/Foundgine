$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$Diagrams = Join-Path $Root "diagrams"

if (Get-Command plantuml -ErrorAction SilentlyContinue) {
    & plantuml -tsvg (Join-Path $Diagrams "*.puml")
}
elif ($env:PLANTUML_JAR -and (Test-Path $env:PLANTUML_JAR)) {
    & java -jar $env:PLANTUML_JAR -tsvg (Join-Path $Diagrams "*.puml")
}
else {
    throw "PlantUML not found. Install the plantuml CLI or set PLANTUML_JAR to plantuml.jar."
}

& python (Join-Path $Root "scripts/verify-diagrams.py")
