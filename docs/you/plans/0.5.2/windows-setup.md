# Connect the Mac to the Windows PC

Use this after you choose home-network access or Tailscale (Q8) and have the PC available (Q19).
This proves that the Mac can start a command on the PC. It leaves Unity closed.

## 1. Pick the addresses

| Choice | What to do |
|---|---|
| Home network | Connect both computers to the same home network. On Windows run `ipconfig`, then use the IPv4 address of your active Ethernet or Wi-Fi connection. On the Mac, find its IPv4 address under **System Settings → Network → Wi-Fi or Ethernet → Details → TCP/IP**. These addresses can change when you reconnect. |
| Tailscale | Install it on both computers and sign in to the same Tailscale network. Copy both devices' Tailscale IPv4 addresses from **Machines** in the Tailscale console. Kehai uses Windows' OpenSSH server over this private link. [Install on Windows](https://tailscale.com/docs/install/windows), [connect to devices](https://tailscale.com/docs/how-to/connect-to-devices). |

## 2. Start OpenSSH on Windows

Open **PowerShell as administrator**. Replace `MAC_ADDRESS` with the Mac's address from step 1.
The firewall rule gives just that Mac access on port 22. Keep port 22 private to your network.

```powershell
$macAddress = "MAC_ADDRESS"
$feature = "OpenSSH.Server~~~~0.0.1.0"
if ((Get-WindowsCapability -Online -Name $feature).State -ne "Installed") {
    Add-WindowsCapability -Online -Name $feature
}
$rule = "OpenSSH-Server-In-TCP"
if (Get-NetFirewallRule -Name $rule -ErrorAction SilentlyContinue) {
    Set-NetFirewallRule -Name $rule -Enabled True -RemoteAddress $macAddress
} else {
    New-NetFirewallRule -Name $rule -DisplayName "OpenSSH Server" -Direction Inbound `
        -Action Allow -Protocol TCP -LocalPort 22 -RemoteAddress $macAddress
}
Start-Service sshd
Set-Service sshd -StartupType Automatic
ssh-keygen -lf "$env:ProgramData\ssh\ssh_host_ed25519_key.pub"
```

Keep the last line's fingerprint to compare when the Mac connects.
[Microsoft's OpenSSH setup](https://learn.microsoft.com/en-us/windows-server/administration/openssh/openssh_install_firstuse?pivots=windows-11).

## 3. Give Windows the Mac's public key

In Terminal on the Mac, generate a key if you haven't already made this one:

```sh
test -f ~/.ssh/kehai_pc.pub || ssh-keygen -t ed25519 -f ~/.ssh/kehai_pc -C kehai-pc
cat ~/.ssh/kehai_pc.pub
```

Copy the one line ending in `kehai-pc`. Keep `~/.ssh/kehai_pc`, the private half, on the Mac.
On the PC open PowerShell signed in as the Windows user that will run Kehai. Use a local user
that can open the project and its build folders. Paste the public key into the prompt below:

```powershell
$publicKey = Read-Host "Paste the Mac public key"
$sshFolder = Join-Path $HOME ".ssh"
New-Item -ItemType Directory -Force -Path $sshFolder | Out-Null
Add-Content -Path (Join-Path $sshFolder "authorized_keys") -Value $publicKey -Encoding ASCII
whoami
```

This uses a standard Windows user. If you choose an administrator account, use Microsoft's
administrator key location and permissions instead. [Microsoft's Windows key setup](https://learn.microsoft.com/en-us/windows-server/administration/openssh/openssh_keymanagement).

## 4. Try one command from the Mac

Add this entry to `~/.ssh/config`. Replace `PC_ADDRESS` and `WINDOWS_USERNAME` with the PC address
and the local account name from `whoami` (the part after the backslash).

```sshconfig
Host kehai-pc
    HostName PC_ADDRESS
    User WINDOWS_USERNAME
    IdentityFile ~/.ssh/kehai_pc
    IdentitiesOnly yes
```

Run `ssh -o HostKeyAlgorithms=ssh-ed25519 kehai-pc hostname`. On the first connection, compare
the fingerprint to the one you wrote down in step 2 before accepting it. A successful run prints
the Windows computer's name. Say whether the PC may stay on overnight before scheduling jobs.
