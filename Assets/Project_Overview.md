This Unity project is a **local party game framework** that allows players to use their smartphones as controllers. It features a self-hosted HTTP and WebSocket server, a dynamic controller template system, and a "Count the Coins" minigame.

## 1. Project Description
This project is designed for multiplayer "couch" gaming. It targets PCs (Windows/Standalone) as the host and mobile devices (via web browsers) as controllers.
- **Core Pillar: Zero Install.** Players join the game by scanning a QR code or entering a local URL. No app store download is required.
- **Core Pillar: Dynamic Layouts.** The PC host can change the controller layout (e.g., Joystick, D-Pad, or Quiz buttons) in real-time based on the current game state.
- **Core Pillar: Low Latency.** Communication happens over a local WebSocket server to minimize input lag.

## 2. Gameplay Flow / User Loop
1.  **Boot & Lobby:** The game starts in the `Main` scene. The `MobileServer` starts listening, and a QR code/URL is displayed via `ConnectionScreenUI`.
2.  **Joining:** Players navigate to the URL. The `MobileServer` serves `controller.html` from `StreamingAssets`. Players enter a nickname, sending a `join` message.
3.  **Active Session:** Once players are connected, the `GameSelect` scene allows the host to pick a game.
4.  **Minigame Loop:**
    *   The host loads a minigame scene (e.g., `CoinGame`).
    *   The game system (e.g., `CoinCountGame`) requests a specific `MobileTemplate`.
    *   Players provide input via their phones.
    *   The game resolves, awards scores to `PlayerInputData`, and returns to the lobby.
5.  **Shutdown:** The server stops when the application quits or the `MobileServer` component is destroyed.

## 3. Architecture
The project follows a **Centralized Controller Pattern** where the PC acts as the authoritative server for both the network and the game logic.
*   **Networking:** A low-level `TcpListener` manages raw socket connections on background threads, but queues events to be processed on the Unity main thread for thread safety.
*   **Data Flow:** Mobile Input -> `MobileServer` -> `PlayerConnectionManager` -> `PlayerInputData` -> Minigame Logic.
*   **Singleton Pattern:** Key managers (`GameManager`, `MobileServer`, `PlayerConnectionManager`) use the Singleton pattern and `DontDestroyOnLoad` to persist across scenes.

## 4. Game Systems & Domain Concepts

### Networking & Messaging
The project implements a custom HTTP/WebSocket hybrid server to bypass the need for external hosting or specialized client apps.
*   `MobileServer`: A self-hosted server that serves the static HTML controller and handles WebSocket upgrades.
*   `NetMessage`: A flat serializable struct used for all communication.
*   `QrCodeGenerator`: Generates a QR code from the local IP address for easy joining.
*   `Location:` `Assets/Scripts/Network/`

### Player & Input Management
Inputs are decoupled from the networking layer through a persistent data model.
*   `PlayerConnectionManager`: Tracks connected clients and spawns a `PlayerInputData` object for each.
*   `PlayerInputData`: A component that holds the state (Nickname, Score, Current Inputs) for a specific player.
*   `MobileTemplate`: An enum defining available controller layouts (Joystick, D-Pad, etc.).
*   `Location:` `Assets/Scripts/Game/`

### Coordinator System
The high-level bridge between the networking backend and the game logic.
*   `GameManager`: Provides a high-level API for switching controller templates globally or per-player.
*   `SceneNavigator`: Handles scene transitions and constants.
*   `Location:` `Assets/Scripts/Game/`

## 5. Scene Overview
*   **Main (Assets/Scenes/Main.unity):** Entry point. Initializes the server and displays the connection UI for players to join.
*   **GameSelect (Assets/Scenes/GameSelect.unity):** A lobby where the host can see joined players and choose a minigame.
*   **CoinGame (Assets/Scenes/CoinGame.unity):** The "Count the Coins" minigame scene.
*   **Scene Flow:** Regulated by `SceneNavigator.cs`, which contains string constants for scene names to prevent magic-string errors.

## 6. UI System
The project uses the **Unity UI (uGUI)** system.
*   **Host UI:** Mostly static or procedurally updated (e.g., `PlayerScoreboard`).
*   **Minigame UI:** `CoinCountGame.cs` builds its gameplay UI (Answer boxes, Status text) procedurally at runtime using `CircleSpriteFactory`.
*   **Mobile UI:** Defined in `StreamingAssets/controller.html`. This is a vanilla HTML/JS file that renders different layouts based on the `template` message received from the PC.
*   **Extending:** To add a new screen, create a new uGUI prefab or scene. To add a mobile screen, modify `controller.html` and the `MobileTemplate` enum.

## 7. Asset & Data Model
*   **ScriptableObjects:** Not heavily used; state is primarily managed via MonoBehaviours and the `NetMessage` struct.
*   **StreamingAssets:** Contains `controller.html`. This is critical as the `MobileServer` reads this file to serve it to connecting phones.
*   **Networking Protocol:** Uses `UnityEngine.JsonUtility` for fast serialization of the flat `NetMessage` struct.
*   **Organization:** 
    *   `Prefabs/`: Contains reusable UI and game elements.
    *   `Scripts/Game/`: Contains high-level game logic.
    *   `Scripts/Network/`: Contains low-level socket and protocol logic.

## 8. Notes, Caveats & Gotchas
*   **Network Environment:** The PC and mobile devices **must** be on the same Local Area Network (LAN). Firewalls on the PC may need to allow traffic on port 8080.
*   **Mobile Browser Sleep:** Mobile browsers may drop WebSocket connections if the screen turns off. The `PlayerConnectionManager` handles the `ClientDisconnected` event by destroying the associated `PlayerInputData`.
*   **Template Sync:** When calling `GameManager.Instance.SetMobileTemplate`, the server broadcasts a message to all phones. If a player joins *after* a template is set, `PlayerConnectionManager` ensures they receive the current `defaultTemplate`.
*   **Thread Safety:** The `MobileServer` processes socket data on background threads but uses a `ConcurrentQueue<Action>` to ensure all Unity API calls happen on the main thread in `Update()`.