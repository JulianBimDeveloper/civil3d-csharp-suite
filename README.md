# Civil 3D C# Add-in Suite (XTS Civil 3D Suite)

A professional C# extension suite built for **Autodesk Civil 3D**. This plugin extends Civil 3D's capabilities by integrating dynamic cloud database synchronization (**PocketBase**), evolves standard IFC export flows with custom property mapping, and implements enterprise-level telemetry and security checks.

---

## 🚀 Key Features

### 1. Cloud Synchronization & Database Bridge
-   **PocketBase Integration**: Syncs infrastructure elements (Alignments, Profiles, and Corridor parameters) to a cloud backend via asynchronous REST API services.
-   **Loader & McpBridge**: Implements assembly loading routines (`Loader.cs`) and TCP/UDP communication bridges (`McpBridge.cs`) for inter-process communications.

### 2. IFC Schema & Parameter Injection
-   **Custom Property Mapping**: Implements C# classes under the `IFC/` namespace to map custom database schemas to Civil 3D elements before IFC export.
-   **Dynamic Injector**: Automates the creation and insertion of property sets (`IfcProjectInjector.cs`) to ensure compliance with BIM standards that Civil 3D doesn't natively support in standard IFC exports.

### 3. Enterprise Quality Controls
-   **Security Manager**: Handles user authorization checks before executing write transactions on drawings.
-   **Telemetry Tracking**: Implements a lightweight `TelemetryManager` to profile execution speeds, log command usage, and monitor performance.

---

## 📂 Codebase Breakdown

-   **[App.cs](App.cs)**: Implements the Ribbon panel loader and buttons inside Civil 3D's UI.
-   **[Commands.cs](Commands.cs)**: Implements the external command logic for alignment exports, parameter updates, and cache synchronization.
-   **[Loader.cs](Loader.cs)**: Handles dependency loading and assembly resolution.
-   **[PocketBaseService.cs](PocketBaseService.cs)**: Communicates asynchronously with the PocketBase database.
-   **[SecurityManager.cs](SecurityManager.cs)** & **[TelemetryManager.cs](TelemetryManager.cs)**: Enterprise monitoring, command profiling, and authentication checks.
-   **`IFC/` Namespace**:
    -   **[IfcProjectInjector.cs](IFC/IfcProjectInjector.cs)**: Automates the property injection on the drawing's database.
    -   **[IfcMappingDeployer.cs](IFC/IfcMappingDeployer.cs)**: Loads mapping rules from the database and maps them to Civil 3D objects.
    -   **[ProjectParamsMap.cs](IFC/ProjectParamsMap.cs)**: Model class for data schema maps.

---

## ⚙️ Compilation & Setup

### Prerequisites
-   **Civil 3D SDK** (or Civil 3D installed locally).
-   **Target Framework**: **.NET Framework 4.8** or **.NET 8.0** (depending on your target Civil 3D version; Civil 3D 2025 uses .NET 8.0).
-   **NuGet Packages**:
    -   `System.Text.Json` (JSON parsing)

### Setup
1.  Open the project in Visual Studio.
2.  Resolve reference assemblies to Autodesk DLLs (e.g., `AecBaseMgd.dll`, `AeccDbMgd.dll`, `accoremgd.dll`, `acdbmgd.dll`, `acmgd.dll`) located in your Civil 3D installation directory.
3.  Build the solution in `Release` mode.
4.  Load the compiled assembly in Civil 3D using the `NETLOAD` command.
