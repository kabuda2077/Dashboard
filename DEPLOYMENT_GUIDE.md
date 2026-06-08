# 快速部署指南

## 🚀 快速开始

### 方法一：使用发布脚本（推荐）

```powershell
# 自动构建并打包
powershell -ExecutionPolicy Bypass -File .\create-release.ps1
```

这个脚本会：
1. ✅ 自动构建前端和后端
2. ✅ 将 publish 目录打包成 ZIP
3. ✅ 创建版本说明文件
4. ✅ 保存到 `releases` 文件夹
5. ✅ 自动打开文件夹供你复制

---

### 方法二：手动复制（快速）

```powershell
# 1. 构建项目
powershell -ExecutionPolicy Bypass -File .\build.ps1

# 2. 打开 publish 目录
explorer bin\Release\net9.0-windows\win-x64\publish
```

然后：
1. 关闭正在运行的 Dashboard 应用
2. 复制 `publish` 文件夹内的**所有文件**
3. 粘贴到你的安装目录（覆盖旧文件）
4. 运行 Dashboard.exe

---

## ⚠️ 重要提示

### 必须复制所有文件

不要只复制 `Dashboard.exe`！必须复制整个 `publish` 文件夹的内容，包括：

- ✅ Dashboard.exe
- ✅ Dashboard.dll
- ✅ resources 文件夹（包含 Web UI）
- ✅ 所有 DLL 依赖文件
- ✅ resources 文件夹中的图标文件

### 部署前检查

- [ ] 关闭正在运行的 Dashboard 应用
- [ ] 备份当前版本（可选但推荐）
- [ ] 确保有 .NET 9 Desktop Runtime
- [ ] 确保有 WebView2 Runtime

---

## 📦 文件结构

部署后你的目录应该包含：

```
Dashboard/
├── Dashboard.exe              # 主程序
├── Dashboard.dll              # 应用库
├── *.dll                      # 依赖库
├── resources/
│   ├── dashboard/             # Web UI 文件
│   │   ├── index.html
│   │   ├── assets/
│   │   └── ...
│   ├── app.ico                # 应用图标
│   └── tray.ico               # 托盘图标
└── ... (其他运行时文件)
```

---

## 🔄 升级步骤（详细）

### 1. 准备阶段

```powershell
# 构建最新版本
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

或使用发布脚本：

```powershell
# 自动打包
powershell -ExecutionPolicy Bypass -File .\create-release.ps1
```

### 2. 备份阶段（推荐）

```powershell
# 备份当前安装（如果需要）
# 假设你的安装目录是 C:\Program Files\Dashboard
Copy-Item "C:\Program Files\Dashboard" "C:\Program Files\Dashboard_backup_$(Get-Date -Format 'yyyyMMdd')" -Recurse
```

### 3. 停止应用

- 右键托盘图标 → 退出
- 或通过任务管理器结束进程

### 4. 复制文件

**如果使用 create-release.ps1 生成的 ZIP：**
1. 解压 ZIP 文件
2. 复制解压后的所有文件
3. 粘贴到安装目录（覆盖）

**如果手动从 publish 复制：**
```powershell
# 复制所有文件到目标目录
# 替换 <目标目录> 为你的实际安装路径
$source = "bin\Release\net9.0-windows\win-x64\publish\*"
$destination = "<目标目录>"

Copy-Item -Path $source -Destination $destination -Recurse -Force
```

### 5. 运行新版本

双击 `Dashboard.exe` 或创建快捷方式运行。

---

## 🛠️ 一键部署脚本

如果你想自动化整个过程，可以使用这个脚本：

```powershell
# deploy-to-directory.ps1
param(
    [Parameter(Mandatory=$true)]
    [string]$TargetDirectory
)

$ErrorActionPreference = 'Stop'

# 1. 构建
Write-Host "==> Building application..." -ForegroundColor Cyan
powershell -ExecutionPolicy Bypass -File .\build.ps1

# 2. 检查目标目录
if (-not (Test-Path $TargetDirectory)) {
    Write-Host "Error: Target directory not found: $TargetDirectory" -ForegroundColor Red
    exit 1
}

# 3. 检查应用是否在运行
$process = Get-Process -Name "Dashboard" -ErrorAction SilentlyContinue
if ($process) {
    Write-Host "Warning: Dashboard is currently running!" -ForegroundColor Yellow
    $response = Read-Host "Stop it now? (Y/n)"
    if ($response -eq '' -or $response -eq 'Y' -or $response -eq 'y') {
        Stop-Process -Name "Dashboard" -Force
        Start-Sleep -Seconds 2
    } else {
        Write-Host "Please close Dashboard manually and run this script again." -ForegroundColor Yellow
        exit 1
    }
}

# 4. 复制文件
Write-Host "==> Copying files to $TargetDirectory..." -ForegroundColor Cyan
$source = "bin\Release\net9.0-windows\win-x64\publish\*"
Copy-Item -Path $source -Destination $TargetDirectory -Recurse -Force

Write-Host "==> Deployment completed!" -ForegroundColor Green
Write-Host ""
Write-Host "You can now run Dashboard from:" -ForegroundColor Yellow
Write-Host "    $TargetDirectory\Dashboard.exe" -ForegroundColor White
```

使用方法：

```powershell
# 替换为你的实际安装路径
powershell -ExecutionPolicy Bypass -File .\deploy-to-directory.ps1 -TargetDirectory "C:\Program Files\Dashboard"
```

---

## 📊 验证部署

部署完成后，验证以下内容：

### 启动检查
- [ ] 应用能正常启动
- [ ] 托盘图标显示正常
- [ ] 窗口能正常显示

### 功能检查
- [ ] 能启动/停止内核
- [ ] 日志正常显示
- [ ] 设置能正常保存
- [ ] 托盘菜单响应快速

### 性能检查
- [ ] 启动速度明显变快
- [ ] 内存占用降低
- [ ] CPU 使用率降低
- [ ] UI 响应流畅

---

## 🐛 问题排查

### 应用无法启动

**症状**: 双击 Dashboard.exe 没有反应

**解决方案**:
1. 确认已安装 .NET 9 Desktop Runtime
2. 确认已安装 WebView2 Runtime
3. 检查是否复制了所有文件（不只是 .exe）
4. 查看 `%LOCALAPPDATA%\Dashboard\crash.log` 日志

### 缺少文件错误

**症状**: 提示找不到 DLL 或资源文件

**解决方案**:
- 重新复制 publish 文件夹的**所有内容**
- 确保 `resources` 文件夹完整复制

### UI 不显示

**症状**: 应用启动但窗口空白或报错

**解决方案**:
1. 确认 WebView2 Runtime 已安装
2. 确认 `resources/dashboard` 文件夹存在且包含内容
3. 清除 WebView2 缓存：删除 `%LOCALAPPDATA%\Dashboard\WebView2`

---

## 💡 推荐工作流

### 日常开发
```powershell
# 快速构建测试（跳过前端）
powershell -ExecutionPolicy Bypass -File .\build.ps1 -SkipDashboardBuild
```

### 发布新版本
```powershell
# 完整构建并打包
powershell -ExecutionPolicy Bypass -File .\create-release.ps1
```

### 快速部署到测试环境
```powershell
# 一键部署
powershell -ExecutionPolicy Bypass -File .\deploy-to-directory.ps1 -TargetDirectory "D:\Test\Dashboard"
```

---

**需要帮助？** 查看 `releases` 文件夹中的 RELEASE_NOTES 文件获取更多信息。
