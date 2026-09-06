$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projects = @(
    @{ Project = 'src/OpenCTS.App/OpenCTS.App.csproj'; Name = 'OpenCTS.App'; Output = 'ScratchASM.exe'; Folder = 'app' },
    @{ Project = 'src/ScratchASM.LanguageHost/ScratchASM.LanguageHost.csproj'; Name = 'ScratchASM.LanguageHost'; Output = 'ScratchASM.LanguageHost.exe'; Folder = 'host' }
)
foreach ($project in $projects) {
    $destination = Join-Path $repositoryRoot ('artifacts/publish/' + $project.Folder)
    dotnet publish (Join-Path $repositoryRoot $project.Project) -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -o $destination --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $($project.Name)" }
    Copy-Item -LiteralPath (Join-Path $destination ($project.Name + '.exe')) -Destination (Join-Path $repositoryRoot $project.Output) -Force
    Write-Output ('Published ' + $project.Output)
}
