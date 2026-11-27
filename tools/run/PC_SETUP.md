# Setting up the PC so this Mac can reach it

Once, about 20 minutes, from your decisions of 26 November (Q8): **Tailscale and SSH**, and the PC
may stay on overnight. When it's done, `ssh kehai-pc` from the Mac answers, and the next tasks
(`kehai-run --on pc`, C3.2 on) can start jobs there.

**What the three parts are:**

| Part | What it is | Like |
|---|---|---|
| Tailscale | a private network between your own devices, wherever they are | a VPN that only connects your things |
| OpenSSH Server | Windows' built-in way to accept a terminal from another computer | the Mac's Remote Login |
| The Mac's key | a file that proves it's your Mac, so no password is typed | a house key instead of a code at the door |

Each `powershell` block goes into **PowerShell as administrator** on the PC (Start → type
PowerShell → right-click → Run as administrator). Each `bash` block goes into Terminal on the Mac.

## 1. Tailscale on both

On the PC:

```powershell
winget install --id Tailscale.Tailscale -e
```

Open Tailscale from the Start menu and sign in. On the Mac, install it from tailscale.com/download
(or the App Store) and sign in **with the same account**. Then, on the PC, note its name:

```powershell
tailscale status
```

The first column of the PC's own line is its address (like `100.x.y.z`); the second is its name (like `desktop-abc123`).

## 2. The SSH server on the PC

```powershell
Add-WindowsCapability -Online -Name OpenSSH.Server~~~~0.0.1.0
Set-Service -Name sshd -StartupType Automatic
Start-Service sshd
New-NetFirewallRule -Name kehai-ssh -DisplayName "SSH for Kehai (Tailscale only)" -Direction Inbound -Protocol TCP -LocalPort 22 -RemoteAddress 100.64.0.0/10 -Action Allow
New-ItemProperty -Path "HKLM:\SOFTWARE\OpenSSH" -Name DefaultShell -Value "C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe" -PropertyType String -Force
```

The firewall rule lets SSH in only from your Tailscale devices, not from the rest of the internet or your Wi-Fi.

## 3. The Mac's key

On the Mac, make a key just for the PC and show it:

```bash
ssh-keygen -t ed25519 -f ~/.ssh/kehai_pc -C "kehai mac" -N ""
```

```bash
cat ~/.ssh/kehai_pc.pub
```

Copy the line it prints. On the PC, paste it between the quotes below. Windows reads an
administrator's keys from one shared file, not the usual one:

```powershell
$key = "PASTE THE LINE HERE"
Add-Content -Path "$env:ProgramData\ssh\administrators_authorized_keys" -Value $key
icacls "$env:ProgramData\ssh\administrators_authorized_keys" /inheritance:r /grant "Administrators:F" /grant "SYSTEM:F"
Restart-Service sshd
```

## 4. A short name on the Mac

Replace `desktop-abc123` with the PC's name from step 1, and `YOURNAME` with your Windows user name
(on the PC, `whoami` shows `computer\YOURNAME`):

```bash
cat >> ~/.ssh/config <<'EOF'
Host kehai-pc
  HostName desktop-abc123
  User YOURNAME
  IdentityFile ~/.ssh/kehai_pc
EOF
```

## 5. Staying on overnight

So a long training run isn't cut off by sleep (the screen may still turn off):

```powershell
powercfg /change standby-timeout-ac 0
powercfg /change hibernate-timeout-ac 0
```

## Check

On the Mac:

```bash
ssh kehai-pc hostname
```

It should print the PC's name with no password asked. If it does, tell Claude "the PC answers"
(Y3.1 is done). If not, the usual causes:

| What you see | Fix |
|---|---|
| `Could not resolve hostname` | Tailscale isn't signed in on one of them, or the name in step 4 is wrong (try the `100.x.y.z` address instead) |
| `Connection timed out` | the SSH service isn't running (step 2's `Start-Service sshd`), or the firewall rule is missing |
| It asks for a password | the key went into the wrong file, or its permissions weren't set: redo step 3 |

Nothing here installs Unity, Git or Python on the PC: those come with C7.3 (`kehai-pull`) and the
`kehai-assets` set-up page.
