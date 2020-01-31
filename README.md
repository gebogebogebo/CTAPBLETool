# CTAPBLETool
- src
  - Visual Studio 2019
  - WPF Application(.net Framework)
  - .net Framework 4.6
  - app.manifest
    - requestedExecutionLevel level="requireAdministrator"
  - Manually add WinRT API
    - [UwpDesktopでわけのわからないエラーが出るようになってしまったメモ](https://qiita.com/gebo/items/d625d77c720403d31db9)
- bin
  - Release Build Module

# CTAPBLETool2
- src2
  - Visual Studio 2019
  - WPF Application(.net Framework)
  - **.net Framework 4.6.1**
  - app.manifest
    - requestedExecutionLevel level="requireAdministrator"
  - Use WinRT API Pack
    - nuget - Microsoft.Windows.SDK.Contracts for Win10 1903
    - Microsoft.Windows.SDK.Contracts Version 10.0.18362.2005
    - [Windows 10 WinRT API Packs released](https://blogs.windows.com/windowsdeveloper/2019/09/30/windows-10-winrt-api-packs-released/)
- bin2
  - Release Build Module
