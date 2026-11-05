#!/usr/bin/env bash
# Installs Docker Engine (free, open source; not Docker Desktop) inside a WSL Ubuntu distro,
# following Docker's official apt instructions: https://docs.docker.com/engine/install/ubuntu/
#
# Run from Windows (PowerShell):
#   wsl -d Ubuntu-24.04 -u root -- bash "/mnt/c/dev/ved/AI Training/scripts/wsl-install-docker.sh"
#
# Safe to run again: it skips what is already installed.
set -euo pipefail

# Don't inherit the Windows PATH. Otherwise tools from Windows-side installs (e.g. Rancher
# Desktop's Linux docker binaries and credential helper) shadow or break the real Docker here.
# Takes effect after `wsl --terminate <distro>`.
if ! grep -q '^appendWindowsPath *= *false' /etc/wsl.conf 2>/dev/null; then
    printf '\n[interop]\nappendWindowsPath = false\n' >> /etc/wsl.conf
    echo "Set appendWindowsPath=false in /etc/wsl.conf (restart the distro to apply)"
fi

if command -v docker >/dev/null 2>&1 && docker compose version >/dev/null 2>&1; then
    echo "Docker is already installed: $(docker --version)"
else
    apt-get update
    apt-get install -y ca-certificates curl

    # Docker's signing key and apt repository
    install -m 0755 -d /etc/apt/keyrings
    curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
    chmod a+r /etc/apt/keyrings/docker.asc
    # shellcheck disable=SC1091
    . /etc/os-release
    echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] \
https://download.docker.com/linux/ubuntu ${UBUNTU_CODENAME:-$VERSION_CODENAME} stable" \
        > /etc/apt/sources.list.d/docker.list

    apt-get update
    apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
fi

# Start now and on every WSL boot (the distro runs systemd)
systemctl enable --now docker

# Let a normal Linux user run docker without sudo, if one has been created yet
for user in $(getent passwd 1000 | cut -d: -f1); do
    usermod -aG docker "$user"
    echo "Added $user to the docker group"
done

docker version --format 'Docker Engine {{.Server.Version}} is running'
