# The Windows services whose names start with $args[0], for a checkpoint's Services button.
Get-CimInstance Win32_Service -Filter "Name LIKE '$($args[0])%'" |
    Format-Table Name, State, StartMode, DelayedAutoStart, PathName -AutoSize -Wrap
