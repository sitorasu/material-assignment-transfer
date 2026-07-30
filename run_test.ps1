$unity = "C:\Program Files\Unity\Hub\Editor\2022.3.22f1\Editor\Unity.exe"
$project = "C:\Users\sitor\MyFolder\test-avatar-proj"
$result = Join-Path $PSScriptRoot "TestResults\editmode.xml"
$log = Join-Path $PSScriptRoot "TestResults\editmode.log"

New-Item -ItemType Directory -Force (Split-Path $result) | Out-Null

$arguments = @(
    "-batchmode"
    "-runTests"
    "-projectPath", $project
    "-testPlatform", "EditMode"
    "-testFilter", "Sitorasu.MaterialAssignmentTransfer"
    "-testResults", $result
    "-logFile", $log
)

$process = Start-Process -FilePath $unity -ArgumentList $arguments -PassThru -Wait -NoNewWindow

$exitCode = $process.ExitCode
Write-Host "Unity exit code: $exitCode"
exit $exitCode
