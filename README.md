# JobRadar

A Blazor web application for tracking job applications and radar.

## Tech Stack

- **Framework**: ASP.NET Core / Blazor
- **Database**: SQLite
- **Language**: C# / .NET

## Getting Started

### Prerequisites

- [.NET SDK](https://dotnet.microsoft.com/download) (see `JobRadar.csproj` for target version)

### Run Locally

```bash
dotnet restore
dotnet run
```

The app will be available at `https://localhost:5001` (or the port shown in the console).

## Project Structure

| Folder | Purpose |
|--------|---------|
| `Components/` | Blazor UI components |
| `Data/` | Data access layer |
| `Models/` | Domain models |
| `Services/` | Business logic services |
| `wwwroot/` | Static assets |
| `Properties/` | Launch settings |
