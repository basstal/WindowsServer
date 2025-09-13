# WindowsService

## 发布程序

```
Stop-Service HttpCheckService
dotnet publish -c Release -r win-x64 -o publish
Start-Service HttpCheckService
```
