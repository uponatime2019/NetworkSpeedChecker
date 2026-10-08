# Contributing to Network Speed Checker

Thank you for your interest in contributing to **Network Speed Checker**! We welcome community contributions, bug reports, feature ideas, and documentation enhancements.

## 🤝 Code of Conduct

We are committed to providing a friendly, welcoming, and inclusive experience for everyone. Please be respectful and considerate in all communications and collaborations.

## 🛠️ How to Contribute

### 1. Reporting Bugs
- Search existing issues to ensure the problem hasn't already been reported.
- If you find a new bug, please open an issue detailing:
  - Windows version and build (e.g. Windows 11 23H2)
  - Clear steps to reproduce the issue
  - Expected vs. actual behavior
  - Diagnostic logs if relevant (`%LocalAppData%\NetworkSpeedChecker\NetworkSpeedChecker.log`)

### 2. Suggesting Features
- Open an issue explaining your proposed feature or enhancement.
- Explain why this feature would be valuable and describe how you envision the user experience.

### 3. Submitting Pull Requests
1. **Fork** the repository and create your feature branch:
   ```bash
   git checkout -b feature/awesome-feature
   ```
2. **Build and test** locally:
   - Ensure the solution builds with 0 errors and 0 warnings:
     ```bash
     dotnet build NetworkSpeedChecker.csproj -p:Platform=x64
     ```
   - Verify self-contained publish works:
     ```bash
     dotnet publish NetworkSpeedChecker.csproj -c Release -p:Platform=x64 -o publish_test
     ```
3. **Commit** your changes with clear, descriptive commit messages.
4. **Push** your branch to your fork:
   ```bash
   git push origin feature/awesome-feature
   ```
5. Open a **Pull Request** against the `main` branch.

## 💻 Development Prerequisites

- Windows 10 (version 1809 / build 17763) or Windows 11
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Visual Studio 2022 (v17.8+) with the **.NET Desktop Development** workload and **Windows App SDK C# Templates**
