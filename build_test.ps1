Write-Host '正在尝试编译项目以验证基础代码正确性...'
 = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not ) {
    # 尝试查找常见路径
     = 'C:\Program Files\dotnet\dotnet.exe'
    if (Test-Path ) {  =  }
}

if () {
    &  build 'src/LongBetterWindows.Host/LongBetterWindows.Host.csproj'
} else {
    Write-Error '未在系统中找到 dotnet CLI，无法进行自动编译测试。'
}
