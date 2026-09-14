function Assert-LocalStorageDirectory([string]$Value) {
    # SQL's default paths are operator configuration, not permission to export a
    # full source backup to a share. Reject UNC/device paths and mapped drives.
    if ([string]::IsNullOrWhiteSpace($Value) -or $Value -notmatch '^[A-Za-z]:[\\/]' -or
        ![IO.Path]::IsPathFullyQualified($Value)) {
        throw 'SQL storage must use an absolute local fixed-drive directory.'
    }
    $absolute = [IO.Path]::GetFullPath($Value)
    $drive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($absolute))
    if ($drive.DriveType -ne [IO.DriveType]::Fixed) {
        throw 'SQL storage must use an absolute local fixed-drive directory.'
    }
    $directory = [IO.DirectoryInfo]::new($absolute)
    if (!$directory.Exists) { throw 'SQL storage directory must already exist.' }
    while ($null -ne $directory) {
        if (($directory.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw 'SQL storage directory cannot traverse a junction or symbolic link.'
        }
        $directory = $directory.Parent
    }
}
