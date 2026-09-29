$srcDir = 'C:\Users\moham\.zcode\workspace\default\LiveTypeBridge\publish'
$destDir = 'C:\Users\moham\Desktop\LiveTypeBridge'

New-Item -ItemType Directory -Force -Path $destDir | Out-Null

Copy-Item "$srcDir\LiveTypeBridge.exe" $destDir -Force
Copy-Item 'C:\Users\moham\.zcode\workspace\default\LiveTypeBridge\README.md' $destDir -Force
Copy-Item 'C:\Users\moham\.zcode\workspace\default\LiveTypeBridge\tools\test-phone.html' $destDir -Force

$size = [math]::Round((Get-Item "$destDir\LiveTypeBridge.exe").Length / 1MB, 0)
Write-Output "INSTALLED to $destDir (exe = $size MB)"
Get-ChildItem $destDir | Select-Object Name, @{n='MB';e={[math]::Round($_.Length/1MB,1)}} | Format-Table -AutoSize
