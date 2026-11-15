# Spec: `foundation` — SplitTrip Core Architecture, Identity & Trip Workspace

## Objective

Establish the 5-layer PRN222 solution architecture on `.NET 10.0`, core relational schema (`SplitTripDbContext`), cookie-based authentication (including the `appsettings.json` System Admin account), user bank profile configuration (for downstream VietQR generation), trip lifecycle state machine, invite-code membership with financial leave-guards, and the base `SignalR` `TripHub` room infrastructure.

### Core Domain Rules & Invariants
1. **Strict Layered Access:** `SplitTrip.Web` (`PageModel` / `Hub` / `BackgroundService`) communicates **only** with `SplitTrip.Services` interfaces. `Services` call `Repositories`, `Repositories` call `DataAccessObjects`, and only `DataAccessObjects` touch `SplitTripDbContext`.
2. **Trip State Machine:**
   - `Planning` → `Ongoing` → `Settling` → `Completed` (Trip Owner may reopen `Settling` → `Ongoing` if an omitted expense must be added).
3. **Member Leave / Removal Guard:**
   - A `TripMember` with zero financial history may be removed cleanly.
   - Once a member participates in any expense or settlement, they **cannot** leave or be removed while their `NetBalance != 0`; if `NetBalance == 0` with historical records, membership transitions to `IsActive = false` to preserve ledger integrity.
4. **Currency Precision:** All monetary columns use `decimal(18, 0)` (whole `VND`, no floating-point types).

## Tech Stack

- **Runtime & SDK:** `.NET 10.0` (`net10.0`)
- **Web & Real-time:** `ASP.NET Core Razor Pages` + `ASP.NET Core SignalR`
- **ORM & Database:** `Microsoft.EntityFrameworkCore.SqlServer` (`10.0.*`), Code-First Migrations + generated SQL export (`database/SplitTrip_Schema.sql`)
- **Authentication:** ASP.NET Core Cookie Authentication + `BCrypt.Net-Next` password hashing; System Admin credentials loaded via `IOptions<AdminAccountOptions>` from `appsettings.json`
- **Testing:** `xUnit`, `Microsoft.EntityFrameworkCore.InMemory` / SQLite in-memory for DAO/Service verification

## Commands

```powershell
# Restore & Build entire solution
dotnet build SplitTrip.sln --configuration Debug

# Run all unit & integration tests
dotnet test SplitTrip.sln --no-build --verbosity normal

# Add a new EF Core migration
dotnet ef migrations add <MigrationName> --project src/SplitTrip.DataAccessObjects --startup-project src/SplitTrip.Web

# Apply migrations to local SQL Server / LocalDB
dotnet ef database update --project src/SplitTrip.DataAccessObjects --startup-project src/SplitTrip.Web

# Export idempotent SQL script for submission
dotnet ef migrations script --idempotent --project src/SplitTrip.DataAccessObjects --startup-project src/SplitTrip.Web --output database/SplitTrip_Schema.sql

# Run Web application locally
dotnet run --project src/SplitTrip.Web
```

## Project Structure

```text
SplitTrip.sln
├── CAPABILITY-MAP.md                 → Module decomposition & build order
├── SPEC-foundation.md                → Specification for foundation module
├── tasks/                            → Implementation plans & task checklists
├── database/                         → Exported SQL Server scripts for submission
├── src/
│   ├── SplitTrip.BusinessObjects/    → Entities, Enums, Option models (zero EF Web dependencies)
│   ├── SplitTrip.DataAccessObjects/  → SplitTripDbContext, Migrations, Entity configurations, DAOs
│   ├── SplitTrip.Repositories/       → I*Repository interfaces & Repository implementations
│   ├── SplitTrip.Services/           → I*Service interfaces, DTOs, domain validation & algorithms
│   └── SplitTrip.Web/                → Razor Pages, SignalR Hubs (TripHub), BackgroundServices, wwwroot
└── tests/
    └── SplitTrip.Tests/              → xUnit tests for domain invariants, services, and algorithms
```

## Code Style

- File-scoped namespaces, `nullable enable`, `implicit usings` enabled.
- Async suffixes (`Async`) and `CancellationToken` propagation on I/O methods.
- Constructor Dependency Injection across all layers.

```csharp
namespace SplitTrip.Services.Trips;

public sealed class TripService(
    ITripRepository tripRepository,
    ITripMemberRepository memberRepository) : ITripService
{
    public async Task RemoveMemberAsync(Guid tripId, Guid actorUserId, Guid targetUserId, CancellationToken ct = default)
    {
        var trip = await tripRepository.GetByIdWithMembersAsync(tripId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy chuyến đi.");

        if (trip.OwnerUserId != actorUserId && actorUserId != targetUserId)
        {
            throw new UnauthorizedAccessException("Chỉ trưởng đoàn hoặc chính thành viên đó mới có quyền thực hiện.");
        }

        var netBalance = await memberRepository.GetMemberNetBalanceAsync(tripId, targetUserId, ct);
        if (netBalance != 0m)
        {
            throw new InvalidOperationException("Không thể rời chuyến đi khi số dư công nợ chưa tất toán về 0đ.");
        }

        await memberRepository.DeactivateOrRemoveAsync(tripId, targetUserId, ct);
    }
}
```

## Testing Strategy

- **Unit Tests (`tests/SplitTrip.Tests`):**
  - Trip state transition rules (`Planning → Ongoing → Settling → Completed` and invalid transitions).
  - Member join via invite code, duplicate join prevention, and `NetBalance != 0` leave/kick rejection.
  - Authentication service verifying both database users and `appsettings.json` Admin login.
- **Build & Migration Verification:** Every schema commit must compile cleanly (`dotnet build`) and generate valid EF Core migrations + SQL script.

## Boundaries

- **Always:**
  - Commit and push **every small atomic slice immediately** (dense, incremental git history — never batch an entire feature into one commit).
  - Run `dotnet build` and `dotnet test` before every commit.
  - Keep `ConnectionStrings`, `AdminAccount`, and `Gemini` settings configurable via `appsettings.json` / User Secrets.
  - Enforce 5-layer dependency rules (`Web → Services → Repositories → DataAccessObjects → BusinessObjects`).
- **Ask first:**
  - Changing approved module boundaries in `CAPABILITY-MAP.md`.
  - Adding paid or heavyweight third-party dependencies.
- **Never:**
  - Reference `SplitTrip.DataAccessObjects` or `SplitTripDbContext` directly from `SplitTrip.Web`.
  - Commit real API keys or secrets into tracked `appsettings.json`.
  - Use `float` or `double` for currency calculations.

## Success Criteria

- [ ] `SplitTrip.sln` builds with 0 errors across all 6 projects (`5 src + 1 test`) on `net10.0`.
- [ ] Complete EF Core domain entities & `SplitTripDbContext` initial migration apply cleanly to SQL Server LocalDB and export `database/SplitTrip_Schema.sql`.
- [ ] Users can register, sign in/out via Cookie Auth, update bank details (Bank bin/code + Account number + Account holder name for VietQR), and System Admin can sign in from `appsettings.json`.
- [ ] Authenticated users can create a Trip, generate/share a unique Invite Code, join a Trip, view members, transition Trip status, and enforce the `NetBalance == 0` leave guard.
- [ ] `TripHub` (`SignalR`) authenticates connected members, joins them into `trip:{tripId}` SignalR groups, and broadcasts live membership/status changes across tabs.
- [ ] Unit tests in `SplitTrip.Tests` pass (`dotnet test`).
