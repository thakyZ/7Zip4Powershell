[CmdLetBinding(DefaultParameterSetName = 'Public')]
param(
	[Parameter(Mandatory = $False)]
	[ValidateSet('Release', 'Debug')]
	[string]
	$Configuration = 'Release',

	[Parameter(Mandatory = $True,
	           ParameterSetName = 'Public')]
	[Parameter(Mandatory = $False,
	           ParameterSetName = 'Private')]
	[string]
	$NuGetApiKey,

	[Parameter(Mandatory = $True,
	           ParameterSetName = 'Private')]
	[ValidateNotNullOrEmpty()]
	[string]
	$Source
)

Begin {
	[int] $ExitCode = 0;
} Process {
	Try {
		# Compile
		& dotnet build --configuration $Configuration;

		# For publishing we need a folder with the same name as the module
		[string] $ModuleTargetPath = (Join-Path -Path $PSScriptRoot -ChildPath 'Module' -AdditionalChildPath @('7Zip4Powershell'));
		If (Test-Path $ModuleTargetPath) {
			Remove-Item $ModuleTargetPath -Recurse -ErrorAction Stop;
		}
		New-Item $ModuleTargetPath -ItemType Directory | Out-Null;

		# Copy all required files to that folder
		Copy-Item -Path (Join-Path -Path $PSScriptRoot -ChildPath '7Zip4Powershell' -AdditionalChildPath @('bin', $Configuration, 'net8.0', '*.*')) -Exclude @('JetBrains.Annotations.dll') -Destination $ModuleTargetPath;

		# Determine the version
		& dotnet tool restore;
		[Hashtable] $VersionInfo = (dotnet tool run dotnet-gitversion | ConvertFrom-Json -AsHashtable);
		[string] $Version = "$($VersionInfo.Major).$($VersionInfo.Minor).$($VersionInfo.Patch)";
		[string] $Prerelease = $VersionInfo.NuGetPreReleaseTagV2;

		# Patch the version in the .PSD1 file
		[string] $Psd1File = (Join-Path -Path $moduleTargetPath -ChildPath '7Zip4PowerShell.psd1')
		Write-Host "Patching version in $(Psd1File) file to $(Version)"
		[string] $Content = (Get-Content $Psd1File -Raw -ErrorAction Stop);
		$Content = ($Content -replace '\$version\$', $Version)
		$Content = ($Content -replace '\$prerelease\$', $Prerelease)
		$Content | Set-Content $Psd1File -ErrorAction Stop;

		If ($PSCmdlet.ParameterSetName -eq 'Public') {
			# Finally publish the module to NuGet
			Publish-Module -Path $ModuleTargetPath -NuGetApiKey $NuGetApiKey;
		} ElseIf ($PSCmdlet.ParameterSetName -eq 'Private') {
			If ($Null -eq $NuGetApiKey) {
				Publish-Module -Path $ModuleTargetPath -Source $Source;
			} Else {
				Publish-Module -Path $ModuleTargetPath -Source $Source -NuGetApiKey $NuGetApiKey;
			}
		} Else {
			Throw [NotSupportedException]::new("Parameter Set Name $($PSCmdlet.ParameterSetName) is not supported.");
		}
	} Catch {
		Write-Error -ErrorRecord $_ | Out-Host;
		$ExitCode = 1;
	}
} End {
	Exit $ExitCode;
}