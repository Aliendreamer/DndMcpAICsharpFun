## ADDED Requirements

### Requirement: UI source grouped under CompanionUI

The project SHALL have a `CompanionUI/` folder at the root containing all Blazor UI source:
`CompanionUI/Components/` (Razor components) and `CompanionUI/wwwroot/` (CSS, JS static assets).

#### Scenario: Components folder relocated

- **WHEN** a developer opens the project root
- **THEN** they SHALL find `CompanionUI/Components/` containing all `.razor` files and no `Components/` folder at the root

#### Scenario: wwwroot folder relocated

- **WHEN** a developer opens the project root
- **THEN** they SHALL find `CompanionUI/wwwroot/` containing `app.css` and `app.js` and no `wwwroot/` folder at the root

### Requirement: Static files served correctly after move

The application SHALL serve static assets (`app.css`, `app.js`) from `CompanionUI/wwwroot/` at runtime.

#### Scenario: App loads with styles

- **WHEN** the application starts and a browser navigates to the Blazor UI
- **THEN** the page SHALL load `app.css` with HTTP 200 and styles SHALL be applied correctly

### Requirement: Orphaned DndMcpAICompanion folder removed

The `DndMcpAICompanion/` folder (containing only `bin/` and `obj/`, no source files) SHALL be deleted from the repository.

#### Scenario: Folder absent after cleanup

- **WHEN** a developer lists the project root
- **THEN** `DndMcpAICompanion/` SHALL NOT be present

### Requirement: Build succeeds after reorganization

The project SHALL build without errors after the folder move and configuration updates.

#### Scenario: dotnet build passes

- **WHEN** `dotnet build` is run after all changes
- **THEN** it SHALL exit with code 0 and report zero errors
