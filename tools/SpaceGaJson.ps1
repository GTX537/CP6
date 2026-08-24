function ConvertFrom-SpaceGaJson {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory, ValueFromPipeline)]
        [string]$Json
    )

    process {
        $parameters = @{}
        $commandName = 'Microsoft.PowerShell.Utility\ConvertFrom-Json'
        $convertFromJson = Get-Command $commandName -CommandType Cmdlet
        if ($convertFromJson.Parameters.ContainsKey('DateKind')) {
            $parameters['DateKind'] = 'String'
        }

        $Json | Microsoft.PowerShell.Utility\ConvertFrom-Json @parameters
    }
}
