﻿# 本脚本用于实际项目中主体框架升级，升级之前会自动备份。

$start = Get-Date
#----------------------------------------------------设置路径--------------------------------------------------------------------------------
$invocation = (Get-Variable MyInvocation).Value
$directorypath = Split-Path $invocation.MyCommand.Path
$psPath = [io.Path]::Combine($directorypath, "functions.ps1");
Import-Module $psPath

Write-Host $directorypath -BackgroundColor Black -ForegroundColor Red
Set-Location $directorypath
$rootDir = [io.Directory]::GetParent($directorypath);
$sourceCodePath = [io.Path]::Combine($rootDir, "src");
if (![io.Directory]::Exists($sourceCodePath)) {
    Write-Error "目录不存在，此结构不支持目前版本的自动升级，请手动升级。$sourceCodePath";
    return;
}
#---------------------------------------------------------------------------------------------------------------------------------------------

#----------------------------------------------------任务定义--------------------------------------------------------------------------------

#备份任务
$bakTask = { 
    Write-Host "正在备份当前代码：$sourceCodePath";
    # 备份路径
    $bakPath = [io.Path]::Combine($directorypath, "bak", "src");
    CopyItemsWithFilter -sPath $sourceCodePath -dPath $bakPath;

}

#创建临时目录
$tempTask = { 
    Write-Host "正在创建临时工作区";
    $sourcePath = [io.Path]::Combine($directorypath, "source")
    # 备份路径
    $tempPath = [io.Path]::Combine($directorypath, "temp");
    CopyItemsWithFilter -sPath $sourcePath -dPath $tempPath;
}


#获取框架最新代码
$gitCloneTask = {
    $sourcePath = [io.Path]::Combine($directorypath, "source")
    $gitCmd = "git clone https://gitee.com/xl_wenqiang/Magicodes.Admin.Core.git" + " " + $sourcePath;
    if (![io.Directory]::Exists($sourcePath)) {
        [io.Directory]::CreateDirectory($sourcePath);
    }
    else {
        $gitCmd = "git pull";
    }
    Set-Location $sourcePath
    
    $cmdString = $gitCmd;
    Write-Host $cmdString
    cmd /c $cmdString
}

#复制项目代码
$upgradeTask = {
    Write-Warning '正在准备升级...';
    $paths = 'admin\api\Admin.Application.Custom', 'admin\api\Admin.Host\appsettings.json', 'admin\api\Admin.Host\appsettings.production.json', 'admin\api\Admin.Host\appsettings.Staging.json', 'admin\ui\nswag\service.config.nswag', 'admin\ui\src\app\admin\admin.module.ts', 'admin\ui\src\app\admin\admin-routing.module.ts', 'admin\ui\src\app\shared\layout\nav\app-navigation.service.ts', 'admin\ui\src\shared\service-proxies\service-proxy.module.ts', 'app\api\App.Application', 'app\api\App.Host\appsettings.json', 'app\api\App.Host\appsettings.production.json', 'app\api\App.Host\appsettings.Staging.json', 'core\Magicodes.Admin.Core.Custom', 'unity\Magicodes.Pay\Startup', 'app\api\App.Tests';

    
    $bakPath = [io.Path]::Combine($directorypath, "bak", "src");
    $tempPath = [io.Path]::Combine($directorypath, "temp", "src");
    #覆盖自定义代码
    CopyItemsByPaths -paths $paths -path $bakPath -destination $tempPath

    #后台UI合并
    $adminUIPath = [io.Path]::Combine($bakPath, "admin\ui\src\app\admin");
    $dAdminUIPath = [io.Path]::Combine($tempPath, "admin\ui\src\app\admin");
    $adminDirs = [io.Directory]::GetDirectories($adminUIPath);
    foreach ($item in $adminDirs) {
        if (",articleInfos,articleSourceInfos,audit-logs,columnInfos,components,dashboard,demo-ui-components,editions,install,languages,maintenance,organization-units,roles,settings,shared,subscription-management,tenants,ui-customization,users,".IndexOf(',' + $item.Name + ',') = -1) {
            Copy-Item -Path  $item  -Destination $dAdminUIPath  -Recurse  -Force
        }
    }
    #升级部分工程文件
    $frmPath = [io.Path]::Combine($directorypath, "source", "src");
    $replacePaths = "admin\api\Admin.Application.Custom\Admin.Application.Custom.csproj;app\api\App.Tests\App.Tests.csproj".Split(';');
    CopyItemsByPaths -paths $replacePaths -path $frmPath -destination $tempPath

    #执行清理
    $clearPaths = "documents;build;docker;res;softs;tools;migration.bat;README.md;.git;package-lock.json;Magicodes.Admin源码高级版授权合同.doc;Magicodes.Admin源码基础版授权合同.doc;build-mvc.ps1;build-with-ng.ps1".Split(';');
    foreach ($item in $clearPaths) {
        $path = [io.Path]::Combine($directorypath, "temp", $item);
        Write-Warning "正在清理：$path"
        RemoveItems -path $path
    }
    Write-Host -ForegroundColor Red '升级完成！请执行数据库迁移、部分项目包版本合并，以及检查代码是否合并正确。（建议使用版本库比较工具仔细比较）';
}
#---------------------------------------------------------------------------------------------------------------------------------------------

#----------------------------------------------------执行任务--------------------------------------------------------------------------------
Invoke-Command -ScriptBlock $bakTask
# Invoke-Command -ScriptBlock $gitCloneTask
# Invoke-Command -ScriptBlock $tempTask
# Invoke-Command -ScriptBlock $upgradeTask
#----------------------------------------------------------------------------------------------------------------------------------------------
$end = Get-Date
Write-Host -ForegroundColor Red "执行时间"+ ($end - $start).TotalSeconds