## 1. Companion Database

- [x] 1.1 Add `Microsoft.Data.Sqlite` package reference to `DndMcpAICompanion.csproj`
- [x] 1.2 Create `Features/Auth/UserRepository.cs` — initializes `data/companion.db`, creates `Users` table if not exists, exposes `FindByUsernameAsync`, `CreateAsync`, `ExistsAsync`
- [x] 1.3 Add `Data:CompanionDb` path (`data/companion.db`) to `Config/appsettings.json` and register `UserRepository` as a scoped service in `Program.cs`
- [x] 1.4 Add `companion_data` volume to `docker-compose.yml` and mount it at `/app/data` for the `companion` service

## 2. Password Hashing

- [x] 2.1 Create `Features/Auth/PasswordHasher.cs` — static class with `Hash(string password)` using `Rfc2898DeriveBytes` (PBKDF2/SHA-256, 100,000 iterations, 16-byte salt) and `Verify(string password, string hash)` returning bool
- [x] 2.2 Write unit tests for `PasswordHasher` — correct password verifies, wrong password does not, hash is different on each call

## 3. Cookie Authentication

- [x] 3.1 Register cookie authentication in `Program.cs`: `builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o => { o.LoginPath = "/login"; o.LogoutPath = "/logout"; })`
- [x] 3.2 Add `app.UseAuthentication()` and `app.UseAuthorization()` to the middleware pipeline before `app.UseAntiforgery()`

## 4. Register and Login Pages

- [x] 4.1 Create `Components/Pages/Register.razor` at route `/register` — form with Username, Password, Confirm Password fields; calls `UserRepository` and `PasswordHasher`; on success signs in via `HttpContext.SignInAsync` and redirects to `/`
- [x] 4.2 Create `Components/Pages/Login.razor` at route `/login` — form with Username and Password; verifies via `UserRepository` + `PasswordHasher`; on success signs in and redirects to `/`; on failure shows "Invalid username or password"
- [x] 4.3 Add logout endpoint: `app.MapGet("/logout", ...)` that calls `HttpContext.SignOutAsync` and redirects to `/login`

## 5. Protect Chat Page

- [x] 5.1 Add `[Authorize]` attribute (or `<AuthorizeView>` redirect logic) to `Components/Pages/Chat.razor` so unauthenticated users are redirected to `/login`
- [x] 5.2 Add username display and Logout button to the chat page header — username from `AuthenticationState.User.Identity.Name`, logout via `NavigationManager.NavigateTo("/logout", forceLoad: true)`
- [x] 5.3 Add `CascadingAuthenticationState` and `AuthorizeRouteView` to `Components/Routes.razor`

## 6. Rate Limiter

- [x] 6.1 Add `RateLimit:MessagesPerMinute` (default `10`) to `Config/appsettings.json` and create `Features/Chat/RateLimitOptions.cs`
- [x] 6.2 Create `Features/Chat/ChatRateLimiter.cs` — singleton service with `TryAcquire(string ip)` using a `ConcurrentDictionary<string, WindowCounter>` (count + window start timestamp); returns false when limit exceeded
- [x] 6.3 Register `IHttpContextAccessor` (`builder.Services.AddHttpContextAccessor()`) and inject it into `DndChatService`
- [x] 6.4 At the top of `DndChatService.SendAsync`, call `ChatRateLimiter.TryAcquire(ip)`; if false, add error message to history and return without calling Ollama

## 7. Tests

- [x] 7.1 Add unit tests for `ChatRateLimiter` — messages under limit pass, messages over limit are rejected, counter resets after window expires
- [x] 7.2 Run `dotnet build` and `dotnet test` — all tests pass
