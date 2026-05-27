$ErrorActionPreference = "Stop"

$Server = "38.76.160.81"
$User = "root"
$Port = 22
$KeyPath = "$HOME\.ssh\id_ed25519"
$AppDir = "/opt/cp6"
$ProjectRoot = Split-Path -Parent $PSCommandPath
$TempArchive = Join-Path $env:TEMP "cp6-deploy.tar.gz"

if (-not (Test-Path $KeyPath)) {
    ssh-keygen -q -t ed25519 -f $KeyPath -N ""
}

if (-not (Test-Path "$KeyPath.pub")) {
    throw "Public key not found: $KeyPath.pub"
}

$pubKeyContent = (Get-Content "$KeyPath.pub" -Raw).Trim()

Write-Host ""
Write-Host "CP6 server bootstrap" -ForegroundColor Cyan
Write-Host "Server: $Server" -ForegroundColor Cyan
Write-Host ""
Write-Host "The next SSH step requires the server password once." -ForegroundColor Yellow
Write-Host "After that, the current local project directory will be uploaded and deployed." -ForegroundColor Yellow
Write-Host ""

$remoteKeySetup = @"
mkdir -p ~/.ssh
touch ~/.ssh/authorized_keys
grep -qxF '$pubKeyContent' ~/.ssh/authorized_keys || echo '$pubKeyContent' >> ~/.ssh/authorized_keys
chmod 700 ~/.ssh
chmod 600 ~/.ssh/authorized_keys
echo KEY_READY
"@

ssh -o StrictHostKeyChecking=no -p $Port "$User@$Server" $remoteKeySetup

Write-Host "Uploading current project files..." -ForegroundColor Yellow
$remoteUpload = @"
rm -rf '$AppDir'
mkdir -p '$AppDir'
"@

if (Test-Path $TempArchive) {
    Remove-Item $TempArchive -Force
}

tar -czf $TempArchive `
    --exclude .git `
    --exclude .claude `
    --exclude .vscode `
    --exclude backend.log `
    --exclude */bin `
    --exclude */obj `
    --exclude cp6.web/node_modules `
    --exclude docs/_manual_render `
    -C $ProjectRoot .

ssh -i $KeyPath -o StrictHostKeyChecking=no -p $Port "$User@$Server" $remoteUpload
scp -i $KeyPath -O -P $Port $TempArchive "${User}@${Server}:/tmp/cp6-deploy.tar.gz"

$remoteDeploy = @'
set -euo pipefail
export DEBIAN_FRONTEND=noninteractive

apt-get update -qq
apt-get install -y -qq ca-certificates curl gnupg git

if ! command -v docker >/dev/null 2>&1; then
  install -m 0755 -d /etc/apt/keyrings
  curl -fsSL https://download.docker.com/linux/ubuntu/gpg | gpg --dearmor -o /etc/apt/keyrings/docker.gpg
  chmod a+r /etc/apt/keyrings/docker.gpg
  . /etc/os-release
  echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/ubuntu ${VERSION_CODENAME} stable" > /etc/apt/sources.list.d/docker.list
  apt-get update -qq
  apt-get install -y -qq docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
  systemctl enable --now docker
fi

tar -xzf /tmp/cp6-deploy.tar.gz -C "$AppDir"
rm -f /tmp/cp6-deploy.tar.gz

cd "$AppDir"
docker compose down --remove-orphans || true
docker compose up -d --build
docker compose ps
'@

ssh -i $KeyPath -o StrictHostKeyChecking=no -p $Port "$User@$Server" $remoteDeploy

if (Test-Path $TempArchive) {
    Remove-Item $TempArchive -Force
}

Write-Host ""
Write-Host "Deployment completed." -ForegroundColor Green
Write-Host "Frontend:  http://$Server`:8080" -ForegroundColor White
Write-Host "API:       http://$Server`:9991/swagger" -ForegroundColor White
Write-Host "RabbitMQ:  http://$Server`:15672" -ForegroundColor White
