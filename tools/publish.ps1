param(
    [string]$ContentFolder = (Join-Path $PSScriptRoot '../dist/player.portraitruntime'),
    [string]$PreviewFile,
    [string]$DescriptionFile,
    [string]$Title = '深空内容核心库',
    [UInt64]$WorkshopId = 0
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$content = (Resolve-Path -LiteralPath $ContentFolder).Path.Replace('\','/')
$description = if ($DescriptionFile) { Get-Content -LiteralPath $DescriptionFile -Raw -Encoding utf8 } else { '内容核心库：人物、剧情、物品与数据包。测试版本，游戏内验收待完成。' }
if ([Text.Encoding]::UTF8.GetByteCount($description) -gt 8000) { throw 'Description exceeds 8000 UTF-8 bytes' }
if ($description.Contains('"') -or $Title.Contains('"')) { throw 'VDF title and description cannot contain English double quotes' }
$description = $description.Replace("`r",'').Replace("`n",'\n')
$preview = ''
if ($PreviewFile) {
    $previewPath = (Resolve-Path -LiteralPath $PreviewFile).Path
    if ((Get-Item -LiteralPath $previewPath).Length -ge 1MB) { throw 'Preview must be smaller than 1 MiB' }
    $preview = $previewPath.Replace('\','/')
}
$vdf = @"
"workshopitem"
{
    "appid" "4348910"
    "publishedfileid" "$WorkshopId"
    "contentfolder" "$content"
    "previewfile" "$preview"
    "visibility" "2"
    "title" "$Title"
    "description" "$description"
    "changenote" "Local test build; awaiting game acceptance"
}
"@
$output = Join-Path $projectRoot 'artifacts/workshop_item.vdf'
[IO.Directory]::CreateDirectory((Split-Path -Parent $output)) | Out-Null
[IO.File]::WriteAllText($output, $vdf, [Text.UTF8Encoding]::new($false))
Write-Output "Prepared private Workshop upload descriptor: $output"
Write-Output 'No upload was performed. Complete game acceptance before using SteamCMD to publish.'
