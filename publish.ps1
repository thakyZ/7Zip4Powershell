[CmdLetBinding(DefaultParameterSetName = 'Public')]
Param()

DynamicParam {
	Function Get-Configurations {
		[CmdLetBinding()]
		[OutputType([string[]])]
		Param()

		Begin {
			[string[]] $Output = @();
		} Process {
			[bool] $Found = $False;
			ForEach ($Line in (Get-Content -Path (Get-ChildItem -LiteralPath $PSScriptRoot -File -Filter '*.sln' | Select-Object -First 1).FullName)) {
				If (-not $Found -and $Line.Trim() -match 'GlobalSection\(SolutionConfigurationPlatforms\) = preSolution') {
					$Found = $True;
				} ElseIf ($Found -and $Line.Trim() -match 'EndGlobalSection') {
					$Found = $False;
				} ElseIf ($Found) {
					$Output += "$(($Line.Trim() -split '\|')[0])";
				}
			}
		} End {
			$Output | Write-Output -NoEnumerate;
		}
	}

  $ConfigurationParameterAttribute = [System.Management.Automation.ParameterAttribute]::new();
  $ConfigurationParameterAttribute.Mandatory = $False;
	[string[]] $Configurations = (Get-Configurations);
  $ConfigurationValidateSetAttribute = [System.Management.Automation.ValidateSetAttribute]::new($Configurations);

  $ConfigurationAttributeCollection = [System.Collections.ObjectModel.Collection[System.Attribute]]::new();
  $ConfigurationAttributeCollection.Add($ConfigurationParameterAttribute)
  $ConfigurationAttributeCollection.Add($ConfigurationValidateSetAttribute)

  $ConfigurationParam = [System.Management.Automation.RuntimeDefinedParameter]::new('Configuration', [string], $ConfigurationAttributeCollection)

  $NuGetApiKeyParameterPublicAttribute = [System.Management.Automation.ParameterAttribute]::new();
  $NuGetApiKeyParameterPublicAttribute.Mandatory = $True;
  $NuGetApiKeyParameterPublicAttribute.ParameterSetName = 'Public';
  $NuGetApiKeyParameterPrivateAttribute = [System.Management.Automation.ParameterAttribute]::new();
  $NuGetApiKeyParameterPrivateAttribute.Mandatory = $False;
  $NuGetApiKeyParameterPrivateAttribute.ParameterSetName = 'Private';
  $NuGetApiKeyAllowNullAttribute = [AllowNullAttribute]::new();

  $NuGetApiKeyAttributeCollection = [System.Collections.ObjectModel.Collection[System.Attribute]]::new();
  $NuGetApiKeyAttributeCollection.Add($NuGetApiKeyParameterPublicAttribute);
  $NuGetApiKeyAttributeCollection.Add($NuGetApiKeyParameterPrivateAttribute);
  $NuGetApiKeyAttributeCollection.Add($NuGetApiKeyAllowNullConditionalAttribute);

  $NuGetApiKeyParam = [System.Management.Automation.RuntimeDefinedParameter]::new('NuGetApiKey', [string], $NuGetApiKeyAttributeCollection)

  $RepositoryParameterAttribute = [System.Management.Automation.ParameterAttribute]::new();
  $RepositoryParameterAttribute.Mandatory = $True;
  $RepositoryParameterAttribute.ParameterSetName = 'Private';
	[string[]] $Repositories = (Get-PSRepository).Name;
	$RepositoryValidateSetAttribute = [System.Management.Automation.ValidateSetAttribute]::new($Repositories);
  $RepositoryValidateNotNullOrEmptyAttribute = [System.Management.Automation.ValidateNotNullOrEmptyAttribute]::new();

  $RepositoryAttributeCollection = [System.Collections.ObjectModel.Collection[System.Attribute]]::new();
  $RepositoryAttributeCollection.Add($RepositoryParameterAttribute);
	$RepositoryAttributeCollection.Add($RepositoryValidateSetAttribute);
  $RepositoryAttributeCollection.Add($RepositoryValidateNotNullOrEmptyAttribute);

  $RepositoryParam = [System.Management.Automation.RuntimeDefinedParameter]::new('Repository', [string], $RepositoryAttributeCollection)

  $ParamDictionary = [System.Management.Automation.RuntimeDefinedParameterDictionary]::new()
  $ParamDictionary.Add('Configuration', $ConfigurationParam);
  $ParamDictionary.Add('NuGetApiKey', $NuGetApiKeyParam);
  $ParamDictionary.Add('Repository', $RepositoryParam);
  Return $ParamDictionary
} Begin {
	[int] $ExitCode = 0;
	[string] $Configuration = $PSBoundParameters['Configuration'] ?? "Release";
	[string] $NuGetApiKey = $PSBoundParameters['NuGetApiKey'];
	[string] $Repository = $PSBoundParameters['Repository'];
} Process {
	Try {
		If ($PSCmdlet.ParameterSetName -eq 'Public' -and [string]::IsNullOrEmpty($NuGetApiKey)) {
			Throw [System.Management.Automation.PSArgumentNullException]::new("Parameter NuGetApiKey cannot be null when it is the only parameter used.");
		}
		If ($PSCmdlet.ParameterSetName -eq 'Private' -and [string]::IsNullOrEmpty($Repository)) {
			Throw [System.Management.Automation.PSArgumentNullException]::new("Parameter Repository cannot be null. `"$($Repository)`"");
		}
		If ([string]::IsNullOrEmpty($Configuration)) {
			Throw [System.Management.Automation.PSArgumentNullException]::new("Parameter Configuration cannot be null.");
		}

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
		[System.Collections.Hashtable] $VersionInfo = (dotnet tool run dotnet-gitversion | ConvertFrom-Json -AsHashtable);
		[string] $Version = "$($VersionInfo.Major).$($VersionInfo.Minor).$($VersionInfo.Patch)";
		[string] $Prerelease = $VersionInfo.NuGetPreReleaseTagV2;

		# Patch the version in the .PSD1 file
		[string] $Psd1File = (Join-Path -Path $ModuleTargetPath -ChildPath '7Zip4PowerShell.psd1')
		Write-Host -Object "Patching version in $($Psd1File) file to $($Version)"
		[string] $Content = (Get-Content -Path $Psd1File -Raw -ErrorAction Stop);
		$Content = ($Content -replace '\$version\$', $Version)
		$Content = ($Content -replace '\$prerelease\$', $Prerelease)
		$Content | Set-Content -Path $Psd1File -ErrorAction Stop;

		If ($PSCmdlet.ParameterSetName -eq 'Public') {
			# Finally publish the module to NuGet
			Publish-Module -Path $ModuleTargetPath -NuGetApiKey $NuGetApiKey;
		} ElseIf ($PSCmdlet.ParameterSetName -eq 'Private') {
			If ([string]::IsNullOrEmpty($NuGetApiKey)) {
				Publish-Module -Path $ModuleTargetPath -Repository $Repository;
			} Else {
				Publish-Module -Path $ModuleTargetPath -Repository $Repository -NuGetApiKey $NuGetApiKey;
			}
		} Else {
			Throw [System.NotSupportedException]::new("Parameter Set Name $($PSCmdlet.ParameterSetName) is not supported.");
		}
	} Catch {
		Write-Error -ErrorRecord $_ | Out-Host;
		$ExitCode = 1;
	}
} End {
	Exit $ExitCode;
}